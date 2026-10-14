using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SesliOkuma.Engine;

namespace SesliOkuma
{
    // Card showing the original (muted) and the translation, with read / copy / close.
    public sealed class TranslationCard : Form
    {
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        const int W = 440, Pad = 20;
        readonly Label _orig = new Label { AutoSize = false, BackColor = Color.Transparent };
        readonly Label _trans = new Label { AutoSize = false, BackColor = Color.Transparent };
        readonly Label _head = new Label { AutoSize = false, BackColor = Color.Transparent };
        readonly FlatButton _play = new FlatButton { Text = "\uE768", IconGlyph = true, Primary = true, Size = new Size(40, 36) };
        readonly FlatButton _copy = new FlatButton { Text = "\uE8C8", IconGlyph = true, Borderless = true, Size = new Size(36, 36) };
        readonly FlatButton _close = new FlatButton { Text = "\uE711", IconGlyph = true, Borderless = true, Size = new Size(36, 36) };
        readonly System.Windows.Forms.Timer _anim = new System.Windows.Forms.Timer { Interval = 40 };
        int _phase; bool _busy;
        public event EventHandler PlayClicked;
        public string Translation = "";

        public TranslationCard(Icon icon)
        {
            FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual; ShowInTaskbar = false; TopMost = true;
            BackColor = Theme.Bg; ForeColor = Theme.Text; Font = Theme.Body; Icon = icon; DoubleBuffered = true; Text = "SesliOkumaTranslation";
            _head.Font = Theme.Caption; _head.ForeColor = Theme.Muted;
            _orig.Font = Theme.Small; _orig.ForeColor = Theme.Muted;
            _trans.Font = Theme.Body; _trans.ForeColor = Theme.Text;
            Controls.AddRange(new Control[] { _head, _orig, _trans, _play, _copy, _close });
            _close.Click += delegate { Close(); };
            _play.Click += delegate { if (PlayClicked != null) PlayClicked(this, EventArgs.Empty); };
            _copy.Click += delegate { try { Clipboard.SetText(Translation); } catch { } };
            MouseDown += Drag; _head.MouseDown += Drag; _orig.MouseDown += Drag; _trans.MouseDown += Drag;
            Tips.Set(_play, L.T("Listen")); Tips.Set(_copy, L.T("Copy")); Tips.Set(_close, L.T("Close"));
            _anim.Tick += delegate { _phase = (_phase + 6) % 400; Invalidate(new Rectangle(0, Height - 6, Width, 6)); };
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x08000000 | 0x00000080; cp.ClassStyle |= 0x20000; return cp; } }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { int round = 2; DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int)); int dark = Theme.Dark ? 1 : 0; DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int)); } catch { }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (!_busy) return;
            Theme.Prepare(e.Graphics);
            var track = new RectangleF(Pad, Height - 5, Width - 2 * Pad, 2);
            using (var b = new SolidBrush(Theme.Track)) e.Graphics.FillRectangle(b, track);
            float w = track.Width * 0.25f, x = track.X + (track.Width + w) * (_phase / 400f) - w;
            var seg = RectangleF.Intersect(track, new RectangleF(x, track.Y, w, track.Height));
            if (seg.Width > 0) using (var b = new SolidBrush(Theme.Accent)) e.Graphics.FillRectangle(b, seg);
        }

        public void SetContent(string sourceLang, string targetLang, string original, string translation, bool busy)
        {
            Translation = translation ?? "";
            _busy = busy; _play.Enabled = _copy.Enabled = !busy;
            if (busy) _anim.Start(); else _anim.Stop();
            _head.Text = busy ? L.T("Translating") : (sourceLang.Length > 0 ? sourceLang + "  →  " + targetLang : targetLang).ToUpperInvariant();
            _orig.Text = original.Length > 300 ? original.Substring(0, 300) + "…" : original;
            _trans.Text = busy ? "…" : Translation;
            Layout2();
        }

        void Layout2()
        {
            int textW = W - 2 * Pad;
            int y = 18;
            _head.SetBounds(Pad, y, textW - 130, 16);
            _close.Location = new Point(W - Pad - 36, y - 8); _copy.Location = new Point(W - Pad - 36 - 8 - 36, y - 8); _play.Location = new Point(W - Pad - 36 - 8 - 36 - 8 - 40, y - 8);
            y += 36;
            using (var g = CreateGraphics())
            {
                int oh = Math.Min(64, TextRenderer.MeasureText(g, _orig.Text, _orig.Font, new Size(textW, 1000), TextFormatFlags.WordBreak).Height);
                _orig.SetBounds(Pad, y, textW, oh); y += oh + 12;
                int th = Math.Min(260, Math.Max(22, TextRenderer.MeasureText(g, _trans.Text, _trans.Font, new Size(textW, 2000), TextFormatFlags.WordBreak).Height));
                _trans.SetBounds(Pad, y, textW, th); y += th + Pad;
            }
            ClientSize = new Size(W, y);
            var wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Right - Width - 12, wa.Bottom - Height - 84);
        }

        void Drag(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero); } }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData) { if (keyData == Keys.Escape) { Close(); return true; } return base.ProcessCmdKey(ref msg, keyData); }
    }
}
