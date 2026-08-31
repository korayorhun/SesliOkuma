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

    // Floating player: text on top (follows the spoken word with a soft highlight), a centered
    // transport row below, rounded chrome and an edge-to-edge progress hairline.
    public sealed class ReaderBar : Form
    {
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool ReleaseCapture();
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool HideCaret(IntPtr hWnd);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int index);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int index, int value);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
        const int GWL_EXSTYLE = -20, WS_EX_NOACTIVATE = 0x08000000;

        readonly Reader _reader;
        readonly AppSettings _settings;
        readonly Action _rateChanged;
        bool _editable;
        public event EventHandler CloseRequested;
        public event Action<string> PlayRequested;      // play pressed after reading finished (possibly edited text)
        readonly FlatButton _pause = new FlatButton { IconGlyph = true, Borderless = true, Accent = true, Size = new Size(38, 34) };
        readonly FlatButton _back = new FlatButton { IconGlyph = true, Borderless = true, Size = new Size(32, 34), Text = "\uE892" };
        readonly FlatButton _skip = new FlatButton { IconGlyph = true, Borderless = true, Size = new Size(32, 34), Text = "\uE893" };
        readonly FlatButton _speed = new FlatButton { Borderless = true, Size = new Size(48, 34) };
        readonly FlatButton _expand = new FlatButton { IconGlyph = true, Borderless = true, Size = new Size(28, 34), Text = "\uE70E" };
        readonly FlatButton _hide = new FlatButton { IconGlyph = true, Borderless = true, Size = new Size(28, 34), Text = "\uE921" };
        readonly FlatButton _close = new FlatButton { IconGlyph = true, Borderless = true, Size = new Size(28, 34), Text = "\uE711" };
        readonly Label _text = new Label { AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.Transparent, AutoEllipsis = true };
        readonly RichTextBox _full = new RichTextBox();
        const int W = 560, RowH = 46, TextMax = 150;
        int _hlStart = -1, _hlLen;
        string _loadedText = "";
        public event EventHandler HideRequested;

        public ReaderBar(Reader reader, AppSettings settings, Action rateChanged)
        {
            _reader = reader; _settings = settings; _rateChanged = rateChanged;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Bg; ForeColor = Theme.Text; Font = Theme.Body; ClientSize = new Size(W, RowH + 6); DoubleBuffered = true;
            Text = "SesliOkumaReaderBar";

            _text.Font = Theme.Body; _text.ForeColor = Theme.Text;
            _full.ReadOnly = true; _full.HideSelection = true; _full.BorderStyle = BorderStyle.None; _full.BackColor = Theme.Bg; _full.ForeColor = Theme.Text;
            _full.Font = new Font("Segoe UI", 10.5f); _full.WordWrap = true; _full.ScrollBars = RichTextBoxScrollBars.None; _full.TabStop = false;
            _full.Visible = false; _full.Cursor = Cursors.Default;
            Controls.AddRange(new Control[] { _back, _pause, _skip, _speed, _text, _expand, _hide, _close, _full });

            _pause.Click += delegate
            {
                if (_reader.Active) { _reader.TogglePause(); return; }
                string t = _settings.BarExpanded ? _full.Text : _loadedText;
                if (t != null && t.Trim().Length > 0 && PlayRequested != null) PlayRequested(t);
            };
            _skip.Click += delegate { _reader.Skip(); };
            _back.Click += delegate { _reader.Back(); };
            _close.Click += delegate { _reader.Stop(false); if (CloseRequested != null) CloseRequested(this, EventArgs.Empty); };
            _hide.Click += delegate { if (HideRequested != null) HideRequested(this, EventArgs.Empty); };
            _expand.Click += delegate { _settings.BarExpanded = !_settings.BarExpanded; _settings.Save(); Sync(); };
            _speed.Click += delegate { ShowSpeedMenu(); };
            Tips.Set(_back, L.T("Previous")); Tips.Set(_skip, L.T("Next")); Tips.Set(_close, L.T("Stop"));
            Tips.Set(_hide, L.T("HideBar")); Tips.Set(_speed, L.T("SpeedTip"));

            MouseDown += Drag; _text.MouseDown += Drag;
            _full.GotFocus += delegate { if (!_editable) HideCaret(_full.Handle); };
            _full.MouseDown += delegate { if (!_reader.Active) EnableEditing(); };
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
                string label = (1.0 + v * 0.1).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "\u00d7";
                if (v == 0) label += "  \u00b7  " + L.T("Normal");
                var item = new ToolStripMenuItem(label) { Tag = v, Checked = _settings.Rate == v };
                item.Click += delegate { _settings.Rate = (int)item.Tag; _settings.Save(); if (_rateChanged != null) _rateChanged(); _reader.Restart(); Sync(); };
                menu.Items.Add(item);
            }
            menu.Show(_speed, new Point(0, -6 * 32));
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

        // Explicit click into the text (while idle) hands the window normal activation so the user can edit.
        void EnableEditing()
        {
            if (_editable) return;
            _editable = true;
            SetWindowLong(Handle, GWL_EXSTYLE, GetWindowLong(Handle, GWL_EXSTYLE) & ~WS_EX_NOACTIVATE);
            _full.ReadOnly = false;
            SetForegroundWindow(Handle);
            _full.Focus();
        }

        void DisableEditing()
        {
            if (!_editable) return;
            _editable = false;
            SetWindowLong(Handle, GWL_EXSTYLE, GetWindowLong(Handle, GWL_EXSTYLE) | WS_EX_NOACTIVATE);
            _full.ReadOnly = true;
            HideCaret(_full.Handle);
        }

        void Sync()
        {
            if (IsDisposed) return;
            if (_reader.Active) DisableEditing();
            _pause.Text = !_reader.Active ? "\uE768" : (_reader.Paused ? "\uE768" : "\uE769");
            Tips.Set(_pause, !_reader.Active ? L.T("Listen") : (_reader.Paused ? L.T("Resume") : L.T("Pause")));
            _speed.Text = (1.0 + _settings.Rate * 0.1).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "\u00d7";
            bool exp = _settings.BarExpanded;
            _expand.Text = exp ? "\uE70D" : "\uE70E";
            Tips.Set(_expand, L.T(exp ? "CollapseTip" : "ExpandTip"));
            _text.Visible = !exp;
            _full.Visible = exp;

            int h;
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
                    th = Math.Min(TextMax, Math.Max(30, TextRenderer.MeasureText(g, _loadedText, _full.Font, new Size(W - 44, 2000), TextFormatFlags.WordBreak).Height + 10));
                _full.SetBounds(22, 16, W - 44, th);
                h = 16 + th + 6 + RowH;
                int rowY = h - RowH + 2;
                int cx = W / 2;
                _back.Location = new Point(cx - 16 - 4 - 32, rowY);
                _pause.Location = new Point(cx - 19, rowY);
                _skip.Location = new Point(cx + 19 + 4, rowY);
                _speed.Location = new Point(14, rowY);
                _expand.Location = new Point(W - 14 - 28 - 4 - 28 - 4 - 28, rowY);
                _hide.Location = new Point(W - 14 - 28 - 4 - 28, rowY);
                _close.Location = new Point(W - 14 - 28, rowY);
            }
            else
            {
                h = RowH + 6;
                int rowY = 6;
                _back.Location = new Point(10, rowY);
                _pause.Location = new Point(46, rowY);
                _skip.Location = new Point(88, rowY);
                _speed.Location = new Point(124, rowY);
                _text.SetBounds(180, rowY + 6, W - 180 - 104, 22);
                _text.Text = _reader.CurrentSentence;
                _expand.Location = new Point(W - 14 - 28 - 4 - 28 - 4 - 28, rowY);
                _hide.Location = new Point(W - 14 - 28 - 4 - 28, rowY);
                _close.Location = new Point(W - 14 - 28, rowY);
            }
            if (ClientSize.Height != h) ClientSize = new Size(W, h);
            Invalidate();
        }

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
                        // Scroll only when the word leaves the comfortable band; keeps lines from hopping.
                        var pt = _full.GetPositionFromCharIndex(start);
                        if (pt.Y < 4 || pt.Y > _full.Height - 26) { _full.Select(start, 0); _full.ScrollToCaret(); }
                        else _full.Select(start, 0);
                        if (!_editable) HideCaret(_full.Handle);
                    }
                }
            }
            else if (!_settings.BarExpanded) _text.Text = _reader.CurrentSentence;
            Invalidate(new Rectangle(0, Height - 4, Width, 4));
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
            using (var path = Theme.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 8))
            using (var pen = new Pen(Theme.Border)) e.Graphics.DrawPath(pen, path);
            float frac = (float)_reader.Fraction;
            using (var b = new SolidBrush(Theme.Track)) e.Graphics.FillRectangle(b, 1, Height - 3, Width - 2, 2);
            using (var b = new SolidBrush(Theme.Accent)) e.Graphics.FillRectangle(b, 1, Height - 3, (Width - 2) * frac, 2);
        }
    }
}