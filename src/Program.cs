using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using System.Windows.Forms;

namespace SesliOkuma
{
    // Hidden host window: owns the global hotkey, the tray icon and the speech engine.
    public sealed class TrayApp : Form
    {
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll")] static extern int GetMessageTime();
        [DllImport("user32.dll")] static extern uint GetClipboardSequenceNumber();

        const int WM_HOTKEY = 0x0312, WM_SHOWSETTINGS = 0x8000 + 41, WM_SHOWABOUT = 0x8000 + 42;
        const string HostTitle = "SesliOkumaHost";
        const uint MOD_NOREPEAT = 0x4000;
        const byte VK_CONTROL = 0x11, VK_MENU = 0x12, VK_SHIFT = 0x10, VK_LWIN = 0x5B, VK_C = 0x43;
        const uint KEYEVENTF_KEYUP = 2;

        public readonly SpeechEngine Engine = new SpeechEngine();
        public readonly AppSettings Settings = AppSettings.Load();
        public Updater Updater;
        public NaturalVoicesInstaller NaturalInstaller;
        HotkeyDef _hotkey = HotkeyDef.Default;
        public HotkeyDef Hotkey { get { return _hotkey; } }
        HotkeyDef _trHotkey;
        public HotkeyDef TranslateHotkey { get { return _trHotkey; } }
        bool _trRegistered;
        TranslationCard _card;
        public Icon AppIcon;
        public event Action LanguageChanged;

        Icon _idleIcon, _speakingIcon;
        readonly NotifyIcon _tray = new NotifyIcon();
        readonly System.Windows.Forms.Timer _pulse = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer _updateTimer = new System.Windows.Forms.Timer();
        ToolStripMenuItem _miSettings, _miStop, _miSave, _miTranslate, _miUpdates, _miAbout, _miExit;
        AboutForm _about;
        SettingsForm _settings;
        bool _busy, _wasSpeaking, _hotkeyRegistered;
        public Reader Reader;
        ReaderBar _bar;
        readonly System.Windows.Forms.Timer _gesture = new System.Windows.Forms.Timer();
        DateTime _pressStart;
        int _lastPressTick = int.MinValue;
        bool _holdCandidate;

        public TrayApp()
        {
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            Opacity = 0;
            Text = HostTitle;

            Theme.Load();
            L.Lang = Settings.Language.Length > 0 ? Settings.Language : L.DetectSystemLanguage();
            BuildIcons();
            Engine.RefreshVoices();
            EnsureDefaults();

            _tray.Icon = _idleIcon;
            _tray.Visible = true;
            _tray.MouseClick += delegate (object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) ToggleSettings(); };
            var menu = ThemedMenu.Create();
            _miSettings = new ToolStripMenuItem(); _miSettings.Click += delegate { ToggleSettings(); };
            _miStop = new ToolStripMenuItem(); _miStop.Click += delegate { Engine.Stop(); };
            _miUpdates = new ToolStripMenuItem(); _miUpdates.Click += delegate { Updater.CheckAsync(true); ShowSettings(); };
            _miSave = new ToolStripMenuItem(); _miSave.Click += delegate { SaveSelectionToWav(); };
            _miTranslate = new ToolStripMenuItem(); _miTranslate.Click += delegate { TranslateSelection(); };
            _miAbout = new ToolStripMenuItem(); _miAbout.Click += delegate { ShowAbout(); };
            _miExit = new ToolStripMenuItem(); _miExit.Click += delegate { Close(); };
            menu.Items.AddRange(new ToolStripItem[] { _miSettings, _miStop, _miTranslate, _miSave, new ToolStripSeparator(), _miUpdates, _miAbout, new ToolStripSeparator(), _miExit });
            _tray.ContextMenuStrip = menu;
            ApplyTexts();

            Reader = new Reader(Engine, delegate { return Settings.Rate; });
            Reader.Changed += SyncBar;
            Reader.Finished += SyncBar;
            _gesture.Interval = 40;
            _gesture.Tick += GestureTick;

            _pulse.Interval = 150;
            _pulse.Tick += delegate
            {
                Reader.Tick();
                bool speaking = Engine.IsSpeaking || Reader.Active;
                if (speaking == _wasSpeaking) return;
                _wasSpeaking = speaking;
                _tray.Icon = speaking ? _speakingIcon : _idleIcon;
            };
            _pulse.Start();

            HotkeyDef def;
            if (!HotkeyDef.TryParse(Settings.Hotkey, out def)) def = HotkeyDef.Default;
            if (!ApplyHotkey(def) && !def.Equals(HotkeyDef.Default)) ApplyHotkey(HotkeyDef.Default);
            if (!_hotkeyRegistered)
                _tray.ShowBalloonTip(6000, "Sesli Okuma", L.F("HotkeyFailBalloon", Hotkey.ToString()), ToolTipIcon.Warning);
            HotkeyDef trDef;
            if (HotkeyDef.TryParse(Settings.TranslateHotkey, out trDef)) ApplyTranslateHotkey(trDef);

            Updater = new Updater(this);
            Updater.UpdateFound += delegate (UpdateInfo u)
            {
                if (u.Version.ToString(3) == Settings.SkipVersion) return;
                _tray.ShowBalloonTip(8000, L.F("Ready", u.Version.ToString(3)), L.T("UpdateClick"), ToolTipIcon.Info);
            };
            Updater.CheckFinished += delegate { Settings.LastUpdateCheck = DateTime.UtcNow; Settings.Save(); };
            _tray.BalloonTipClicked += delegate { if (Updater.Available != null) ShowSettings(); };
            _updateTimer.Interval = 60 * 1000;
            _updateTimer.Tick += delegate
            {
                _updateTimer.Interval = 6 * 60 * 60 * 1000;
                if (Settings.AutoUpdate && DateTime.UtcNow - Settings.LastUpdateCheck >= Updater.CheckInterval) Updater.CheckAsync(false);
            };
            _updateTimer.Start();

            NaturalInstaller = new NaturalVoicesInstaller(this);
            NaturalInstaller.Completed += delegate { RefreshVoices(); };
        }

        void ApplyTexts()
        {
            _tray.Text = L.F("TrayTip", Hotkey.ToString());
            _miSettings.Text = L.T("Settings");
            _miStop.Text = L.T("Stop");
            _miUpdates.Text = L.T("CheckUpdates");
            _miSave.Text = L.T("SaveAudio") + "…";
            _miTranslate.Text = L.T("TranslateRead");
            _miAbout.Text = L.T("About") + "…";
            _miExit.Text = L.T("Exit");
        }

        void BuildIcons()
        {
            Color glyph = Theme.TaskbarIsDark() ? Color.White : Color.FromArgb(0x1B, 0x1D, 0x24);
            _idleIcon = IconFactory.Create(32, glyph, glyph, false);
            _speakingIcon = IconFactory.Create(32, glyph, Theme.Accent, true);
            AppIcon = IconFactory.Create(64, Theme.Accent, Theme.Accent, true);
            Icon = AppIcon;
        }

        // Picks sensible primary/other voices whenever the saved ones are missing.
        public void EnsureDefaults()
        {
            bool changed = false;
            var present = Engine.LanguagesPresent();
            if (Settings.PrimaryLang.Length == 0 || !present.Contains(Settings.PrimaryLang))
            {
                string want = present.Contains(L.Lang) ? L.Lang : (present.Count > 0 ? present[0] : L.Lang);
                if (Settings.PrimaryLang != want) { Settings.PrimaryLang = want; changed = true; }
            }
            var pv = Engine.FindById(Settings.PrimaryVoiceId);
            if (pv == null || (pv.Lang2 != Settings.PrimaryLang && !pv.IsMultilingual))
            {
                var v = Engine.BestFor(Settings.PrimaryLang);
                if (v != null) { Settings.PrimaryVoiceId = v.Id; changed = true; }
            }
            if (Engine.FindById(Settings.OtherVoiceId) == null)
            {
                VoiceInfo v = null;
                foreach (var c in Engine.Voices) if (c.IsMultilingual) { v = c; break; }
                if (v == null) v = Settings.PrimaryLang != "en" ? Engine.BestFor("en") : null;
                if (v == null) foreach (var c in Engine.Voices) if (c.Lang2 != Settings.PrimaryLang) { v = c; break; }
                if (v == null && Engine.Voices.Count > 0) v = Engine.Voices[0];
                if (v != null) { Settings.OtherVoiceId = v.Id; changed = true; }
            }
            if (changed) Settings.Save();
            Logger.Log("voices ready; primary=" + Settings.PrimaryLang + " voice=" + (PrimaryVoice != null ? PrimaryVoice.Name : "-") + " other=" + (OtherVoice != null ? OtherVoice.Name : "-"));
        }

        public void RefreshVoices()
        {
            Engine.RefreshVoices();
            EnsureDefaults();
            if (_settings != null && !_settings.IsDisposed) _settings.SyncFromApp();
        }

        public VoiceInfo PrimaryVoice { get { return Engine.FindById(Settings.PrimaryVoiceId); } }
        public VoiceInfo OtherVoice { get { return Engine.FindById(Settings.OtherVoiceId); } }

        public void Speak(string text, VoiceInfo voice)
        {
            Reader.Stop(false);
            try { Engine.Speak(text, voice, Settings.Rate); }
            catch (Exception ex) { Logger.Log("speak failed: " + ex.Message); }
        }

        void SyncBar()
        {
            bool show = Reader.Active && Settings.ShowReaderBar;
            if (show)
            {
                if (_bar == null || _bar.IsDisposed)
                    _bar = new ReaderBar(Reader, delegate { return Settings.Rate; }, delegate (int d) { Settings.Rate = Math.Max(-10, Math.Min(10, Settings.Rate + d)); Settings.Save(); });
                if (!_bar.Visible) { _bar.Place(); _bar.Show(); }
            }
            else if (_bar != null && !_bar.IsDisposed && _bar.Visible) _bar.Hide();
        }

        // Paragraph under the mouse pointer (used when nothing is selected).
        static string GetParagraphUnderMouse()
        {
            try
            {
                var pt = Cursor.Position;
                AutomationElement el = AutomationElement.FromPoint(new System.Windows.Point(pt.X, pt.Y));
                if (el == null) return null;
                object pat;
                if (!el.TryGetCurrentPattern(TextPattern.Pattern, out pat)) return null;
                var tp = (TextPattern)pat;
                TextPatternRange range = tp.RangeFromPoint(new System.Windows.Point(pt.X, pt.Y));
                if (range == null) return null;
                range.ExpandToEnclosingUnit(TextUnit.Paragraph);
                string s = range.GetText(-1);
                return (s != null && s.Trim().Length > 0) ? s : null;
            }
            catch (Exception ex) { Logger.Log("UIA point: " + ex.Message); return null; }
        }

        void GestureTick(object sender, EventArgs e)
        {
            if (!_holdCandidate) { _gesture.Stop(); return; }
            bool down = (GetAsyncKeyState((int)Hotkey.Key) & 0x8000) != 0;
            if (!down) { _holdCandidate = false; _gesture.Stop(); Reader.Stop(false); Logger.Log("stopped"); return; }
            if ((DateTime.UtcNow - _pressStart).TotalMilliseconds >= 600) { _holdCandidate = false; _gesture.Stop(); Reader.TogglePause(); Logger.Log(Reader.Paused ? "paused" : "resumed"); }
        }

        public bool ApplyHotkey(HotkeyDef def)
        {
            if (_hotkeyRegistered) { UnregisterHotKey(Handle, 1); _hotkeyRegistered = false; }
            if (RegisterHotKey(Handle, 1, def.Modifiers | MOD_NOREPEAT, (uint)def.Key))
            {
                _hotkeyRegistered = true;
                _hotkey = def;
                Settings.Hotkey = def.ToString();
                Settings.Save();
                if (_miSettings != null) ApplyTexts();
                Logger.Log("hotkey registered: " + def);
                return true;
            }
            Logger.Log("hotkey failed: " + def);
            // Restore the previous one so the app never ends up without a shortcut.
            if (RegisterHotKey(Handle, 1, Hotkey.Modifiers | MOD_NOREPEAT, (uint)Hotkey.Key)) _hotkeyRegistered = true;
            return false;
        }

        public bool ApplyTranslateHotkey(HotkeyDef def)
        {
            if (_trRegistered) { UnregisterHotKey(Handle, 2); _trRegistered = false; }
            if (def.IsValid && RegisterHotKey(Handle, 2, def.Modifiers | MOD_NOREPEAT, (uint)def.Key))
            {
                _trRegistered = true; _trHotkey = def; Settings.TranslateHotkey = def.ToString(); Settings.Save();
                Logger.Log("translate hotkey registered: " + def);
                return true;
            }
            if (_trHotkey.IsValid && RegisterHotKey(Handle, 2, _trHotkey.Modifiers | MOD_NOREPEAT, (uint)_trHotkey.Key)) _trRegistered = true;
            return false;
        }

        public void TranslateSelection()
        {
            try { TranslateSelectionCore(); }
            catch (Exception ex) { Logger.Log("translate error: " + ex); }
        }

        void TranslateSelectionCore()
        {
            Logger.Log("translate requested");
            if (Settings.DeepLKey.Trim().Length == 0)
            {
                _tray.ShowBalloonTip(6000, L.T("TranslateRead"), L.T("TranslateNeedsKey"), ToolTipIcon.Info);
                Settings.AdvancedOpen = true; Settings.Save(); ShowSettings();
                return;
            }
            string text = GrabText();
            if (text == null) { _tray.ShowBalloonTip(4000, L.T("TranslateRead"), L.T("NoTextToSave"), ToolTipIcon.Warning); return; }
            Reader.Stop(false);
            if (_card == null || _card.IsDisposed) { _card = new TranslationCard(AppIcon); _card.PlayClicked += delegate { var v2 = Engine.BestFor(Settings.PrimaryLang) ?? PrimaryVoice; Reader.Start(_card.Translation, v2); }; }
            string targetName = L.NativeName(Settings.PrimaryLang) == Settings.PrimaryLang ? Settings.PrimaryLang.ToUpperInvariant() : L.NativeName(Settings.PrimaryLang);
            _card.SetContent("", targetName, text, "", true); _card.Show();
            Translator.TranslateAsync(this, Settings.DeepLKey, text, Settings.PrimaryLang,
                delegate (string translated, string detected)
                {
                    if (_card == null || _card.IsDisposed) return;
                    _card.SetContent(detected, targetName, text, translated, false);
                    var v = Engine.BestFor(Settings.PrimaryLang) ?? PrimaryVoice;
                    Reader.Start(translated, v);
                },
                delegate (string err) { if (_card != null && !_card.IsDisposed) _card.Close(); _tray.ShowBalloonTip(6000, L.T("TranslateRead"), L.F("TranslateFailed", err), ToolTipIcon.Warning); });
        }

        public void ApplyLanguage(string code)
        {
            L.Lang = code;
            Settings.Language = code;
            Settings.Save();
            ApplyTexts();
            if (_settings != null && !_settings.IsDisposed)
            {
                bool wasVisible = _settings.Visible;
                _settings.Close(true); _settings.Dispose(); _settings = null;
                if (wasVisible) ShowSettings();
            }
            if (LanguageChanged != null) LanguageChanged();
        }

        void ToggleSettings()
        {
            if (_settings == null || _settings.IsDisposed) _settings = new SettingsForm(this);
            if (_settings.Visible) _settings.Hide(); else _settings.ShowNearTray();
        }

        public void ShowAbout()
        {
            if (_about != null && !_about.IsDisposed) { _about.Activate(); return; }
            _about = new AboutForm(AppIcon);
            _about.ShowNearTray();
        }

        void ShowSettings()
        {
            if (_settings == null || _settings.IsDisposed) _settings = new SettingsForm(this);
            if (!_settings.Visible) _settings.ShowNearTray();
        }

        protected override void SetVisibleCore(bool value) { base.SetVisibleCore(false); }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY) { if (m.WParam.ToInt32() == 2) TranslateSelection(); else OnHotkey(); return; }
            if (m.Msg == WM_SHOWSETTINGS) { ToggleSettings(); return; }
            if (m.Msg == WM_SHOWABOUT) { ShowAbout(); return; }
            base.WndProc(ref m);
        }

        static string ForegroundApp()
        {
            try { uint pid; GetWindowThreadProcessId(GetForegroundWindow(), out pid); return System.Diagnostics.Process.GetProcessById((int)pid).ProcessName; }
            catch { return "?"; }
        }

        static string GetSelectionViaUia()
        {
            try
            {
                AutomationElement el = AutomationElement.FocusedElement;
                if (el == null) return null;
                object pat;
                if (el.TryGetCurrentPattern(TextPattern.Pattern, out pat))
                {
                    TextPatternRange[] ranges = ((TextPattern)pat).GetSelection();
                    if (ranges != null && ranges.Length > 0)
                    {
                        string s = ranges[0].GetText(-1);
                        if (s != null && s.Trim().Length > 0) return s;
                    }
                }
            }
            catch (Exception ex) { Logger.Log("UIA: " + ex.Message); }
            return null;
        }

        string GetSelectionViaCopy()
        {
            string old = null;
            try { if (Clipboard.ContainsText()) old = Clipboard.GetText(); } catch { }
            try { Clipboard.Clear(); } catch { }
            // Release whatever modifiers the hotkey holds down, then send a clean Ctrl+C.
            keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_SHIFT, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            Thread.Sleep(30);
            keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
            keybd_event(VK_C, 0, 0, UIntPtr.Zero);
            keybd_event(VK_C, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            uint seq = GetClipboardSequenceNumber();
            string text = null;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 350)
            {
                Thread.Sleep(15);
                if (GetClipboardSequenceNumber() == seq) continue;
                try { if (Clipboard.ContainsText()) text = Clipboard.GetText(); } catch { }
                break;
            }
            if (old != null) { try { Clipboard.SetText(old); } catch { } }
            return (text != null && text.Trim().Length > 0) ? text : null;
        }

        static string GetClipboardText()
        {
            try { if (Clipboard.ContainsText()) return Clipboard.GetText(); } catch { }
            return null;
        }

        void OnHotkey()
        {
            var now = DateTime.UtcNow;
            int tick = GetMessageTime();                      // time the key went down, not when we got to process it
            bool dbl = _lastPressTick != int.MinValue && (tick - _lastPressTick) >= 0 && (tick - _lastPressTick) < 450;
            if (dbl)
            {
                // double press: read the clipboard
                _lastPressTick = int.MinValue; _holdCandidate = false; _gesture.Stop();
                Reader.Stop(false);
                string clip = GetClipboardText();
                if (clip == null || clip.Trim().Length == 0) { Logger.Log("double press: clipboard empty"); return; }
                Read(clip, "clipboard(2x)");
                return;
            }
            _lastPressTick = tick;
            if (Reader.Active)
            {
                // quick release = stop, hold = pause/resume (decided by GestureTick)
                _pressStart = now; _holdCandidate = true; _gesture.Start();
                return;
            }
            if (Engine.IsSpeaking) { Engine.Stop(); Logger.Log("stopped"); return; }
            ReadSelection();
        }

        void ReadSelection()
        {
            if (_busy) return;
            _busy = true;
            try
            {
                if (!Engine.IsAvailable) { Logger.Log("no voice"); return; }
                if (Engine.Voices.Count == 0) { Engine.RefreshVoices(); EnsureDefaults(); }
                var sw = System.Diagnostics.Stopwatch.StartNew();
                string source = "uia";
                string text = GetSelectionViaUia(); long tUia = sw.ElapsedMilliseconds;
                if (text == null) { source = "copy"; text = GetSelectionViaCopy(); }
                long tCopy = sw.ElapsedMilliseconds;
                if (text == null) { source = "pointer"; text = GetParagraphUnderMouse(); }
                if (text == null) { source = "clipboard"; text = GetClipboardText(); }
                long tGet = sw.ElapsedMilliseconds;
                if (text == null || text.Trim().Length == 0) { Logger.Log("no text app=" + ForegroundApp() + " (" + tGet + " ms)"); return; }
                Read(text, source + " t=" + tUia + "/" + tCopy + "/" + tGet + "ms");
            }
            catch (Exception ex) { Logger.Log("hotkey error: " + ex.Message); }
            finally { _busy = false; }
        }

        string GrabText()
        {
            string src = "uia"; string text = GetSelectionViaUia();
            if (text == null) { src = "copy"; text = GetSelectionViaCopy(); }
            if (text == null) { src = "pointer"; text = GetParagraphUnderMouse(); }
            if (text == null) { src = "clipboard"; text = GetClipboardText(); }
            bool ok = text != null && text.Trim().Length > 0;
            Logger.Log("grab " + (ok ? src + " len=" + text.Length : "none") + " app=" + ForegroundApp());
            return ok ? text : null;
        }

        public void SaveSelectionToWav()
        {
            string text = GrabText();
            if (text == null) { _tray.ShowBalloonTip(4000, "Sesli Okuma", L.T("NoTextToSave"), ToolTipIcon.Warning); return; }
            bool primary = TextLanguage.IsPrimary(text, Settings.PrimaryLang);
            VoiceInfo v = primary ? (PrimaryVoice ?? OtherVoice) : (OtherVoice ?? PrimaryVoice);
            string suggested = text.Trim(); if (suggested.Length > 40) suggested = suggested.Substring(0, 40);
            foreach (char bad in System.IO.Path.GetInvalidFileNameChars()) suggested = suggested.Replace(bad, ' ');
            using (var dlg = new SaveFileDialog { Filter = "WAV|*.wav", FileName = suggested.Trim() + ".wav", Title = L.T("SaveAudio") })
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;
                string path = dlg.FileName; int rate = Settings.Rate;
                var t = new Thread(delegate ()
                {
                    string err = null;
                    try { Engine.SaveToWav(text, v, rate, path); } catch (Exception ex) { err = ex.Message; }
                    try { BeginInvoke(new Action(delegate
                    {
                        if (err != null) { Logger.Log("save wav failed: " + err); _tray.ShowBalloonTip(5000, "Sesli Okuma", L.F("SaveFailed", err), ToolTipIcon.Error); }
                        else { Logger.Log("saved wav " + path); _tray.ShowBalloonTip(5000, "Sesli Okuma", L.F("SavedTo", System.IO.Path.GetFileName(path)), ToolTipIcon.Info); }
                    })); } catch { }
                });
                t.IsBackground = true; t.Start();
            }
        }

        void Read(string text, string source)        {
            bool primary = TextLanguage.IsPrimary(text, Settings.PrimaryLang);
            VoiceInfo v = primary ? (PrimaryVoice ?? OtherVoice) : (OtherVoice ?? PrimaryVoice);
            Reader.Start(text, v);
            Logger.Log("read " + source + " " + (primary ? Settings.PrimaryLang : "other") + " len=" + text.Length + " sentences=" + Reader.Count + " app=" + ForegroundApp());
        }
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (_hotkeyRegistered) UnregisterHotKey(Handle, 1);
            if (_trRegistered) UnregisterHotKey(Handle, 2);
            _pulse.Stop();
            _gesture.Stop();
            _updateTimer.Stop();
            Reader.Stop(false);
            Engine.Stop();
            _tray.Visible = false;
            base.OnFormClosed(e);
        }

        [STAThread]
        static void Main(string[] args)
        {
            bool wantAbout = args.Length > 0 && args[0] == "--about";
            bool createdNew;
            using (var mutex = new Mutex(true, @"Local\SesliOkumaHotkey", out createdNew))
            {
                if (!createdNew)
                {
                    IntPtr h = FindWindow(null, HostTitle);
                    if (h != IntPtr.Zero) PostMessage(h, wantAbout ? WM_SHOWABOUT : WM_SHOWSETTINGS, IntPtr.Zero, IntPtr.Zero);
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayApp());
            }
        }
    }
}
