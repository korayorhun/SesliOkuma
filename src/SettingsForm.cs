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
        readonly ActionCard _updateCard = new ActionCard { ShowDismiss = true };
        readonly ActionCard _naturalCard = new ActionCard();
        readonly Label _subtitle;
        readonly HotkeyBox _hotkey = new HotkeyBox();
        readonly CaptionLink _primaryCaption = new CaptionLink();
        readonly VoicePicker _primaryPicker = new VoicePicker();
        readonly VoicePicker _otherPicker = new VoicePicker();
        readonly Slider _rate = new Slider();
        readonly ToggleSwitch _startup = new ToggleSwitch();
        readonly ToggleSwitch _autoUpdate = new ToggleSwitch();
        readonly Label _rateValue = new Label();
        readonly Label _status = new Label();
        readonly Label _version = new Label();
        readonly Timer _statusTimer = new Timer();
        bool _loading, _langMenuOpen;
        string _hint = "";

        const int W = 380, Pad = 24, HeaderH = 92, CardH = 58, CardGap = 14;
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
            RightToLeft = RightToLeft.No;

            _subtitle = MakeLabel("", Theme.Small, Theme.Muted, Pad, 56, W - 2 * Pad, 18);
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
            // ---- header: title, language, close
            var title = MakeLabel("Sesli Okuma", Theme.Title, Theme.Text, Pad, 22, 200, 30);
            var close = new FlatButton { Text = "\uE711", IconGlyph = true, Size = new Size(36, 36), Location = new Point(W - Pad - 36, 20) };
            close.Click += delegate { Hide(); };
            var lang = new FlatButton { Text = "\uE774", IconGlyph = true, Size = new Size(36, 36), Location = new Point(W - Pad - 36 - 8 - 36, 20), Tip = L.T("Language") };
            lang.Click += delegate { ShowLanguageMenu(lang); };
            Controls.Add(title); Controls.Add(lang); Controls.Add(close);
            title.MouseDown += DragStart;
            Controls.Add(_subtitle);

            // ---- cards (shown on demand, above the body)
            _updateCard.ActionText = L.T("Update");
            _updateCard.Visible = false;
            _updateCard.ActionClicked += delegate { _app.Updater.DownloadAndInstall(_app.Updater.Available); };
            _updateCard.DismissClicked += delegate
            {
                if (_app.Updater.Available != null) { _app.Settings.SkipVersion = _app.Updater.Available.Version.ToString(3); _app.Settings.Save(); }
                Relayout(); Flash(L.T("Skipped"));
            };
            _updateCard.BodyClicked += delegate { if (_app.Updater.Available != null) OpenUrl(_app.Updater.Available.PageUrl); };
            Controls.Add(_updateCard);

            _naturalCard.Title = L.T("NaturalTitle"); _naturalCard.Text2 = L.T("NaturalText"); _naturalCard.Note = L.T("NaturalNote");
            _naturalCard.ActionText = L.T("Install");
            _naturalCard.Visible = false;
            _naturalCard.ActionClicked += delegate { _naturalCard.SetProgress(0, L.F("NaturalInstalling", 0)); _app.NaturalInstaller.Start(); };
            _naturalCard.BodyClicked += delegate { OpenUrl("https://github.com/gexgd0419/NaturalVoiceSAPIAdapter"); };
            Controls.Add(_naturalCard);

            // ---- body
            _body.BackColor = Theme.Bg;
            _body.Width = W;
            Controls.Add(_body);
            int y = 0;

            _body.Controls.Add(MakeLabel(L.T("Hotkey"), Theme.Caption, Theme.Muted, Pad, y, 200, 16));
            y += 22;
            _hotkey.SetBounds(Pad, y, W - 2 * Pad, 48);
            _hotkey.HotkeyChosen += delegate (HotkeyDef def)
            {
                if (_app.ApplyHotkey(def)) { _hotkey.Value = def; UpdateSubtitle(); Flash(L.F("HotkeySaved", def.ToString())); }
                else { _hotkey.Value = _app.Hotkey; Flash(L.F("HotkeyTaken", def.ToString())); }
            };
            _hotkey.NeedModifier += delegate { Flash(L.T("HotkeyNeedMod")); };
            _body.Controls.Add(_hotkey);
            y += 48 + 24;

            _primaryCaption.Caption = L.T("Primary");
            _primaryCaption.SetBounds(Pad, y, W - 2 * Pad, 18);
            _primaryCaption.Click += delegate { ShowPrimaryLangMenu(); };
            _body.Controls.Add(_primaryCaption);
            y += 22;
            y = Picker(_primaryPicker, y, true);

            _body.Controls.Add(MakeLabel(L.T("Other"), Theme.Caption, Theme.Muted, Pad, y, 200, 16));
            y += 22;
            y = Picker(_otherPicker, y, false);

            _body.Controls.Add(MakeLabel(L.T("Speed"), Theme.Caption, Theme.Muted, Pad, y, 200, 16));
            _rateValue.Font = Theme.Small; _rateValue.ForeColor = Theme.Muted; _rateValue.TextAlign = ContentAlignment.TopRight;
            _rateValue.SetBounds(W - Pad - 140, y, 140, 16); _rateValue.BackColor = Color.Transparent;
            _body.Controls.Add(_rateValue);
            y += 22;
            _rate.SetBounds(Pad - 6, y, W - 2 * Pad + 12, 28);
            _rate.ValueChanged += delegate { _rateValue.Text = RateText(_rate.Value); if (!_loading) { _app.Settings.Rate = _rate.Value * 2; _app.Settings.Save(); } };
            _body.Controls.Add(_rate);
            y += 44;

            _body.Controls.Add(new Panel { BackColor = Theme.Border, Location = new Point(Pad, y), Size = new Size(W - 2 * Pad, 1) });
            y += 16;

            _body.Controls.Add(MakeLabel(L.T("StartWithWindows"), Theme.Body, Theme.Text, Pad, y + 1, W - 2 * Pad - 56, 22));
            _startup.Location = new Point(W - Pad - 44, y);
            _startup.CheckedChanged += delegate { if (!_loading) { StartupShortcut.SetEnabled(_startup.Checked); Flash(L.T(_startup.Checked ? "StartupAdded" : "StartupRemoved")); } };
            _body.Controls.Add(_startup);
            y += 36;

            _body.Controls.Add(MakeLabel(L.T("AutoUpdate"), Theme.Body, Theme.Text, Pad, y + 1, W - 2 * Pad - 56, 22));
            _autoUpdate.Location = new Point(W - Pad - 44, y);
            _autoUpdate.CheckedChanged += delegate { if (!_loading) { _app.Settings.AutoUpdate = _autoUpdate.Checked; _app.Settings.Save(); } };
            _body.Controls.Add(_autoUpdate);
            y += 40;

            _status.Font = Theme.Small; _status.ForeColor = Theme.Muted; _status.BackColor = Color.Transparent;
            _status.SetBounds(Pad, y, W - 2 * Pad - 116, 18);
            _status.Click += delegate { if (_hint.Length > 0 && _status.Text == _hint) NaturalVoicesInstaller.OpenWindowsVoiceSettings(); };
            _body.Controls.Add(_status);
            _version.Font = Theme.Small; _version.ForeColor = Theme.Muted; _version.BackColor = Color.Transparent; _version.Cursor = Cursors.Hand;
            _version.TextAlign = ContentAlignment.TopRight; _version.SetBounds(W - Pad - 116, y, 116, 18);
            _version.Text = "v" + Updater.CurrentVersionText + "  ·  " + L.T("Check");
            _version.Click += delegate { Flash(L.T("Checking")); _app.Updater.CheckAsync(true); };
            _body.Controls.Add(_version);
            y += 30;
            _bodyH = y;

            _statusTimer.Interval = 2800;
            _statusTimer.Tick += delegate { _statusTimer.Stop(); ShowHint(); };

            MouseDown += DragStart;
            _body.MouseDown += DragStart;

            _app.Updater.CheckFinished += delegate (string text) { if (Visible) Flash(text); Relayout(); };
            _app.Updater.UpdateFound += delegate { Relayout(); };
            _app.Updater.DownloadProgress += delegate (int p) { _updateCard.SetProgress(p, p < 100 ? L.F("Downloading", p) : L.T("Installing")); };
            _app.Updater.UpdateFailed += delegate (string msg) { _updateCard.SetIdle(); Flash(msg); };

            _app.NaturalInstaller.Progress += delegate (int p) { _naturalCard.SetProgress(p, L.F("NaturalInstalling", p)); };
            _app.NaturalInstaller.Status += delegate (string s) { _naturalCard.SetProgress(100, s); };
            _app.NaturalInstaller.Completed += delegate { _naturalCard.SetIdle(); Relayout(); Flash(L.T("NaturalDone")); };
            _app.NaturalInstaller.Failed += delegate (string msg) { _naturalCard.SetIdle(); Flash(L.F("NaturalFailed", msg)); };
        }

        int Picker(VoicePicker picker, int y, bool primary)
        {
            picker.SetBounds(Pad, y, W - 2 * Pad - 56, 48);
            picker.SelectionChanged += delegate { if (!_loading) OnVoiceChanged(picker, primary); };
            var test = new FlatButton { Text = "\uE768", IconGlyph = true, Size = new Size(48, 48), Location = new Point(W - Pad - 48, y), Primary = true };
            test.Click += delegate { Preview(picker.Selected); };
            _body.Controls.Add(picker); _body.Controls.Add(test);
            return y + 48 + 24;
        }

        void Relayout()
        {
            var u = _app.Updater.Available;
            bool showUpdate = u != null && u.Version.ToString(3) != _app.Settings.SkipVersion;
            bool showNatural = !_app.Engine.HasNaturalVoices || _naturalCard.Busy;
            int y = HeaderH;
            if (showUpdate)
            {
                _updateCard.Title = L.F("NewVersion", u.Version.ToString(3)); _updateCard.Text2 = L.T("ReleaseNotes"); _updateCard.Note = L.T("RestartSoon");
                if (!_updateCard.Busy) _updateCard.SetIdle();
                _updateCard.SetBounds(Pad, y, W - 2 * Pad, CardH); y += CardH + CardGap;
            }
            _updateCard.Visible = showUpdate;
            if (showNatural) { _naturalCard.SetBounds(Pad, y, W - 2 * Pad, CardH); y += CardH + CardGap; }
            _naturalCard.Visible = showNatural;
            _body.Top = y;
            _body.Height = _bodyH;
            ClientSize = new Size(W, _body.Bottom);
            if (Visible) PlaceNearTray();
        }

        void UpdateSubtitle() { _subtitle.Text = L.F("Subtitle", _app.Hotkey.ToString()); }

        void ShowLanguageMenu(Control anchor)
        {
            var menu = ThemedMenu.Create();
            foreach (string code in L.Languages)
            {
                var item = new ToolStripMenuItem(L.NativeName(code)) { Tag = code, Checked = code == L.Lang };
                item.Click += delegate { _app.ApplyLanguage((string)item.Tag); };
                menu.Items.Add(item);
            }
            menu.Opened += delegate { _langMenuOpen = true; };
            menu.Closed += delegate { _langMenuOpen = false; };
            menu.Show(anchor, new Point(anchor.Width - 160, anchor.Height + 4));
        }

        void ShowPrimaryLangMenu()
        {
            var menu = ThemedMenu.Create();
            foreach (string code in _app.Engine.LanguagesPresent())
            {
                string name = null;
                foreach (var v in _app.Engine.Voices) if (v.Lang2 == code) { name = v.LanguageName; break; }
                var item = new ToolStripMenuItem(name ?? code) { Tag = code, Checked = code == _app.Settings.PrimaryLang };
                item.Click += delegate
                {
                    _app.Settings.PrimaryLang = (string)item.Tag;
                    _app.Settings.PrimaryVoiceId = "";
                    _app.EnsureDefaults();
                    SyncFromApp();
                };
                menu.Items.Add(item);
            }
            menu.Items.Add(new ToolStripSeparator());
            var more = new ToolStripMenuItem(L.T("MoreVoices"));
            more.Click += delegate { NaturalVoicesInstaller.OpenWindowsVoiceSettings(); };
            menu.Items.Add(more);
            menu.Opened += delegate { _langMenuOpen = true; };
            menu.Closed += delegate { _langMenuOpen = false; };
            menu.Show(_primaryCaption, new Point(0, _primaryCaption.Height + 4));
        }

        static Label MakeLabel(string text, Font font, Color color, int x, int y, int w, int h)
        {
            var l = new Label { Text = text, Font = font, ForeColor = color, BackColor = Color.Transparent, AutoSize = false };
            l.SetBounds(x, y, w, h);
            return l;
        }

        static string RateText(int v)
        {
            if (v == 0) return L.T("Normal");
            return (v > 0 ? L.T("Fast") + "  +" : L.T("Slow") + "  ") + v;
        }

        static void OpenUrl(string url) { try { Process.Start(url); } catch { } }

        void OnVoiceChanged(VoicePicker picker, bool primary)
        {
            if (primary) _app.Settings.PrimaryVoiceId = picker.Selected.Id;
            else _app.Settings.OtherVoiceId = picker.Selected.Id;
            _app.Settings.Save();
            Flash(L.F("VoiceSelected", picker.Selected.ShortName));
        }

        void Preview(VoiceInfo v)
        {
            if (v == null) return;
            string lang = v.IsMultilingual ? L.Lang : v.Lang2;
            _app.Speak(L.Sample(lang, v.ShortName), v);
        }

        void Flash(string text)
        {
            _status.ForeColor = Theme.Muted; _status.Cursor = Cursors.Default; _status.Text = text;
            _statusTimer.Stop(); _statusTimer.Start();
        }

        void ShowHint()
        {
            bool hasPrimaryVoice = false;
            foreach (var v in _app.Engine.Voices) if (v.Lang2 == _app.Settings.PrimaryLang) { hasPrimaryVoice = true; break; }
            _hint = hasPrimaryVoice ? "" : L.T("MoreVoices");
            _status.Text = _hint;
            _status.ForeColor = hasPrimaryVoice ? Theme.Muted : Theme.Accent;
            _status.Cursor = hasPrimaryVoice ? Cursors.Default : Cursors.Hand;
        }

        public void SyncFromApp()
        {
            _loading = true;
            string primary = _app.Settings.PrimaryLang;
            _primaryPicker.PrimaryLang = primary; _otherPicker.PrimaryLang = primary;
            _primaryPicker.Filter = delegate (VoiceInfo v) { return v.Lang2 == primary || v.IsMultilingual; };
            _primaryPicker.SetVoices(_app.Engine.Voices);
            _otherPicker.SetVoices(_app.Engine.Voices);
            _primaryPicker.Selected = _app.PrimaryVoice;
            _otherPicker.Selected = _app.OtherVoice;
            string primaryName = primary;
            foreach (var v in _app.Engine.Voices) if (v.Lang2 == primary) { primaryName = v.LanguageName; break; }
            _primaryCaption.Value = primaryName; _primaryCaption.Invalidate();
            _hotkey.Value = _app.Hotkey;
            UpdateSubtitle();
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

        bool AnyMenuOpen { get { return _primaryPicker.MenuOpen || _otherPicker.MenuOpen || _langMenuOpen; } }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (AnyMenuOpen) return;
            BeginInvoke(new Action(delegate { if (!AnyMenuOpen && Form.ActiveForm != this && !_updateCard.Busy && !_naturalCard.Busy) Hide(); }));
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && !_forceClose) { e.Cancel = true; Hide(); return; }
            base.OnFormClosing(e);
        }

        bool _forceClose;
        public void Close(bool force) { _forceClose = force; Close(); }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape && !_hotkey.Focused) { Hide(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
