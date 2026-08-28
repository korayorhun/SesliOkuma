using System;
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
        readonly VoicePicker _trPicker = new VoicePicker();
        readonly VoicePicker _enPicker = new VoicePicker();
        readonly Slider _rate = new Slider();
        readonly ToggleSwitch _startup = new ToggleSwitch();
        readonly Label _rateValue = new Label();
        readonly Label _status = new Label();
        readonly Timer _statusTimer = new Timer();
        bool _loading;
        string _hint = "";
        const string AdapterUrl = "https://github.com/gexgd0419/NaturalVoiceSAPIAdapter";

        const int W = 380, H = 440, Pad = 24;

        public SettingsForm(TrayApp app)
        {
            _app = app;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            ClientSize = new Size(W, H);
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            Text = "Sesli Okuma";
            Icon = app.AppIcon;
            DoubleBuffered = true;
            Build();
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
            int y = 26;

            var title = MakeLabel("Sesli Okuma", Theme.Title, Theme.Text, Pad, y - 4, 240, 30);
            var close = new FlatButton { Text = "", IconGlyph = true, Size = new Size(36, 36), Location = new Point(W - Pad - 36, y - 6) };
            close.Click += delegate { Hide(); };
            Controls.Add(title); Controls.Add(close);
            title.MouseDown += DragStart;
            y += 30;
            Controls.Add(MakeLabel("Ctrl + Alt + S  seçili metni okur  ·  tekrar basınca susar", Theme.Small, Theme.Muted, Pad, y, W - 2 * Pad, 18));
            y += 40;

            y = Section("TÜRKÇE METİNLER", _trPicker, y);
            y = Section("DİĞER DİLLER", _enPicker, y);

            Controls.Add(MakeLabel("OKUMA HIZI", Theme.Caption, Theme.Muted, Pad, y, 200, 16));
            _rateValue.Font = Theme.Small; _rateValue.ForeColor = Theme.Muted; _rateValue.TextAlign = ContentAlignment.TopRight;
            _rateValue.SetBounds(W - Pad - 120, y, 120, 16); _rateValue.BackColor = Color.Transparent;
            Controls.Add(_rateValue);
            y += 22;
            _rate.SetBounds(Pad - 6, y, W - 2 * Pad + 12, 28);
            _rate.ValueChanged += delegate { _rateValue.Text = RateText(_rate.Value); if (!_loading) { _app.Settings.Rate = _rate.Value * 2; _app.Settings.Save(); } };
            Controls.Add(_rate);
            y += 48;

            using (var pen = new Pen(Theme.Border)) { }
            var line = new Panel { BackColor = Theme.Border, Location = new Point(Pad, y), Size = new Size(W - 2 * Pad, 1) };
            Controls.Add(line);
            y += 18;

            Controls.Add(MakeLabel("Windows ile başlat", Theme.Body, Theme.Text, Pad, y + 1, 240, 22));
            _startup.Location = new Point(W - Pad - 44, y);
            _startup.CheckedChanged += delegate { if (!_loading) { StartupShortcut.SetEnabled(_startup.Checked); Flash(_startup.Checked ? "Başlangıca eklendi" : "Başlangıçtan kaldırıldı"); } };
            Controls.Add(_startup);
            y += 44;

            _status.Font = Theme.Small; _status.ForeColor = Theme.Muted; _status.BackColor = Color.Transparent;
            _status.SetBounds(Pad, y, W - 2 * Pad, 18);
            Controls.Add(_status);
            _statusTimer.Interval = 2600;
            _statusTimer.Tick += delegate { _statusTimer.Stop(); ShowHint(); };
            _status.Click += delegate { if (_hint.Length > 0 && _status.Text == _hint) { try { System.Diagnostics.Process.Start(AdapterUrl); } catch { } } };

            MouseDown += DragStart;
        }

        int Section(string caption, VoicePicker picker, int y)
        {
            Controls.Add(MakeLabel(caption, Theme.Caption, Theme.Muted, Pad, y, 200, 16));
            y += 22;
            picker.SetBounds(Pad, y, W - 2 * Pad - 56, 48);
            picker.SelectionChanged += delegate { if (!_loading) OnVoiceChanged(picker); };
            var test = new FlatButton { Text = "", IconGlyph = true, Size = new Size(48, 48), Location = new Point(W - Pad - 48, y), Primary = true };
            test.Click += delegate { Preview(picker.Selected); };
            Controls.Add(picker); Controls.Add(test);
            return y + 48 + 26;
        }

        Label MakeLabel(string text, Font font, Color color, int x, int y, int w, int h)
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

        void Flash(string text) { _status.ForeColor = Theme.Muted; _status.Cursor = Cursors.Default; _status.Text = text; _statusTimer.Stop(); _statusTimer.Start(); }

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
            _loading = false;
            ShowHint();
        }

        public void ShowNearTray()
        {
            SyncFromApp();
            var wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Right - W - 12, wa.Bottom - H - 12);
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
            BeginInvoke(new Action(delegate { if (!_trPicker.MenuOpen && !_enPicker.MenuOpen && Form.ActiveForm != this) Hide(); }));
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
}
