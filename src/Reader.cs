using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace SesliOkuma
{
    // Sentence-by-sentence playback on top of SpeechEngine: pause/resume, skip, live speed, progress.
    public sealed class Reader
    {
        readonly SpeechEngine _engine;
        readonly Func<int> _rate;
        readonly List<string> _sentences = new List<string>();
        int _index = -1;
        DateTime _launched;
        bool _sawSpeaking;
        VoiceInfo _voice;

        public bool Active { get; private set; }
        public bool Paused { get; private set; }
        public int Count { get { return _sentences.Count; } }
        public int Index { get { return Math.Max(0, _index); } }
        public string Current { get { return _index >= 0 && _index < _sentences.Count ? _sentences[_index] : ""; } }
        public event Action Changed;      // state/progress changed
        public event Action Finished;

        public Reader(SpeechEngine engine, Func<int> rate) { _engine = engine; _rate = rate; }

        public static List<string> Split(string text)
        {
            var list = new List<string>();
            text = text.Replace("\r", "\n");
            foreach (string para in text.Split('\n'))
            {
                string p = para.Trim();
                if (p.Length == 0) continue;
                // Split after . ! ? … when followed by whitespace; keep short fragments together.
                var parts = Regex.Split(p, @"(?<=[\.!\?…])\s+(?=\S)");
                string pending = "";
                foreach (string s in parts)
                {
                    string t = s.Trim();
                    if (t.Length == 0) continue;
                    pending = pending.Length == 0 ? t : pending + " " + t;
                    if (pending.Length >= 24) { list.Add(pending); pending = ""; }
                }
                if (pending.Length > 0) list.Add(pending);
            }
            return list;
        }

        public void Start(string text, VoiceInfo voice)
        {
            Stop(false);
            _sentences.Clear();
            _sentences.AddRange(Split(text));
            // Online voices return audio faster for short requests: start with a short lead-in chunk.
            if (_sentences.Count > 0 && _sentences[0].Length > 70)
            {
                string first = _sentences[0];
                int cut = -1;
                foreach (char sep in new[] { ',', ';', ':' }) { int i = first.IndexOf(sep, 20); if (i > 0 && i < 90 && (cut < 0 || i < cut)) cut = i; }
                if (cut < 0) { int sp = first.IndexOf(' ', 60); if (sp > 0 && sp < first.Length - 20) cut = sp; }
                if (cut > 0) { _sentences[0] = first.Substring(cut + 1).Trim(); _sentences.Insert(0, first.Substring(0, cut + 1).Trim()); }
            }            if (_sentences.Count == 0) return;
            _voice = voice;
            Active = true; Paused = false; _index = -1;
            Next();
        }

        void SpeakCurrent()
        {
            _launched = DateTime.UtcNow; _sawSpeaking = false;
            try { _engine.Speak(_sentences[_index], _voice, _rate()); }
            catch (Exception ex) { Logger.Log("reader speak: " + ex.Message); }
            if (Changed != null) Changed();
        }

        void Next()
        {
            if (_index + 1 >= _sentences.Count) { Stop(true); return; }
            _index++;
            Logger.Log("reader sentence " + (_index + 1) + "/" + _sentences.Count);
            SpeakCurrent();
        }

        // Called ~every 150 ms from the UI timer.
        public void Tick()
        {
            if (!Active || Paused) return;
            bool speaking = _engine.IsSpeaking;
            if (speaking) { _sawSpeaking = true; return; }
            double ms = (DateTime.UtcNow - _launched).TotalMilliseconds;
            if (_sawSpeaking || ms > 6000) Next();          // finished (or the voice never started: give up on it)
        }

        public void TogglePause()
        {
            if (!Active) return;
            if (Paused) { Paused = false; _engine.Resume(); }
            else { Paused = true; _engine.Pause(); }
            Logger.Log(Paused ? "reader paused" : "reader resumed");
            if (Changed != null) Changed();
        }

        public void Skip()
        {
            if (!Active) return;
            if (Paused) { Paused = false; _engine.Resume(); }
            _engine.Stop();
            Next();
        }

        public void Back()
        {
            if (!Active) return;
            if (Paused) { Paused = false; _engine.Resume(); }
            _engine.Stop();
            _index = Math.Max(-1, _index - 2);
            Next();
        }

        // Speed changed while reading: restart the current sentence at the new rate.
        public void Restart()
        {
            if (!Active || _index < 0) return;
            if (Paused) { Paused = false; _engine.Resume(); }
            _engine.Stop();
            SpeakCurrent();
        }

        public void Stop(bool finished)
        {
            bool was = Active;
            if (was) Logger.Log(finished ? "reader finished" : "reader stopped");
            Active = false; Paused = false;
            try { _engine.Resume(); } catch { }
            _engine.Stop();
            if (was && Changed != null) Changed();
            if (was && finished && Finished != null) Finished();
            if (was && !finished && Finished != null) Finished();
        }
    }

    // Floating bar shown while reading: draggable anywhere, hideable, speed menu, expandable full sentence.
    public sealed class ReaderBar : Form
    {
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool ReleaseCapture();
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        readonly Reader _reader;
        readonly AppSettings _settings;
        readonly Action _rateChanged;
        readonly FlatButton _pause = new FlatButton { IconGlyph = true, Borderless = true, Size = new Size(34, 34) };
        readonly FlatButton _back = new FlatButton { IconGlyph = true, Borderless = true, Size = new Size(34, 34), Text = "\uE892" };
        readonly FlatButton _skip = new FlatButton { IconGlyph = true, Borderless = true, Size = new Size(34, 34), Text = "\uE893" };
        readonly FlatButton _speed = new FlatButton { Borderless = true, Size = new Size(52, 34) };
        readonly FlatButton _expand = new FlatButton { IconGlyph = true, Borderless = true, Size = new Size(30, 34), Text = "\uE70E" };
        readonly FlatButton _hide = new FlatButton { IconGlyph = true, Borderless = true, Size = new Size(30, 34), Text = "\uE921" };
        readonly FlatButton _close = new FlatButton { IconGlyph = true, Borderless = true, Size = new Size(30, 34), Text = "\uE711" };
        readonly Label _text = new Label { AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.Transparent, AutoEllipsis = true };
        readonly Label _full = new Label { AutoSize = false, BackColor = Color.Transparent };
        const int W = 560, BaseH = 56;
        public event EventHandler HideRequested;

        public ReaderBar(Reader reader, AppSettings settings, Action rateChanged)
        {
            _reader = reader; _settings = settings; _rateChanged = rateChanged;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Bg; ForeColor = Theme.Text; Font = Theme.Body; ClientSize = new Size(W, BaseH); DoubleBuffered = true;
            Text = "SesliOkumaReaderBar";

            int x = 10;
            _back.Location = new Point(x, 11); x += 38;
            _pause.Location = new Point(x, 11); x += 38;
            _skip.Location = new Point(x, 11); x += 42;
            _speed.Location = new Point(x, 11); x += 60;
            _text.Font = Theme.Body; _text.ForeColor = Theme.Text; _text.SetBounds(x, 17, W - x - 104, 22);
            _close.Location = new Point(W - 40, 11);
            _hide.Location = new Point(W - 40 - 32, 11);
            _expand.Location = new Point(W - 40 - 64, 11);
            _full.Font = Theme.Body; _full.ForeColor = Theme.Text; _full.Visible = false;
            Controls.AddRange(new Control[] { _back, _pause, _skip, _speed, _text, _expand, _hide, _close, _full });

            _pause.Click += delegate { _reader.TogglePause(); };
            _skip.Click += delegate { _reader.Skip(); };
            _back.Click += delegate { _reader.Back(); };
            _close.Click += delegate { _reader.Stop(false); };
            _hide.Click += delegate { if (HideRequested != null) HideRequested(this, EventArgs.Empty); };
            _expand.Click += delegate { _settings.BarExpanded = !_settings.BarExpanded; _settings.Save(); Sync(); };
            _speed.Click += delegate { ShowSpeedMenu(); };
            Tips.Set(_back, L.T("Previous")); Tips.Set(_skip, L.T("Next")); Tips.Set(_close, L.T("Stop"));
            Tips.Set(_hide, L.T("HideBar")); Tips.Set(_speed, L.T("SpeedTip"));

            // drag from anywhere that is not a button
            MouseDown += Drag; _text.MouseDown += Drag; _full.MouseDown += Drag;
            _reader.Changed += Sync;
            Sync();
        }

        void ShowSpeedMenu()
        {
            var menu = ThemedMenu.Create();
            int[] steps = { -4, -2, 0, 2, 4, 6 };
            foreach (int v in steps)
            {
                string label = (1.0 + v * 0.1).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "×";
                if (v == 0) label += "  ·  " + L.T("Normal");
                var item = new ToolStripMenuItem(label) { Tag = v, Checked = _settings.Rate == v };
                item.Click += delegate { _settings.Rate = (int)item.Tag; _settings.Save(); if (_rateChanged != null) _rateChanged(); _reader.Restart(); Sync(); };
                menu.Items.Add(item);
            }
            menu.Show(_speed, new Point(0, _speed.Height + 4));
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x08000000 | 0x00000080; cp.ClassStyle |= 0x20000; return cp; }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { int round = 2; DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int)); int dark = Theme.Dark ? 1 : 0; DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int)); } catch { }
        }

        public void Place()
        {
            var wa = Screen.PrimaryScreen.WorkingArea;
            var saved = new Point(_settings.BarX, _settings.BarY);
            bool ok = _settings.BarX > -30000 && _settings.BarX != -1;
            if (ok) { ok = false; foreach (var sc in Screen.AllScreens) if (sc.WorkingArea.Contains(new Rectangle(saved, new Size(60, 30)))) { ok = true; break; } }
            Location = ok ? saved : new Point(wa.Left + (wa.Width - Width) / 2, wa.Bottom - Height - 14);
        }

        protected override void OnMove(EventArgs e)
        {
            base.OnMove(e);
            if (Visible) { _settings.BarX = Location.X; _settings.BarY = Location.Y; }
        }

        void Sync()
        {
            if (IsDisposed) return;
            _pause.Text = _reader.Paused ? "\uE768" : "\uE769";
            Tips.Set(_pause, _reader.Paused ? L.T("Resume") : L.T("Pause"));
            _speed.Text = (1.0 + _settings.Rate * 0.1).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "×";
            _text.Text = _reader.Current;
            bool exp = _settings.BarExpanded;
            _expand.Text = exp ? "\uE70D" : "\uE70E";
            Tips.Set(_expand, L.T(exp ? "CollapseTip" : "ExpandTip"));
            _text.Visible = !exp;
            int h = BaseH;
            if (exp)
            {
                int th;
                using (var g = CreateGraphics())
                    th = Math.Min(150, Math.Max(22, TextRenderer.MeasureText(g, _reader.Current, Theme.Body, new Size(W - 28, 1000), TextFormatFlags.WordBreak).Height));
                _full.SetBounds(14, BaseH - 4, W - 28, th);
                _full.Text = _reader.Current;
                _full.Visible = true;
                h = BaseH + th + 8;
            }
            else _full.Visible = false;
            if (ClientSize.Height != h) ClientSize = new Size(W, h);
            Invalidate();
        }

        void Drag(object s, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            ReleaseCapture();
            SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Theme.Prepare(e.Graphics);
            using (var pen = new Pen(Theme.Border)) e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            float frac = _reader.Count > 0 ? (_reader.Index + 1) / (float)_reader.Count : 0f;
            using (var b = new SolidBrush(Theme.Track)) e.Graphics.FillRectangle(b, 12, Height - 5, Width - 24, 2);
            using (var b = new SolidBrush(Theme.Accent)) e.Graphics.FillRectangle(b, 12, Height - 5, (Width - 24) * frac, 2);
        }
    }
}