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
            if (_sentences.Count == 0) return;
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
            Active = false; Paused = false;
            try { _engine.Resume(); } catch { }
            _engine.Stop();
            if (was && Changed != null) Changed();
            if (was && finished && Finished != null) Finished();
            if (was && !finished && Finished != null) Finished();
        }
    }

    // Thin floating bar shown at the bottom of the screen while reading. Never takes focus.
    public sealed class ReaderBar : Form
    {
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        readonly Reader _reader;
        readonly Action<int> _rateDelta;      // +1 / -1 speed steps
        readonly Func<int> _rate;
        readonly FlatButton _pause = new FlatButton { IconGlyph = true, Size = new Size(34, 34) };
        readonly FlatButton _back = new FlatButton { IconGlyph = true, Size = new Size(34, 34), Text = "\uE892" };
        readonly FlatButton _skip = new FlatButton { IconGlyph = true, Size = new Size(34, 34), Text = "\uE893" };
        readonly FlatButton _slower = new FlatButton { Size = new Size(30, 34), Text = "−" };
        readonly FlatButton _faster = new FlatButton { Size = new Size(30, 34), Text = "+" };
        readonly FlatButton _close = new FlatButton { IconGlyph = true, Size = new Size(34, 34), Text = "\uE711" };
        readonly Label _speed = new Label { AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, BackColor = Color.Transparent };
        readonly Label _text = new Label { AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.Transparent, AutoEllipsis = true };
        const int W = 620, H = 56;

        public ReaderBar(Reader reader, Func<int> rate, Action<int> rateDelta)
        {
            _reader = reader; _rate = rate; _rateDelta = rateDelta;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Bg; ForeColor = Theme.Text; Font = Theme.Body; ClientSize = new Size(W, H); DoubleBuffered = true;
            Text = "SesliOkumaReaderBar";

            int x = 10;
            _back.Location = new Point(x, 11); x += 38;
            _pause.Location = new Point(x, 11); x += 38;
            _skip.Location = new Point(x, 11); x += 46;
            _slower.Location = new Point(x, 11); x += 32;
            _speed.Font = Theme.Small; _speed.ForeColor = Theme.Muted; _speed.SetBounds(x, 11, 42, 34); x += 44;
            _faster.Location = new Point(x, 11); x += 40;
            _text.Font = Theme.Body; _text.ForeColor = Theme.Text; _text.SetBounds(x, 17, W - x - 58, 22);
            _close.Location = new Point(W - 44, 11);
            Controls.AddRange(new Control[] { _back, _pause, _skip, _slower, _speed, _faster, _text, _close });

            _pause.Click += delegate { _reader.TogglePause(); };
            _skip.Click += delegate { _reader.Skip(); };
            _back.Click += delegate { _reader.Back(); };
            _close.Click += delegate { _reader.Stop(false); };
            _slower.Click += delegate { _rateDelta(-1); _reader.Restart(); Sync(); };
            _faster.Click += delegate { _rateDelta(1); _reader.Restart(); Sync(); };
            _reader.Changed += Sync;
            Sync();
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x08000000 | 0x00000080; cp.ClassStyle |= 0x20000; return cp; } // NOACTIVATE, TOOLWINDOW, DROPSHADOW
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { int round = 2; DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int)); int dark = Theme.Dark ? 1 : 0; DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int)); } catch { }
        }

        public void Place()
        {
            var wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Bottom - Height - 14);
        }

        void Sync()
        {
            if (IsDisposed) return;
            _pause.Text = _reader.Paused ? "\uE768" : "\uE769";
            _speed.Text = (1.0 + _rate() * 0.1).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "×";
            _text.Text = _reader.Current;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Theme.Prepare(e.Graphics);
            using (var pen = new Pen(Theme.Border)) e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            // progress line along the bottom edge
            float frac = _reader.Count > 0 ? (_reader.Index + 1) / (float)_reader.Count : 0f;
            using (var b = new SolidBrush(Theme.Track)) e.Graphics.FillRectangle(b, 12, Height - 5, Width - 24, 2);
            using (var b = new SolidBrush(Theme.Accent)) e.Graphics.FillRectangle(b, 12, Height - 5, (Width - 24) * frac, 2);
        }
    }
}
