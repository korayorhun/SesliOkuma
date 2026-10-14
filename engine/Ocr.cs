using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace SesliOkuma.Engine
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
            var v = new Rectangle(GetSystemMetrics(SM_XVIRTUALSCREEN), GetSystemMetrics(SM_YVIRTUALSCREEN), GetSystemMetrics(SM_CXVIRTUALSCREEN), GetSystemMetrics(SM_CYVIRTUALSCREEN));
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
                EngineLog.Log("ocr lang=" + engine.RecognizerLanguage.LanguageTag + " size=" + bitmap.Width + "x" + bitmap.Height + " chars=" + text.Length);
                return text.Length > 0 ? text : null;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                EngineLog.Log("ocr: " + ex.Message);
                return null;
            }
            finally { if (!ReferenceEquals(work, bitmap)) work.Dispose(); }
        }
    }
}
