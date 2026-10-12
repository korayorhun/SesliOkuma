using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace SesliOkuma
{
    // Reads text straight off the screen with the OCR engine built into Windows 10/11 — offline and free.
    // Used for content that cannot be selected: images, protected PDFs, remote desktops, custom-drawn UI.
    public static class ScreenOcr
    {
        [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
        const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77, SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;

        // The whole virtual screen in physical pixels. The app is DPI-unaware, so on a scaled display its own
        // coordinates would grab the wrong area; capture per-monitor-aware and map selections with Crop().
        public static Bitmap CaptureVirtualScreen()
        {
            IntPtr previous = IntPtr.Zero;
            try { previous = SetThreadDpiAwarenessContext(new IntPtr(-4)); } catch { }
            try
            {
                int x = GetSystemMetrics(SM_XVIRTUALSCREEN), y = GetSystemMetrics(SM_YVIRTUALSCREEN);
                int w = GetSystemMetrics(SM_CXVIRTUALSCREEN), h = GetSystemMetrics(SM_CYVIRTUALSCREEN);
                var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
                using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(x, y, 0, 0, new Size(w, h), CopyPixelOperation.SourceCopy);
                return bmp;
            }
            finally { if (previous != IntPtr.Zero) { try { SetThreadDpiAwarenessContext(previous); } catch { } } }
        }

        // Maps a rectangle in the app's logical screen space onto the physical screenshot; null when too small.
        public static Bitmap Crop(Bitmap screen, Rectangle logical)
        {
            var v = SystemInformation.VirtualScreen;
            double sx = screen.Width / (double)v.Width, sy = screen.Height / (double)v.Height;
            var r = Rectangle.FromLTRB((int)Math.Floor((logical.Left - v.X) * sx), (int)Math.Floor((logical.Top - v.Y) * sy),
                                       (int)Math.Ceiling((logical.Right - v.X) * sx), (int)Math.Ceiling((logical.Bottom - v.Y) * sy));
            r.Intersect(new Rectangle(0, 0, screen.Width, screen.Height));
            if (r.Width < 4 || r.Height < 4) return null;
            return screen.Clone(r, PixelFormat.Format24bppRgb);
        }

        // Blocking; call on a worker thread. Returns null when nothing readable was found.
        // primaryMissing: the OCR language of the user's primary language is not installed (another one was used).
        public static string Recognize(Bitmap bitmap, string lang2, out bool primaryMissing, out string error)
        {
            error = null;
            primaryMissing = false;
            Bitmap work = bitmap;
            try
            {
                OcrEngine engine = null;
                try { engine = OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language(lang2)); } catch { }
                if (engine == null)
                {
                    primaryMissing = true;
                    engine = OcrEngine.TryCreateFromUserProfileLanguages();
                    if (engine == null) { try { engine = OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en")); } catch { } }
                }
                if (engine == null) { error = "no OCR language"; return null; }

                // Small text recognizes much better when enlarged.
                uint max = OcrEngine.MaxImageDimension;
                if (bitmap.Height < 300 && bitmap.Width * 2 <= max && bitmap.Height * 2 <= max)
                {
                    work = new Bitmap(bitmap.Width * 2, bitmap.Height * 2, PixelFormat.Format24bppRgb);
                    using (var g = Graphics.FromImage(work))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(bitmap, 0, 0, work.Width, work.Height);
                    }
                }

                SoftwareBitmap soft;
                using (var ms = new MemoryStream())
                {
                    work.Save(ms, ImageFormat.Png);
                    ms.Position = 0;
                    var decoder = BitmapDecoder.CreateAsync(ms.AsRandomAccessStream()).AsTask().Result;
                    soft = decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied).AsTask().Result;
                }
                var result = engine.RecognizeAsync(soft).AsTask().Result;
                var sb = new System.Text.StringBuilder();
                foreach (var line in result.Lines)
                {
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(line.Text);
                }
                string text = sb.ToString().Trim();
                Logger.Log("ocr lang=" + engine.RecognizerLanguage.LanguageTag + " size=" + bitmap.Width + "x" + bitmap.Height + " chars=" + text.Length);
                return text.Length > 0 ? text : null;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Logger.Log("ocr: " + ex.Message);
                return null;
            }
            finally { if (!ReferenceEquals(work, bitmap)) work.Dispose(); }
        }
    }

    // Frozen screenshot of the screen to drag out the region to read (like the Snipping Tool).
    // Enter reads the whole window that was active before; Esc or right-click cancels.
    public sealed class RegionOverlay : Form
    {
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);

        readonly Bitmap _shot;              // physical-pixel screenshot taken before the overlay appeared (owned)
        readonly Bitmap _view;              // _shot scaled to the overlay's logical size, for painting
        readonly Rectangle _window;         // the previously active window, logical screen coordinates
        readonly IntPtr _previous;
        readonly Action<Bitmap> _done;      // receives the cropped physical bitmap; the receiver disposes it
        Point _start;
        Rectangle _rect;
        bool _dragging, _delivered;
        readonly Rectangle _hint;

        public RegionOverlay(Bitmap screenshot, IntPtr previous, Rectangle window, Action<Bitmap> done)
        {
            _shot = screenshot; _previous = previous; _window = window; _done = done;
            var area = SystemInformation.VirtualScreen;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = area;
            TopMost = true;
            ShowInTaskbar = false;
            BackColor = Color.Black;
            Cursor = Cursors.Cross;
            DoubleBuffered = true;
            KeyPreview = true;
            Text = "SesliOkumaOcr";
            if (_shot.Width == area.Width && _shot.Height == area.Height) _view = _shot;
            else
            {
                _view = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppPArgb);
                using (var g = Graphics.FromImage(_view))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    g.DrawImage(_shot, 0, 0, area.Width, area.Height);
                }
            }
            // The hint goes on the monitor the user is looking at, not the middle of a multi-monitor bounding box.
            var screen = Screen.FromPoint(Cursor.Position).Bounds;
            _hint = new Rectangle(screen.X - area.X, screen.Y - area.Y + 24, screen.Width, 30);
        }

        void Deliver(Rectangle screenRect)
        {
            if (_delivered) return;
            _delivered = true;
            Bitmap crop = null;
            try { crop = ScreenOcr.Crop(_shot, screenRect); } catch (Exception ex) { Logger.Log("ocr crop: " + ex.Message); }
            Close();
            if (_previous != IntPtr.Zero) SetForegroundWindow(_previous);
            if (crop != null && _done != null) _done(crop);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { e.Handled = true; Close(); if (_previous != IntPtr.Zero) SetForegroundWindow(_previous); return; }
            if (e.KeyCode == Keys.Enter) { e.Handled = true; Deliver(_window); return; }
            base.OnKeyDown(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right) { Close(); if (_previous != IntPtr.Zero) SetForegroundWindow(_previous); return; }
            _dragging = true; _start = e.Location; _rect = new Rectangle(e.Location, Size.Empty);
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!_dragging) return;
            var old = _rect;
            _rect = new Rectangle(Math.Min(_start.X, e.X), Math.Min(_start.Y, e.Y), Math.Abs(e.X - _start.X), Math.Abs(e.Y - _start.Y));
            var dirty = Rectangle.Union(old, _rect);
            dirty.Inflate(4, 4);
            Invalidate(dirty);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (!_dragging) return;
            _dragging = false;
            if (_rect.Width > 8 && _rect.Height > 8) Deliver(new Rectangle(Left + _rect.X, Top + _rect.Y, _rect.Width, _rect.Height));
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.DrawImageUnscaled(_view, 0, 0);
            using (var tint = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
            {
                if (_rect.Width > 0 && _rect.Height > 0)
                    using (var outside = new Region(ClientRectangle)) { outside.Exclude(_rect); e.Graphics.FillRegion(tint, outside); }
                else
                    e.Graphics.FillRectangle(tint, ClientRectangle);
            }
            if (_rect.Width > 0 && _rect.Height > 0)
                using (var pen = new Pen(Theme.Accent, 2f)) e.Graphics.DrawRectangle(pen, _rect.X, _rect.Y, _rect.Width, _rect.Height);
            if (e.ClipRectangle.IntersectsWith(_hint))
            {
                TextRenderer.DrawText(e.Graphics, L.T("OcrHint"), Theme.Body, new Rectangle(_hint.X + 1, _hint.Y + 1, _hint.Width, _hint.Height), Color.Black,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.Top);
                TextRenderer.DrawText(e.Graphics, L.T("OcrHint"), Theme.Body, _hint, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.Top);
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (!ReferenceEquals(_view, _shot)) _view.Dispose();
            _shot.Dispose();
            base.OnFormClosed(e);
        }
    }
}
