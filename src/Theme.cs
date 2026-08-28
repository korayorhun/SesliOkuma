using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SesliOkuma
{
    public static class Theme
    {
        public static bool Dark;
        public static Color Bg, Card, CardHover, Border, Text, Muted, Accent, AccentHover, AccentText, Track, AccentSoft;
        public static Font Body, Small, Title, Caption, Icon, Mono;

        public static void Load()
        {
            Dark = ReadDword(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) == 0;
            string forced = Environment.GetEnvironmentVariable("SESLIOKUMA_THEME");
            if (forced == "light") Dark = false; else if (forced == "dark") Dark = true;
            if (Dark)
            {
                Bg = Rgb(0x16171D); Card = Rgb(0x1F2129); CardHover = Rgb(0x272A34); Border = Rgb(0x2E3140);
                Text = Rgb(0xEDEEF2); Muted = Rgb(0x8A8FA3); Accent = Rgb(0x6C8CFF); AccentHover = Rgb(0x86A0FF);
                AccentText = Color.White; Track = Rgb(0x2E3140); AccentSoft = Rgb(0x1F2740);
            }
            else
            {
                Bg = Rgb(0xF6F7FB); Card = Color.White; CardHover = Rgb(0xF0F2F8); Border = Rgb(0xE1E4EC);
                Text = Rgb(0x1B1D24); Muted = Rgb(0x6B7080); Accent = Rgb(0x3B6CFF); AccentHover = Rgb(0x2F5BE0);
                AccentText = Color.White; Track = Rgb(0xE1E4EC); AccentSoft = Rgb(0xE8EEFF);
            }
            Body = new Font("Segoe UI", 10f);
            Small = new Font("Segoe UI", 8.5f);
            Caption = new Font("Segoe UI Semibold", 8f);
            Title = new Font("Segoe UI Semibold", 15f);
            Mono = new Font("Segoe UI Semibold", 10.5f);
            Icon = new Font("Segoe MDL2 Assets", 10f);
        }

        public static bool TaskbarIsDark()
        {
            return ReadDword(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", 0) == 0;
        }

        static int ReadDword(string key, string name, int fallback)
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(key))
                {
                    if (k == null) return fallback;
                    object v = k.GetValue(name);
                    return v is int ? (int)v : fallback;
                }
            }
            catch { return fallback; }
        }

        static Color Rgb(int hex) { return Color.FromArgb((hex >> 16) & 0xFF, (hex >> 8) & 0xFF, hex & 0xFF); }

        public static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            float d = radius * 2;
            var p = new GraphicsPath();
            if (d <= 0 || r.Width <= 0 || r.Height <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void Prepare(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        }

        // Text flags honoring right-to-left UI languages.
        public static TextFormatFlags Flags(TextFormatFlags f)
        {
            return L.IsRtl ? (f | TextFormatFlags.RightToLeft) : f;
        }
    }

    // Draws the app glyph: a speech bubble holding text lines ("text being spoken").
    public static class IconFactory
    {
        public static Icon Create(int size, Color glyph, Color fill, bool filled)
        {
            using (var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    Theme.Prepare(g);
                    float s = size / 32f;
                    using (var bubble = new GraphicsPath())
                    {
                        var body = new RectangleF(3 * s, 4 * s, 26 * s, 19 * s);
                        float r = 6 * s;
                        bubble.AddArc(body.X, body.Y, 2 * r, 2 * r, 180, 90);
                        bubble.AddArc(body.Right - 2 * r, body.Y, 2 * r, 2 * r, 270, 90);
                        bubble.AddArc(body.Right - 2 * r, body.Bottom - 2 * r, 2 * r, 2 * r, 0, 90);
                        bubble.AddLine(13 * s, body.Bottom, 8 * s, 29 * s);
                        bubble.AddLine(8 * s, 29 * s, 8.5f * s, body.Bottom);
                        bubble.AddArc(body.X, body.Bottom - 2 * r, 2 * r, 2 * r, 90, 90);
                        bubble.CloseFigure();
                        if (filled)
                        {
                            using (var b = new SolidBrush(fill)) g.FillPath(b, bubble);
                        }
                        else
                        {
                            using (var pen = new Pen(glyph, Math.Max(1.5f, 2.2f * s)) { LineJoin = LineJoin.Round }) g.DrawPath(pen, bubble);
                        }
                    }
                    Color lines = filled ? Color.White : glyph;
                    using (var pen = new Pen(lines, Math.Max(1.5f, 2.4f * s)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    {
                        g.DrawLine(pen, 9 * s, 10 * s, 23 * s, 10 * s);
                        g.DrawLine(pen, 9 * s, 14.5f * s, 20 * s, 14.5f * s);
                        g.DrawLine(pen, 9 * s, 19 * s, 16 * s, 19 * s);
                    }
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }
    }
}
