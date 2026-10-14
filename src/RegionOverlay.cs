using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SesliOkuma.Engine;

namespace SesliOkuma
{
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
