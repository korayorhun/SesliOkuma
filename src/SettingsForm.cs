using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SesliOkuma
{
    public sealed class SettingsForm : Form
    {
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        readonly TrayApp _app;
        readonly Panel _body = new Panel();
        readonly UpdateBanner _banner = new UpdateBanner();
        readonly VoicePicker _trPicker = new VoicePicker();
        readonly VoicePicker _enPicker = new VoicePicker();
        readonly Slider _rate = new Slider();
        readonly ToggleSwitch _startup = new ToggleSwitch();
        readonly ToggleSwitch _autoUpdate = new ToggleSwitch();
        readonly Label _rateValue = new Label();
        readonly Label _status = new Label();
        readonly Label _version = new Label();
        readonly Timer _statusTimer = new Timer();
        bool _loading;
        string _hint = "";
        const string AdapterUrl = "https://github.com/gexgd0419/NaturalVoiceSAPIAdapter";

        const int W = 380, Pad = 24, HeaderH = 92, BannerH = 72;
        int _bodyH;

        public SettingsForm(TrayApp app)
        {
            _app = app;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            Text = "Sesli Okuma";
            Icon = app.AppIcon;
            DoubleBuffered = true;
            Build();
            Relayout();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int round = 2; DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int));
                int dark = Theme.Dark ? 1 : 0; DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));
            }
            catch { }
        }

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ClassStyle |= 0x20000; return cp; } // CS_DROPSHADOW
        }

        void Build()
        {
            var title = MakeLabel("Sesli Okuma", Theme.Title, Theme.Text, Pad, 22, 240, 30);
            var close = new FlatButton { Text = "", IconGlyph = true, Size = new Size(36, 36), Location = new Point(W - Pad - 36, 20) };
            close.Click += delegate { Hide(); };
            Controls.Add(title); Controls.Add(close);
            title.MouseDown += DragStart;
            Controls.Add(MakeLabel("Ctrl + Alt + S  seçili metni okur  ·  tekrar basınca susar", Theme.Small, Theme.Muted, Pad, 56, W - 2 * Pad, 18));

            _banner.SetBounds(Pad, HeaderH, W - 2 * Pad, BannerH - 14);
            _banner.Visible = false;
            _banner.UpdateClicked += delegate { _app.Updater.DownloadAndInstall(_app.Updater.Available); };
            _banner.SkipClicked += delegate
            {
                if (_app.Updater.Available != null) { _app.Settings.SkipVersion = _app.Updater.Available.Version.ToString(3); _app.Settings.Save(); }
                Relayout();
                Flash("Bu sürüm atlandı");
            };
            _banner.NotesClicked += delegate { if (_app.Updater.Available != null) OpenUrl(_app.Updater.Available.PageUrl); };
            Controls.Add(_banner);

            _body.BackColor = Theme.Bg;
            _body.Location = new Point(0, HeaderH);
            _body.Width = W;
            Controls.Add(_body);
            int y = 0;
            y = Section("TÜRKÇE METİNLER", _trPicker, y);
            y = Section("DİĞER DİLLER", _enPicker, y);

            _body.Controls.Add(MakeLabel("OKUMA HIZI", Theme.Caption, Theme.Muted, Pad, y, 200, 16));
            _rateValue.Font = Theme.Small; _rateValue.ForeColor = Theme.Muted; _rateValue.TextAlign = ContentAlignment.TopRight;
            _rateValue.SetBounds(W - Pad - 120, y, 120, 16); _rateValue.BackColor = Color.Transparent;
            _body.Controls.Add(_rateValue);
            y += 22;
            _rate.SetBounds(Pad - 6, y, W - 2 * Pad + 12, 28);
            _rate.ValueChanged += delegate { _rateValue.Text = RateText(_rate.Value); if (!_loading) { _app.Settings.Rate = _rate.Value * 2; _app.Settings.Save(); } };
            _body.Controls.Add(_rate);
            y += 44;

            _body.Controls.Add(new Panel { BackColor = Theme.Border, Location = new Point(Pad, y), Size = new Size(W - 2 * Pad, 1) });
            y += 16;

            _body.Controls.Add(MakeLabel("Windows ile başlat", Theme.Body, Theme.Text, Pad, y + 1, 240, 22));
            _startup.Location = new Point(W - Pad - 44, y);
            _startup.CheckedChanged += delegate { if (!_loading) { StartupShortcut.SetEnabled(_startup.Checked); Flash(_startup.Checked ? "Başlangıca eklendi" : "Başlangıçtan kaldırıldı"); } };
            _body.Controls.Add(_startup);
            y += 36;

            _body.Controls.Add(MakeLabel("Güncellemeleri otomatik denetle", Theme.Body, Theme.Text, Pad, y + 1, 260, 22));
            _autoUpdate.Location = new Point(W - Pad - 44, y);
            _autoUpdate.CheckedChanged += delegate { if (!_loading) { _app.Settings.AutoUpdate = _autoUpdate.Checked; _app.Settings.Save(); } };
            _body.Controls.Add(_autoUpdate);
            y += 40;

            _status.Font = Theme.Small; _status.ForeColor = Theme.Muted; _status.BackColor = Color.Transparent;
            _status.SetBounds(Pad, y, W - 2 * Pad - 116, 18);
            _status.Click += delegate { if (_hint.Length > 0 && _status.Text == _hint) OpenUrl(AdapterUrl); };
            _body.Controls.Add(_status);
            _version.Font = Theme.Small; _version.ForeColor = Theme.Muted; _version.BackColor = Color.Transparent; _version.Cursor = Cursors.Hand;
            _version.TextAlign = ContentAlignment.TopRight; _version.SetBounds(W - Pad - 116, y, 116, 18);
            _version.Text = "v" + Updater.CurrentVersionText + "  ·  denetle";
            _version.Click += delegate { Flash("Güncelleme denetleniyor…"); _app.Updater.CheckAsync(true); };
            _body.Controls.Add(_version);
            y += 30;
            _bodyH = y;

            _statusTimer.Interval = 2800;
            _statusTimer.Tick += delegate { _statusTimer.Stop(); ShowHint(); };

            MouseDown += DragStart;
            _body.MouseDown += DragStart;

            _app.Updater.CheckFinished += delegate (string text) { if (Visible) Flash(text); Relayout(); };
            _app.Updater.UpdateFound += delegate { Relayout(); };
            _app.Updater.DownloadProgress += delegate (int p) { _banner.SetProgress(p); };
            _app.Updater.UpdateFailed += delegate (string msg) { _banner.SetProgress(-1); Flash(msg); };
        }

        void Relayout()
        {
            var u = _app.Updater.Available;
            bool show = u != null && u.Version.ToString(3) != _app.Settings.SkipVersion;
            if (show) _banner.SetInfo(u.Version.ToString(3));
            _banner.Visible = show;
            _body.Top = HeaderH + (show ? BannerH : 0);
            _body.Height = _bodyH;
            ClientSize = new Size(W, _body.Bottom);
            if (Visible) PlaceNearTray();
        }

        int Section(string caption, VoicePicker picker, int y)
        {
            _body.Controls.Add(MakeLabel(caption, Theme.Caption, Theme.Muted, Pad, y, 200, 16));
            y += 22;
            picker.SetBounds(Pad, y, W - 2 * Pad - 56, 48);
            picker.SelectionChanged += delegate { if (!_loading) OnVoiceChanged(picker); };
            var test = new FlatButton { Text = "", IconGlyph = true, Size = new Size(48, 48), Location = new Point(W - Pad - 48, y), Primary = true };
            test.Click += delegate { Preview(picker.Selected); };
            _body.Controls.Add(picker); _body.Controls.Add(test);
            return y + 48 + 24;
        }

        static Label MakeLabel(string text, Font font, Color color, int x, int y, int w, int h)
        {
            var l = new Label { Text = text, Font = font, ForeColor = color, BackColor = Color.Transparent, AutoSize = false };
            l.SetBounds(x, y, w, h);
            return l;
        }

        static string RateText(int v)
        {
            if (v == 0) return "Normal";
            return (v > 0 ? "Hızlı  +" : "Yavaş  ") + v;
        }

        static void OpenUrl(string url) { try { Process.Start(url); } catch { } }

        void OnVoiceChanged(VoicePicker picker)
        {
            if (picker == _trPicker) _app.Settings.TrVoiceId = picker.Selected.Id;
            else _app.Settings.EnVoiceId = picker.Selected.Id;
            _app.Settings.Save();
            Flash(picker.Selected.Name + " seçildi");
        }

        void Preview(VoiceInfo v)
        {
            if (v == null) return;
            string sample = v.IsTurkish
                ? "Merhaba, ben " + v.Name + ". Seçtiğiniz metinleri bu sesle okuyacağım."
                : "Hi, I'm " + v.Name + ". I will read your selected text with this voice.";
            _app.Speak(sample, v);
        }

        void Flash(string text)
        {
            _status.ForeColor = Theme.Muted; _status.Cursor = Cursors.Default; _status.Text = text;
            _statusTimer.Stop(); _statusTimer.Start();
        }

        void ShowHint()
        {
            bool natural = false;
            foreach (var v in _app.Engine.Voices) if (v.IsNatural) { natural = true; break; }
            _hint = natural ? "" : "Doğal (neural) sesler yüklü değil — kurulum rehberi için tıklayın";
            _status.Text = _hint;
            _status.ForeColor = natural ? Theme.Muted : Theme.Accent;
            _status.Cursor = natural ? Cursors.Default : Cursors.Hand;
        }

        public void SyncFromApp()
        {
            _loading = true;
            _trPicker.SetVoices(_app.Engine.Voices);
            _enPicker.SetVoices(_app.Engine.Voices);
            _trPicker.Selected = _app.TurkishVoice;
            _enPicker.Selected = _app.OtherVoice;
            _rate.Value = (int)Math.Round(_app.Settings.Rate / 2.0);
            _rateValue.Text = RateText(_rate.Value);
            _startup.Checked = StartupShortcut.IsEnabled;
            _autoUpdate.Checked = _app.Settings.AutoUpdate;
            _loading = false;
            ShowHint();
            Relayout();
        }

        void PlaceNearTray()
        {
            var wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Right - Width - 12, wa.Bottom - Height - 12);
        }

        public void ShowNearTray()
        {
            SyncFromApp();
            PlaceNearTray();
            Show();
            Activate();
        }

        void DragStart(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            ReleaseCapture();
            SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero);
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (_trPicker.MenuOpen || _enPicker.MenuOpen) return;
            BeginInvoke(new Action(delegate { if (!_trPicker.MenuOpen && !_enPicker.MenuOpen && Form.ActiveForm != this && !_banner.Busy) Hide(); }));
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); return; }
            base.OnFormClosing(e);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { Hide(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    // Accent card announcing a new version: notes link, Update button, skip (x); doubles as download progress.
    public sealed class UpdateBanner : SmoothControl
    {
        readonly FlatButton _update = new FlatButton { Primary = true, Text = "Güncelle", Size = new Size(96, 34) };
        readonly FlatButton _skip = new FlatButton { Text = "", IconGlyph = true, Size = new Size(26, 26), Tip = "Bu sürümü geç" };
        string _version = "";
        int _progress = -1;
        public bool Busy { get { return _progress >= 0; } }
        public event EventHandler UpdateClicked, SkipClicked, NotesClicked;

        public UpdateBanner()
        {
            Cursor = Cursors.Default;
            Controls.Add(_update); Controls.Add(_skip);
            _update.Click += delegate { if (UpdateClicked != null) UpdateClicked(this, EventArgs.Empty); };
            _skip.Click += delegate { if (SkipClicked != null) SkipClicked(this, EventArgs.Empty); };
        }

        public void SetInfo(string version) { _version = version; _progress = -1; _update.Enabled = true; _update.Visible = true; _skip.Visible = true; Invalidate(); }
        public void SetProgress(int p) { _progress = p; _update.Visible = p < 0; _skip.Visible = p < 0; Invalidate(); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (_progress < 0 && e.X < Width - 150 && NotesClicked != null) NotesClicked(this, EventArgs.Empty);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            _update.Location = new Point(Width - _update.Width - 40, (Height - _update.Height) / 2);
            _skip.Location = new Point(Width - 26 - 8, (Height - 26) / 2);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Prepare(e.Graphics);
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            using (var p = Theme.RoundRect(r, 10))
            {
                using (var b = new SolidBrush(Theme.AccentSoft)) e.Graphics.FillPath(b, p);
                using (var pen = new Pen(Theme.Accent)) e.Graphics.DrawPath(pen, p);
            }
            string head = _progress < 0 ? "Yeni sürüm " + _version + " hazır" : (_progress < 100 ? "İndiriliyor…  %" + _progress : "Doğrulanıyor ve kuruluyor…");
            string sub = _progress < 0 ? "Sürüm notları için tıklayın" : "Uygulama birkaç saniye içinde yeniden başlayacak";
            int textW = _progress < 0 ? Width - 160 : Width - 28;
            TextRenderer.DrawText(e.Graphics, head, Theme.Body, new Rectangle(14, 10, textW, 20), Theme.Text, TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(e.Graphics, sub, Theme.Small, new Rectangle(14, 31, textW, 16), _progress < 0 ? Theme.Accent : Theme.Muted, TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            if (_progress >= 0)
            {
                var track = new RectangleF(14, Height - 8, Width - 28, 3);
                using (var b = new SolidBrush(Theme.Track)) e.Graphics.FillRectangle(b, track);
                using (var b = new SolidBrush(Theme.Accent)) e.Graphics.FillRectangle(b, track.X, track.Y, track.Width * Math.Min(100, _progress) / 100f, track.Height);
            }
        }
    }
}
