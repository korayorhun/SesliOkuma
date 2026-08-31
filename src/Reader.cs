using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace SesliOkuma
{
    // Single-utterance playback: the whole text is sent to the engine at once (no per-sentence gaps).
    // A short lead-in chunk keeps the start fast for online voices; sentence positions are tracked
    // locally for prev/next, the collapsed label, progress and the soft word highlight.
    public sealed class Reader
    {
        struct Span { public int Start, Length; }

        readonly SpeechEngine _engine;
        readonly Func<int> _rate;
        readonly List<Span> _sentences = new List<Span>();
        string _full = "";
        int _segAOffset, _segBOffset;         // char offsets of the two queued segments within _full
        int _streamA = -1, _streamB = -1;
        DateTime _launched, _lastSpeaking;
        bool _sawSpeaking;
        VoiceInfo _voice;

        public bool Active { get; private set; }
        public bool Paused { get; private set; }
        public string FullText { get { return _full; } }
        public event Action Changed;          // start/stop/pause state
        public event Action Position;         // word position advanced (highlight)
        public event Action Finished;

        public Reader(SpeechEngine engine, Func<int> rate) { _engine = engine; _rate = rate; }

        public static List<string> Split(string text)
        {
            var list = new List<string>();
            foreach (var sp in Spans(Normalize(text))) list.Add(Normalize(text).Substring(sp.Start, sp.Length));
            return list;
        }

        static string Normalize(string text)
        {
            // Line breaks inside copied text cause artificial pauses; flatten them to spaces.
            text = text.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
            return Regex.Replace(text, @"  +", " ").Trim();
        }

        static List<Span> Spans(string full)
        {
            var spans = new List<Span>();
            int start = 0;
            foreach (Match m in Regex.Matches(full, @"[\.!\?…]+(\s+|$)"))
            {
                int end = m.Index + m.Length;
                if (end - start > 1) spans.Add(new Span { Start = start, Length = end - start });
                start = end;
            }
            if (start < full.Length) spans.Add(new Span { Start = start, Length = full.Length - start });
            if (spans.Count == 0 && full.Length > 0) spans.Add(new Span { Start = 0, Length = full.Length });
            return spans;
        }

        public void Start(string text, VoiceInfo voice)
        {
            Stop(false);
            _full = Normalize(text);
            if (_full.Length == 0) return;
            _voice = voice;
            _sentences.Clear(); _sentences.AddRange(Spans(_full));
            Active = true; Paused = false;
            SpeakFrom(0, true);
            if (Changed != null) Changed();
        }

        // Speaks _full from the given offset. With leadIn, a short first chunk keeps online voices fast.
        void SpeakFrom(int offset, bool leadIn)
        {
            string rest = _full.Substring(offset);
            _launched = DateTime.UtcNow; _lastSpeaking = DateTime.UtcNow; _sawSpeaking = false;
            _streamB = -1;
            int cut = -1;
            if (leadIn && rest.Length > 150)
            {
                foreach (char sep in new[] { ',', ';', ':', '.', '!', '?' }) { int i = rest.IndexOf(sep, 20); if (i > 0 && i < 90 && (cut < 0 || i < cut)) cut = i; }
                if (cut < 0) { int sp = rest.IndexOf(' ', 60); if (sp > 0 && sp < 110) cut = sp; }
            }
            Logger.Log("reader start off=" + offset + " len=" + rest.Length + " lead=" + (cut > 0 ? cut : 0));
            try
            {
                if (cut > 0)
                {
                    _segAOffset = offset;
                    _streamA = _engine.Speak(rest.Substring(0, cut + 1), _voice, _rate());
                    _segBOffset = offset + cut + 1;
                    while (_segBOffset < _full.Length && _full[_segBOffset] == ' ') _segBOffset++;
                    _streamB = _engine.SpeakQueued(_full.Substring(_segBOffset));
                }
                else
                {
                    _segAOffset = offset;
                    _streamA = _engine.Speak(rest, _voice, _rate());
                }
            }
            catch (Exception ex) { Logger.Log("reader speak: " + ex.Message); }
        }

        // Current absolute character index within _full, from the engine's word position.
        public int CharIndex
        {
            get
            {
                if (!Active) return 0;
                int stream = _engine.CurrentStream;
                int baseOff = (stream == _streamB && _streamB >= 0) ? _segBOffset : _segAOffset;
                int pos = _engine.WordPosition;
                int idx = baseOff + Math.Max(0, pos);
                return Math.Min(idx, Math.Max(0, _full.Length - 1));
            }
        }

        public int WordLength { get { int l = _engine.WordLength; return l > 0 ? l : 0; } }
        public int SentenceCount { get { return _sentences.Count; } }
        public double Fraction { get { return _full.Length > 0 ? Math.Min(1.0, CharIndex / (double)_full.Length) : 0; } }

        int SentenceIndexAt(int charIndex)
        {
            for (int i = 0; i < _sentences.Count; i++)
                if (charIndex < _sentences[i].Start + _sentences[i].Length) return i;
            return _sentences.Count - 1;
        }

        public string CurrentSentence
        {
            get
            {
                if (!Active || _sentences.Count == 0) return "";
                var sp = _sentences[SentenceIndexAt(CharIndex)];
                return _full.Substring(sp.Start, sp.Length).Trim();
            }
        }

        int _lastIndex = -1;

        // Called ~every 120 ms from the UI timer.
        public void Tick()
        {
            if (!Active) return;
            if (!Paused)
            {
                bool speaking = _engine.IsSpeaking;
                if (speaking) { _sawSpeaking = true; _lastSpeaking = DateTime.UtcNow; }
                else
                {
                    double idle = (DateTime.UtcNow - _lastSpeaking).TotalMilliseconds;
                    double total = (DateTime.UtcNow - _launched).TotalMilliseconds;
                    if ((_sawSpeaking && idle > 1200) || (!_sawSpeaking && total > 15000)) { Stop(true); return; }
                }
            }
            int idx = CharIndex;
            if (idx != _lastIndex) { _lastIndex = idx; if (Position != null) Position(); }
        }

        public void TogglePause()
        {
            if (!Active) return;
            if (Paused) { Paused = false; _lastSpeaking = DateTime.UtcNow; _engine.Resume(); }
            else { Paused = true; _engine.Pause(); }
            Logger.Log(Paused ? "reader paused" : "reader resumed");
            if (Changed != null) Changed();
        }

        public void Skip() { Jump(1); }
        public void Back() { Jump(-1); }

        void Jump(int delta)
        {
            if (!Active || _sentences.Count == 0) return;
            if (Paused) { Paused = false; try { _engine.Resume(); } catch { } }
            int i = SentenceIndexAt(CharIndex) + delta;
            if (i >= _sentences.Count) { Stop(true); return; }
            if (i < 0) i = 0;
            Logger.Log("reader jump to sentence " + (i + 1) + "/" + _sentences.Count);
            SpeakFrom(_sentences[i].Start, false);
            if (Changed != null) Changed();
        }

        // Rate changed: continue from the start of the current sentence at the new rate.
        public void Restart()
        {
            if (!Active || _sentences.Count == 0) return;
            if (Paused) { Paused = false; try { _engine.Resume(); } catch { } }
            SpeakFrom(_sentences[SentenceIndexAt(CharIndex)].Start, false);
            if (Changed != null) Changed();
        }

        public void Stop(bool finished)
        {
            bool was = Active;
            if (was) Logger.Log(finished ? "reader finished" : "reader stopped");
            Active = false; Paused = false; _lastIndex = -1;
            try { _engine.Resume(); } catch { }
            _engine.Stop();
            if (was && Changed != null) Changed();
            if (was && Finished != null) Finished();
        }
    }

    // Floating bar shown while reading: draggable anywhere, hideable, speed menu, expandable full text
    // with a soft highlight that follows the spoken word.
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
        readonly RichTextBox _full = new RichTextBox();
        const int W = 560, BaseH = 56, FullH = 168;
        int _hlStart = -1, _hlLen;
        string _loadedText = "";
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
            _full.ReadOnly = true; _full.BorderStyle = BorderStyle.None; _full.BackColor = Theme.Bg; _full.ForeColor = Theme.Text;
            _full.Font = Theme.Body; _full.WordWrap = true; _full.ScrollBars = RichTextBoxScrollBars.Vertical; _full.TabStop = false;
            _full.Visible = false; _full.Cursor = Cursors.Default;
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

            MouseDown += Drag; _text.MouseDown += Drag;
            _reader.Changed += Sync;
            _reader.Position += SyncHighlight;
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

        static Color Soft(Color a, Color b, float t)
        {
            return Color.FromArgb((int)(a.R * t + b.R * (1 - t)), (int)(a.G * t + b.G * (1 - t)), (int)(a.B * t + b.B * (1 - t)));
        }

        void Sync()
        {
            if (IsDisposed) return;
            _pause.Text = _reader.Paused ? "\uE768" : "\uE769";
            Tips.Set(_pause, _reader.Paused ? L.T("Resume") : L.T("Pause"));
            _speed.Text = (1.0 + _settings.Rate * 0.1).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "×";
            bool exp = _settings.BarExpanded;
            _expand.Text = exp ? "\uE70D" : "\uE70E";
            Tips.Set(_expand, L.T(exp ? "CollapseTip" : "ExpandTip"));
            _text.Visible = !exp;
            _full.Visible = exp;
            int h = BaseH;
            if (exp)
            {
                if (_loadedText != _reader.FullText)
                {
                    _loadedText = _reader.FullText;
                    _full.Text = _loadedText;
                    _hlStart = -1; _hlLen = 0;
                }
                int th;
                using (var g = CreateGraphics())
                    th = Math.Min(FullH, Math.Max(28, TextRenderer.MeasureText(g, _loadedText, Theme.Body, new Size(W - 28, 2000), TextFormatFlags.WordBreak).Height + 12));
                _full.SetBounds(14, BaseH - 6, W - 28, th);
                h = BaseH + th + 6;
            }
            if (ClientSize.Height != h) ClientSize = new Size(W, h);
            if (!exp) _text.Text = _reader.CurrentSentence;
            Invalidate();
        }

        // Soft highlight of the word being spoken; also keeps the collapsed label and progress fresh.
        void SyncHighlight()
        {
            if (IsDisposed || !_reader.Active) return;
            if (_settings.BarExpanded && _full.Visible && _loadedText.Length > 0)
            {
                int start = _reader.CharIndex;
                int len = Math.Max(_reader.WordLength, 1);
                if (start != _hlStart)
                {
                    if (_hlStart >= 0 && _hlStart < _loadedText.Length)
                    {
                        _full.Select(Math.Max(0, _hlStart - 1), Math.Min(_hlLen + 2, _loadedText.Length - Math.Max(0, _hlStart - 1)));
                        _full.SelectionBackColor = _full.BackColor;
                    }
                    if (start < _loadedText.Length)
                    {
                        len = Math.Min(len, _loadedText.Length - start);
                        _full.Select(start, len);
                        _full.SelectionBackColor = Soft(Theme.Accent, Theme.Bg, 0.22f);
                        _hlStart = start; _hlLen = len;
                        _full.Select(start, 0);
                        _full.ScrollToCaret();
                    }
                }
            }
            else if (!_settings.BarExpanded) _text.Text = _reader.CurrentSentence;
            Invalidate(new Rectangle(0, Height - 6, Width, 6));
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
            float frac = (float)_reader.Fraction;
            using (var b = new SolidBrush(Theme.Track)) e.Graphics.FillRectangle(b, 12, Height - 5, Width - 24, 2);
            using (var b = new SolidBrush(Theme.Accent)) e.Graphics.FillRectangle(b, 12, Height - 5, (Width - 24) * frac, 2);
        }
    }
}
