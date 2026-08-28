using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace SesliOkuma
{
    // DeepL API client (free or pro key). Source language is auto-detected; target is the user's primary language.
    public static class Translator
    {
        public static string DeepLTarget(string lang2)
        {
            switch (lang2)
            {
                case "en": return "EN-US";
                case "pt": return "PT-BR";
                case "zh": return "ZH";
                case "hi": return null;                       // not offered by DeepL
                default: return lang2.ToUpperInvariant();     // TR, ES, FR, AR, DE, RU, IT, JA, KO, NL, PL…
            }
        }

        public static void TranslateAsync(Control ui, string apiKey, string text, string targetLang2, Action<string, string> done, Action<string> failed)
        {
            string target = DeepLTarget(targetLang2);
            if (target == null) { failed(L.F("TranslateUnsupported", L.NativeName(targetLang2))); return; }
            var t = new Thread(delegate ()
            {
                string translated = null, detected = null, err = null;
                try
                {
                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | (SecurityProtocolType)12288;
                    string host = apiKey.TrimEnd().EndsWith(":fx") ? "https://api-free.deepl.com" : "https://api.deepl.com";
                    var req = (HttpWebRequest)WebRequest.Create(host + "/v2/translate");
                    req.Method = "POST"; req.Timeout = 15000; req.UserAgent = "SesliOkuma";
                    req.Headers["Authorization"] = "DeepL-Auth-Key " + apiKey.Trim();
                    req.ContentType = "application/x-www-form-urlencoded";
                    byte[] body = Encoding.UTF8.GetBytes("text=" + Uri.EscapeDataString(text) + "&target_lang=" + target);
                    using (var rs = req.GetRequestStream()) rs.Write(body, 0, body.Length);
                    string json;
                    using (var resp = (HttpWebResponse)req.GetResponse())
                    using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) json = sr.ReadToEnd();
                    var root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
                    var arr = root["translations"] as System.Collections.ArrayList;
                    var first = (Dictionary<string, object>)arr[0];
                    translated = Convert.ToString(first["text"]);
                    detected = first.ContainsKey("detected_source_language") ? Convert.ToString(first["detected_source_language"]) : "";
                }
                catch (WebException wex)
                {
                    var r = wex.Response as HttpWebResponse;
                    if (r != null && (int)r.StatusCode == 403) err = L.T("TranslateBadKey");
                    else if (r != null && (int)r.StatusCode == 456) err = L.T("TranslateQuota");
                    else err = wex.Message;
                }
                catch (Exception ex) { err = ex.Message; }
                try { ui.BeginInvoke(new Action(delegate { if (err != null) { Logger.Log("translate: " + err); failed(err); } else { Logger.Log("translated " + detected + "->" + target + " len=" + translated.Length); done(translated, detected); } })); } catch { }
            });
            t.IsBackground = true; t.Start();
        }
    }

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
        readonly FlatButton _copy = new FlatButton { Text = "\uE8C8", IconGlyph = true, Size = new Size(36, 36) };
        readonly FlatButton _close = new FlatButton { Text = "\uE711", IconGlyph = true, Size = new Size(36, 36) };
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
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x08000000 | 0x00000080; cp.ClassStyle |= 0x20000; return cp; } }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { int round = 2; DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int)); int dark = Theme.Dark ? 1 : 0; DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int)); } catch { }
        }

        public void SetContent(string sourceLang, string targetLang, string original, string translation, bool busy)
        {
            Translation = translation ?? "";
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
