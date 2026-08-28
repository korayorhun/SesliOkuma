using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SesliOkuma
{
    // The settings panel. Layout is on an 8 px grid; everything rarely used lives under "Advanced".
    public sealed class SettingsForm : Form
    {
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        readonly TrayApp _app;
        readonly Panel _body = new Panel();
        readonly Panel _adv = new Panel();
        readonly ActionCard _updateCard = new ActionCard { ShowDismiss = true };
        readonly ActionCard _naturalCard = new ActionCard();
        readonly HotkeyBox _hotkey = new HotkeyBox();
        readonly CaptionLink _primaryCaption = new CaptionLink();
        readonly VoicePicker _primaryPicker = new VoicePicker();
        readonly VoicePicker _otherPicker = new VoicePicker();
        readonly Slider _rate = new Slider();
        readonly Label _rateValue = new Label();
        readonly ToggleSwitch _startup = new ToggleSwitch();
        readonly CaptionLink _advanced = new CaptionLink { ChevronOnly = true };
        readonly ToggleSwitch _autoUpdate = new ToggleSwitch();
        readonly ToggleSwitch _readerBar = new ToggleSwitch();
        readonly ToggleSwitch _hover = new ToggleSwitch();
        readonly Label _stats = new Label();
        readonly HotkeyBox _trHotkey = new HotkeyBox { HintKey = "HotkeyHintSimple" };
        readonly TextBox _key = new TextBox();
        readonly Label _checkLink = new Label();
        readonly Label _status = new Label();
        readonly Label _version = new Label();
        readonly Timer _statusTimer = new Timer();
        bool _loading, _langMenuOpen, _winVoiceBusy;
        string _hint = "";

        const int W = 384, Pad = 24, HeaderH = 72, CardH = 56, Gap = 16, Row = 48, Cap = 24;
        int _bodyH, _advH, _footerOffset;

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
            // ---- header
            var title = MakeLabel("Sesli Okuma", Theme.Title, Theme.Text, Pad, 22, 200, 30);
            title.MouseDown += DragStart;
            var lang = new FlatButton { Text = "\uE774", IconGlyph = true, Borderless = true, Size = new Size(36, 36), Location = new Point(W - Pad - 36 - 4 - 36, 20) };
            lang.Click += delegate { ShowLanguageMenu(lang); };
            Tips.Set(lang, L.T("Language"));
            var close = new FlatButton { Text = "\uE711", IconGlyph = true, Borderless = true, Size = new Size(36, 36), Location = new Point(W - Pad - 36, 20) };
            close.Click += delegate { Hide(); };
            Tips.Set(close, L.T("Close"));
            Controls.Add(title); Controls.Add(lang); Controls.Add(close);

            // ---- cards (only when needed)
            _updateCard.ActionText = L.T("Update"); _updateCard.DismissTip = L.T("SkipVersionTip");
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
            _body.BackColor = Theme.Bg; _body.Width = W;
            Controls.Add(_body);
            int y = 0;

            y = Caption(L.T("Hotkey"), y);
            _hotkey.SetBounds(Pad, y, W - 2 * Pad, Row);
            _hotkey.HotkeyChosen += delegate (HotkeyDef def)
            {
                if (_app.ApplyHotkey(def)) { _hotkey.Value = def; Flash(L.F("HotkeySaved", def.ToString())); }
                else { _hotkey.Value = _app.Hotkey; Flash(L.F("HotkeyTaken", def.ToString())); }
            };
            _hotkey.NeedModifier += delegate { Flash(L.T("HotkeyNeedMod")); };
            _body.Controls.Add(_hotkey);
            y += Row + Gap + 8;

            _primaryCaption.Caption = L.T("Primary");
            _primaryCaption.SetBounds(Pad, y, W - 2 * Pad, 22);
            _primaryCaption.Click += delegate { ShowPrimaryLangMenu(); };
            _body.Controls.Add(_primaryCaption);
            y += Cap;
            y = Picker(_primaryPicker, y, true);

            y = Caption(L.T("Other"), y);
            y = Picker(_otherPicker, y, false);

            y = Caption(L.T("Speed"), y);
            _rateValue.Font = Theme.Small; _rateValue.ForeColor = Theme.Muted; _rateValue.TextAlign = ContentAlignment.TopRight; _rateValue.BackColor = Color.Transparent;
            _rateValue.SetBounds(W - Pad - 160, y - Cap, 160, 16);
            _body.Controls.Add(_rateValue);
            _rate.SetBounds(Pad - 6, y - 8, W - 2 * Pad + 12, 28);
            _rate.ValueChanged += delegate { _rateValue.Text = RateText(_rate.Value); if (!_loading) { _app.Settings.Rate = _rate.Value * 2; _app.Settings.Save(); } };
            _body.Controls.Add(_rate);
            y += 24 + Gap;

            _body.Controls.Add(new Panel { BackColor = Theme.Border, Location = new Point(Pad, y), Size = new Size(W - 2 * Pad, 1) });
            y += Gap;

            y = ToggleRow(L.T("StartWithWindows"), _startup, y);
            _startup.CheckedChanged += delegate { if (!_loading) { StartupShortcut.SetEnabled(_startup.Checked); Flash(L.T(_startup.Checked ? "StartupAdded" : "StartupRemoved")); } };

            // ---- advanced (collapsed by default)
            _advanced.Caption = L.T("Advanced").ToUpperInvariant();
            _advanced.SetBounds(Pad, y, W - 2 * Pad, 22);
            _advanced.Click += delegate { _app.Settings.AdvancedOpen = !_app.Settings.AdvancedOpen; _app.Settings.Save(); Relayout(); };
            _body.Controls.Add(_advanced);
            y += Cap + 8;

            _adv.BackColor = Theme.Bg; _adv.Location = new Point(0, y); _adv.Width = W;
            int a = 0;
            a = ToggleRowIn(_adv, L.T("AutoUpdate"), _autoUpdate, a);
            _autoUpdate.CheckedChanged += delegate { if (!_loading) { _app.Settings.AutoUpdate = _autoUpdate.Checked; _app.Settings.Save(); } };
            a = ToggleRowIn(_adv, L.T("ReaderBar"), _readerBar, a);
            _readerBar.CheckedChanged += delegate { if (!_loading) { _app.Settings.ShowReaderBar = _readerBar.Checked; _app.Settings.Save(); } };
            a = ToggleRowIn(_adv, L.T("HoverRead"), _hover, a);
            _hover.CheckedChanged += delegate { if (!_loading) { _app.Settings.HoverRead = _hover.Checked; _app.Settings.Save(); _app.SyncHover(); } };
            a += 8;
            a = CaptionIn(_adv, L.T("TranslateHotkey"), a);
            _trHotkey.SetBounds(Pad, a, W - 2 * Pad, Row);
            _trHotkey.HotkeyChosen += delegate (HotkeyDef def) { if (_app.ApplyTranslateHotkey(def)) { _trHotkey.Value = def; Flash(L.F("HotkeySaved", def.ToString())); } else { _trHotkey.Value = _app.TranslateHotkey; Flash(L.F("HotkeyTaken", def.ToString())); } };
            _trHotkey.NeedModifier += delegate { Flash(L.T("HotkeyNeedMod")); };
            _adv.Controls.Add(_trHotkey);
            a += Row + 6;
            _adv.Controls.Add(MakeLabel(L.T("FreeEngine"), Theme.Small, Theme.Muted, Pad, a, W - 2 * Pad, 16));
            a += 16 + Gap;
            a = CaptionIn(_adv, L.T("DeepLKey"), a, W - 2 * Pad - 130);
            var get = MakeLabel(L.T("GetKey"), Theme.Caption, Theme.Accent, W - Pad - 125, a - Cap + 3, 125, 16); get.TextAlign = ContentAlignment.TopRight; get.Cursor = Cursors.Hand;
            get.Click += delegate { OpenUrl("https://www.deepl.com/pro-api"); };
            _adv.Controls.Add(get);
            var keyBox = new Panel { BackColor = Theme.Card, Location = new Point(Pad, a), Size = new Size(W - 2 * Pad, 40), BorderStyle = Theme.HighContrast ? BorderStyle.FixedSingle : BorderStyle.None };
            _key.BorderStyle = BorderStyle.None; _key.BackColor = Theme.Card; _key.ForeColor = Theme.Text; _key.Font = Theme.Body; _key.UseSystemPasswordChar = true;
            _key.SetBounds(12, 10, keyBox.Width - 24, 22);
            _key.Leave += delegate { SaveKey(); };
            _key.KeyDown += delegate (object s2, KeyEventArgs e2) { if (e2.KeyCode == Keys.Enter) { e2.SuppressKeyPress = true; SaveKey(); _body.Focus(); } };
            keyBox.Controls.Add(_key); _adv.Controls.Add(keyBox);
            a += 40 + Gap;
            _checkLink.Font = Theme.Small; _checkLink.ForeColor = Theme.Accent; _checkLink.BackColor = Color.Transparent; _checkLink.Cursor = Cursors.Hand; _checkLink.AutoSize = false;
            _checkLink.Text = L.T("CheckUpdates"); _checkLink.SetBounds(Pad, a, 150, 18);
            _checkLink.Click += delegate { Flash(L.T("Checking")); _app.Updater.CheckAsync(true); };
            _adv.Controls.Add(_checkLink);
            _stats.Font = Theme.Small; _stats.ForeColor = Theme.Muted; _stats.BackColor = Color.Transparent; _stats.AutoSize = false; _stats.TextAlign = ContentAlignment.TopRight;
            _stats.SetBounds(Pad + 150, a, W - 2 * Pad - 150, 18);
            _adv.Controls.Add(_stats);
            a += 18 + 8;
            _adv.Height = a; _advH = a;
            _body.Controls.Add(_adv);
            y += _advH;

            // ---- footer: status hint (left) + version (right)
            _status.Font = Theme.Small; _status.ForeColor = Theme.Muted; _status.BackColor = Color.Transparent; _status.AutoSize = false;
            _status.SetBounds(Pad, y, W - 2 * Pad - 70, 18);
            _status.Click += delegate { if (_hint.Length > 0 && _status.Text == _hint) { if (WindowsVoicePack.CultureFor(_app.Settings.PrimaryLang) != null) InstallWindowsVoice(); else NaturalVoicesInstaller.OpenWindowsVoiceSettings(); } };
            _body.Controls.Add(_status);
            _version.Font = Theme.Small; _version.ForeColor = Theme.Muted; _version.BackColor = Color.Transparent; _version.AutoSize = false;
            _version.TextAlign = ContentAlignment.TopRight; _version.SetBounds(W - Pad - 70, y, 70, 18);
            _version.Text = "v" + Updater.CurrentVersionText;
            _body.Controls.Add(_version);
            _footerOffset = y - (_adv.Top + _advH);
            y += 18 + Pad;
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

        int Caption(string text, int y) { return CaptionIn(_body, text, y); }
        int CaptionIn(Control parent, string text, int y) { return CaptionIn(parent, text, y, W - 2 * Pad); }
        int CaptionIn(Control parent, string text, int y, int width)
        {
            parent.Controls.Add(MakeLabel(text, Theme.Caption, Theme.Muted, Pad, y + 3, width, 16));
            return y + Cap;
        }

        int Picker(VoicePicker picker, int y, bool primary)
        {
            picker.SetBounds(Pad, y, W - 2 * Pad - Row - 8, Row);
            picker.SelectionChanged += delegate { if (!_loading) OnVoiceChanged(picker, primary); };
            var test = new FlatButton { Text = "\uE768", IconGlyph = true, Accent = true, Size = new Size(Row, Row), Location = new Point(W - Pad - Row, y) };
            test.Click += delegate { Preview(picker.Selected); };
            Tips.Set(test, L.T("Listen"));
            _body.Controls.Add(picker); _body.Controls.Add(test);
            return y + Row + Gap + 8;
        }

        int ToggleRow(string text, ToggleSwitch t, int y) { return ToggleRowIn(_body, text, t, y); }
        int ToggleRowIn(Control parent, string text, ToggleSwitch t, int y)
        {
            parent.Controls.Add(MakeLabel(text, Theme.Body, Theme.Text, Pad, y + 1, W - 2 * Pad - 56, 22));
            t.Location = new Point(W - Pad - 44, y);
            parent.Controls.Add(t);
            return y + 24 + Gap;
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
                _updateCard.SetBounds(Pad, y, W - 2 * Pad, CardH); y += CardH + Gap;
            }
            _updateCard.Visible = showUpdate;
            if (showNatural) { _naturalCard.SetBounds(Pad, y, W - 2 * Pad, CardH); y += CardH + Gap; }
            _naturalCard.Visible = showNatural;

            bool open = _app.Settings.AdvancedOpen;
            _adv.Visible = open;
            int advH = open ? _advH : 0;
            _advanced.Open = open; _advanced.Invalidate();
            _status.Top = _adv.Top + advH + _footerOffset; _version.Top = _status.Top;

            _body.Top = y;
            _body.Height = _bodyH - (_advH - advH);
            ClientSize = new Size(W, _body.Bottom);
            if (Visible) PlaceNearTray();
        }

        void SaveKey()
        {
            string k = _key.Text.Trim();
            if (k == _app.Settings.DeepLKey) return;
            _app.Settings.DeepLKey = k; _app.Settings.Save(); Flash(L.T("KeySaved"));
        }

        void InstallWindowsVoice()
        {
            if (_winVoiceBusy) return;
            _winVoiceBusy = true;
            _status.Text = L.T("WinVoiceInstalling"); _status.ForeColor = Theme.Accent; _status.Cursor = Cursors.Default; _statusTimer.Stop();
            WindowsVoicePack.InstallAsync(this, _app.Settings.PrimaryLang,
                delegate { _winVoiceBusy = false; _app.RefreshVoices(); Flash(L.T("WinVoiceDone")); },
                delegate (string err) { _winVoiceBusy = false; Flash(L.F("WinVoiceFailed", err)); });
        }

        void ShowLanguageMenu(Control anchor)
        {
            var menu = ThemedMenu.Create();
            foreach (string code in L.Languages)
            {
                var item = new ToolStripMenuItem(L.NativeName(code)) { Tag = code, Checked = code == L.Lang };
                item.Click += delegate { _app.ApplyLanguage((string)item.Tag); };
                menu.Items.Add(item);
            }
            menu.Items.Add(new ToolStripSeparator());
            var about = new ToolStripMenuItem(L.T("About") + "…");
            about.Click += delegate { Hide(); _app.ShowAbout(); };
            menu.Items.Add(about);
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
            if (WindowsVoicePack.CultureFor(_app.Settings.PrimaryLang) != null)
            {
                var win = new ToolStripMenuItem(L.F("WinVoiceInstall", WindowsVoicePack.VoiceNameFor(_app.Settings.PrimaryLang)));
                win.Click += delegate { InstallWindowsVoice(); };
                menu.Items.Add(win);
            }
            var more = new ToolStripMenuItem(L.T("WinVoiceSettings"));
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
            if (v <= -3) return L.T("Slow");
            if (v < 0) return L.T("SlightlySlow");
            if (v >= 3) return L.T("Fast");
            return L.T("SlightlyFast");
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
            if (_winVoiceBusy) return;
            bool hasPrimaryVoice = false;
            foreach (var v in _app.Engine.Voices) if (v.Lang2 == _app.Settings.PrimaryLang) { hasPrimaryVoice = true; break; }
            _hint = hasPrimaryVoice ? "" : L.T("NoVoiceForLang");
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
            _trHotkey.Value = _app.TranslateHotkey;
            _key.Text = _app.Settings.DeepLKey;
            _rate.Value = (int)Math.Round(_app.Settings.Rate / 2.0);
            _rateValue.Text = RateText(_rate.Value);
            _startup.Checked = StartupShortcut.IsEnabled;
            _autoUpdate.Checked = _app.Settings.AutoUpdate;
            _readerBar.Checked = _app.Settings.ShowReaderBar;
            _hover.Checked = _app.Settings.HoverRead;
            _stats.Text = L.F("Stats", _app.Settings.WordsToday.ToString("N0"), _app.Settings.WordsTotal.ToString("N0"));
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

        bool _forceClose;
        public void Close(bool force) { _forceClose = force; Close(); }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && !_forceClose) { e.Cancel = true; Hide(); return; }
            base.OnFormClosing(e);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape && !_hotkey.Focused && !_trHotkey.Focused && !_key.Focused) { Hide(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
