using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Collections.Generic;

namespace SesliOkuma
{
    // Support link is read from the repository so it can change without a new release.
    public static class SupportLink
    {
        const string Json = "https://raw.githubusercontent.com/korayorhun/SesliOkuma/main/docs/support.json";
        public static string Url = "https://github.com/sponsors/korayorhun";
        public static string Label = "GitHub Sponsors";
        public static bool Enabled = true;
        static bool _fetched;

        public static void FetchAsync(Control ui, Action done)
        {
            if (_fetched) { if (done != null) done(); return; }
            var t = new Thread(delegate ()
            {
                try
                {
                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | (SecurityProtocolType)12288;
                    var wc = new WebClient(); wc.Headers[HttpRequestHeader.UserAgent] = "SesliOkuma"; wc.Encoding = Encoding.UTF8;
                    var root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(wc.DownloadString(Json));
                    object v;
                    if (root.TryGetValue("url", out v)) { Url = Convert.ToString(v); Enabled = Url.Length > 0; }
                    if (root.TryGetValue("label", out v)) Label = Convert.ToString(v);
                    _fetched = true;
                }
                catch (Exception ex) { Logger.Log("support.json: " + ex.Message); }
                try { if (done != null && ui.IsHandleCreated) ui.BeginInvoke(done); } catch { }
            });
            t.IsBackground = true;
            t.Start();
        }
    }

    public sealed class AboutForm : Form
    {
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        const string Repo = "https://github.com/korayorhun/SesliOkuma";
        const int W = 380, Pad = 24;
        readonly FlatButton _support = new FlatButton { Primary = true, Size = new Size(W - 2 * Pad, 40) };
        readonly Label _supportNote;

        public AboutForm(Icon icon)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            Text = "Sesli Okuma - " + L.T("About");
            Icon = icon;
            DoubleBuffered = true;

            int y = 22;
            var close = new FlatButton { Text = "\uE711", IconGlyph = true, Size = new Size(36, 36), Location = new Point(W - Pad - 36, y - 2) };
            close.Click += delegate { Close(); };
            Controls.Add(close);

            var pic = new PictureBox { Image = icon.ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom, Bounds = new Rectangle(Pad, y, 44, 44), BackColor = Color.Transparent };
            Controls.Add(pic);
            Controls.Add(Make("Sesli Okuma", Theme.Title, Theme.Text, Pad + 56, y - 2, 220, 30));
            Controls.Add(Make("v" + Updater.CurrentVersionText + "  ·  MIT", Theme.Small, Theme.Muted, Pad + 56, y + 28, 220, 16));
            y += 66;

            Controls.Add(Make(L.T("Tagline"), Theme.Body, Theme.Text, Pad, y, W - 2 * Pad, 62));
            y += 72;

            y = Link(L.T("SourceCode"), "github.com/korayorhun/SesliOkuma", Repo, y);
            y = Link(L.T("ReleaseNotes2"), "", Repo + "/releases", y);
            y = Link(L.T("ReportIssue"), "", Repo + "/issues", y);
            y = Link(L.T("Credits"), "NaturalVoiceSAPIAdapter · MIT", "https://github.com/gexgd0419/NaturalVoiceSAPIAdapter", y);
            y += 6;

            Controls.Add(new Panel { BackColor = Theme.Border, Location = new Point(Pad, y), Size = new Size(W - 2 * Pad, 1) });
            y += 18;

            _support.Text = "♥  " + L.T("Support");
            _support.Location = new Point(Pad, y);
            _support.Click += delegate { Open(SupportLink.Url); };
            Controls.Add(_support);
            y += 46;
            _supportNote = Make(L.T("SupportNote") + "  ·  " + SupportLink.Label, Theme.Small, Theme.Muted, Pad, y, W - 2 * Pad, 18);
            _supportNote.TextAlign = ContentAlignment.TopCenter;
            Controls.Add(_supportNote);
            y += 30;

            ClientSize = new Size(W, y);
            _support.Visible = _supportNote.Visible = SupportLink.Enabled;
            SupportLink.FetchAsync(this, delegate
            {
                _support.Visible = _supportNote.Visible = SupportLink.Enabled;
                _supportNote.Text = L.T("SupportNote") + "  ·  " + SupportLink.Label;
            });

            MouseDown += Drag;
            foreach (Control c in Controls) if (c is Label || c is PictureBox) c.MouseDown += Drag;
        }

        int Link(string title, string sub, string url, int y)
        {
            var l = Make(title, Theme.Body, Theme.Accent, Pad, y, W - 2 * Pad, 20);
            l.Cursor = Cursors.Hand;
            l.Click += delegate { Open(url); };
            l.MouseEnter += delegate { l.ForeColor = Theme.AccentHover; };
            l.MouseLeave += delegate { l.ForeColor = Theme.Accent; };
            Controls.Add(l);
            if (sub.Length > 0) { Controls.Add(Make(sub, Theme.Small, Theme.Muted, Pad, y + 19, W - 2 * Pad, 16)); return y + 40; }
            return y + 26;
        }

        static Label Make(string text, Font font, Color color, int x, int y, int w, int h)
        {
            var l = new Label { Text = text, Font = font, ForeColor = color, BackColor = Color.Transparent, AutoSize = false };
            l.SetBounds(x, y, w, h);
            return l;
        }

        static void Open(string url) { try { Process.Start(url); } catch { } }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { int round = 2; DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int)); int dark = Theme.Dark ? 1 : 0; DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int)); } catch { }
        }

        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ClassStyle |= 0x20000; return cp; } }

        public void ShowNearTray()
        {
            var wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Right - Width - 12, wa.Bottom - Height - 12);
            Show();
            Activate();
        }

        void Drag(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero); } }


        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { Close(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
