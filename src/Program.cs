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
        const int WM_HOTKEY = 0x0312, WM_SHOWSETTINGS = 0x8000 + 41;
        const string HostTitle = "SesliOkumaHost";
        const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_NOREPEAT = 0x4000, VK_S = 0x53;
        const byte VK_CONTROL = 0x11, VK_MENU = 0x12, VK_C = 0x43;
        const uint KEYEVENTF_KEYUP = 2;

        public readonly SpeechEngine Engine = new SpeechEngine();
        public readonly AppSettings Settings = AppSettings.Load();
        public Icon AppIcon;
        Icon _idleIcon, _speakingIcon;
        readonly NotifyIcon _tray = new NotifyIcon();
        readonly System.Windows.Forms.Timer _pulse = new System.Windows.Forms.Timer();
        SettingsForm _settings;
        bool _busy, _wasSpeaking;
        public Updater Updater;
        readonly System.Windows.Forms.Timer _updateTimer = new System.Windows.Forms.Timer();

        public TrayApp()
        {
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            Opacity = 0;
            Text = HostTitle;

            Theme.Load();
            BuildIcons();
            Engine.RefreshVoices();
            EnsureDefaultVoices();

            _tray.Icon = _idleIcon;
            _tray.Text = "Sesli Okuma  ·  Ctrl+Alt+S";
            _tray.Visible = true;
            _tray.MouseClick += delegate (object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) ToggleSettings(); };
            var menu = new ContextMenuStrip { Renderer = new MenuRenderer(), ShowImageMargin = false, Font = Theme.Body, BackColor = Theme.Card, ForeColor = Theme.Text };
            menu.Items.Add("Ayarlar", null, delegate { ToggleSettings(); });
            menu.Items.Add("Sustur", null, delegate { Engine.Stop(); });
            menu.Items.Add("Güncellemeleri denetle", null, delegate { Updater.CheckAsync(true); ShowSettings(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Çıkış", null, delegate { Close(); });
            _tray.ContextMenuStrip = menu;

            _pulse.Interval = 250;
            _pulse.Tick += delegate
            {
                bool speaking = Engine.IsSpeaking;
                if (speaking == _wasSpeaking) return;
                _wasSpeaking = speaking;
                _tray.Icon = speaking ? _speakingIcon : _idleIcon;
            };
            _pulse.Start();

            Updater = new Updater(this);
            Updater.UpdateFound += delegate (UpdateInfo u)
            {
                if (u.Version.ToString(3) == Settings.SkipVersion) return;
                _tray.ShowBalloonTip(8000, "Sesli Okuma " + u.Version.ToString(3) + " hazır", "Güncellemek için tıklayın.", ToolTipIcon.Info);
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

            if (!RegisterHotKey(Handle, 1, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_S))
            {
                Logger.Log("RegisterHotKey failed");
                _tray.ShowBalloonTip(6000, "Sesli Okuma", "Ctrl+Alt+S kaydedilemedi (başka uygulama kullanıyor)", ToolTipIcon.Warning);
            }
            else Logger.Log("hotkey registered");
        }

        void BuildIcons()
        {
            int size = 32;
            Color glyph = Theme.TaskbarIsDark() ? Color.White : Color.FromArgb(0x1B, 0x1D, 0x24);
            _idleIcon = IconFactory.Create(size, glyph, glyph, false);
            _speakingIcon = IconFactory.Create(size, glyph, Theme.Accent, true);
            AppIcon = IconFactory.Create(64, Theme.Accent, Theme.Accent, true);
            Icon = AppIcon;
        }

        void EnsureDefaultVoices()
        {
            bool changed = false;
            if (Engine.FindById(Settings.TrVoiceId) == null)
            {
                var v = Engine.FindByName("Emel") ?? Engine.FindByName("Ahmet") ?? Engine.FindByName("Tolga");
                if (v != null) { Settings.TrVoiceId = v.Id; changed = true; }
            }
            if (Engine.FindById(Settings.EnVoiceId) == null)
            {
                var v = Engine.FindByName("AndrewMultilingual") ?? Engine.FindByName("Aria") ?? Engine.FindByName("Zira");
                if (v != null) { Settings.EnVoiceId = v.Id; changed = true; }
            }
            if (changed) Settings.Save();
            Logger.Log("voices ready; TR=" + (TurkishVoice != null ? TurkishVoice.Name : "-") + " EN=" + (OtherVoice != null ? OtherVoice.Name : "-"));
        }

        public VoiceInfo TurkishVoice { get { return Engine.FindById(Settings.TrVoiceId); } }
        public VoiceInfo OtherVoice { get { return Engine.FindById(Settings.EnVoiceId); } }

        public void Speak(string text, VoiceInfo voice)
        {
            try { Engine.Speak(text, voice, Settings.Rate); }
            catch (Exception ex) { Logger.Log("speak failed: " + ex.Message); }
        }

        void ToggleSettings()
        {
            if (_settings == null || _settings.IsDisposed) _settings = new SettingsForm(this);
            if (_settings.Visible) _settings.Hide(); else _settings.ShowNearTray();
        }

        void ShowSettings()
        {
            if (_settings == null || _settings.IsDisposed) _settings = new SettingsForm(this);
            if (!_settings.Visible) _settings.ShowNearTray();
        }

        protected override void SetVisibleCore(bool value) { base.SetVisibleCore(false); }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY) { OnHotkey(); return; }
            if (m.Msg == WM_SHOWSETTINGS) { ToggleSettings(); return; }
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

        static string GetSelectionViaCopy()
        {
            string old = null;
            try { if (Clipboard.ContainsText()) old = Clipboard.GetText(); } catch { }
            try { Clipboard.Clear(); } catch { }
            keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            Thread.Sleep(30);
            keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
            keybd_event(VK_C, 0, 0, UIntPtr.Zero);
            keybd_event(VK_C, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            string text = null;
            for (int i = 0; i < 12; i++)
            {
                Thread.Sleep(60);
                try { if (Clipboard.ContainsText()) { text = Clipboard.GetText(); break; } } catch { }
            }
            if (old != null) { try { Clipboard.SetText(old); } catch { } }
            return (text != null && text.Trim().Length > 0) ? text : null;
        }

        static string GetClipboardText()
        {
            try { if (Clipboard.ContainsText()) return Clipboard.GetText(); } catch { }
            return null;
        }

        static bool LooksTurkish(string text)
        {
            const string trChars = "çğışöüÇĞİŞÖÜ";
            foreach (char c in trChars) if (text.IndexOf(c) >= 0) return true;
            return false;
        }

        void OnHotkey()
        {
            if (_busy) return;
            _busy = true;
            try
            {
                if (Engine.IsSpeaking) { Engine.Stop(); Logger.Log("stopped"); return; }
                if (!Engine.IsAvailable) { Logger.Log("no voice"); return; }
                if (Engine.Voices.Count == 0) { Engine.RefreshVoices(); EnsureDefaultVoices(); }

                string source = "uia";
                string text = GetSelectionViaUia();
                if (text == null) { source = "copy"; text = GetSelectionViaCopy(); }
                if (text == null) { source = "clipboard"; text = GetClipboardText(); }
                if (text == null || text.Trim().Length == 0) { Logger.Log("no text app=" + ForegroundApp()); return; }

                bool turkish = LooksTurkish(text);
                VoiceInfo v = turkish ? (TurkishVoice ?? OtherVoice) : (OtherVoice ?? TurkishVoice);
                Speak(text, v);
                Logger.Log("speak " + source + " " + (turkish ? "TR" : "EN") + " len=" + text.Length + " app=" + ForegroundApp());
            }
            catch (Exception ex) { Logger.Log("hotkey error: " + ex.Message); }
            finally { _busy = false; }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            UnregisterHotKey(Handle, 1);
            _pulse.Stop();
            _updateTimer.Stop();
            Engine.Stop();
            _tray.Visible = false;
            base.OnFormClosed(e);
        }

        [STAThread]
        static void Main()
        {
            bool createdNew;
            using (var mutex = new Mutex(true, @"Local\SesliOkumaHotkey", out createdNew))
            {
                if (!createdNew)
                {
                    IntPtr h = FindWindow(null, HostTitle);
                    if (h != IntPtr.Zero) PostMessage(h, WM_SHOWSETTINGS, IntPtr.Zero, IntPtr.Zero);
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayApp());
            }
        }
    }
}
