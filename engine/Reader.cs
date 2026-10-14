using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace SesliOkuma.Engine
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
        string _full = "";                    // what the bar shows (normalized whitespace)
        SpeechMap _map = new SpeechMap();     // what the voice gets, with offsets mapped both ways
        int _segAOffset, _segBOffset;         // spoken-text offsets of the two queued segments
        int _curLen;                          // length on screen of the word being spoken (SAPI path)
        int _streamA = -1, _streamB = -1;
        DateTime _launched, _lastSpeaking;
        bool _sawSpeaking;
        VoiceInfo _voice;
        LocalSession _local;                  // non-null while the local (Ema) voice is reading

        bool IsLocal { get { return _voice != null && _voice.Provider == VoiceProvider.Local; } }

        public VoiceInfo Voice { get { return _voice; } }
        public bool Active { get; private set; }
        public bool Paused { get; private set; }
        public string FullText { get { return _full; } }
        public event Action Changed;          // start/stop/pause state
        public event Action Position;         // word position advanced (highlight)
        public event Action Finished;
        public event Action<bool> Completed;           // true = reached the end; false = stopped by the user
        public int LastStopOffset { get; private set; }
        public static Action<bool> PauseAnnounce;   // accessibility mode: true = paused, false = resumed

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

        public void Start(string text, VoiceInfo voice) { StartAt(text, voice, 0); }

        public static string NormalizeText(string text) { return Normalize(text); }

        // Starts at an offset that is already in normalized coordinates (resume from a saved position).
        public void StartAtNorm(string text, VoiceInfo voice, int normOffset)
        {
            Stop(false);
            _full = Normalize(text);
            if (_full.Length == 0) return;
            _voice = voice;
            BuildMap();
            int off = Math.Max(0, Math.Min(normOffset, _full.Length - 1));
            while (off > 0 && !char.IsWhiteSpace(_full[off - 1])) off--;
            _sentences.Clear(); _sentences.AddRange(Spans(_full));
            Active = true; Paused = false;
            SpeakFrom(off, off == 0);
            if (Changed != null) Changed();
        }

        public void StartAt(string text, VoiceInfo voice, int rawOffset)
        {
            Stop(false);
            _full = Normalize(text);
            if (_full.Length == 0) return;
            _voice = voice;
            BuildMap();
            int startOff = MapOffset(text, rawOffset);
            _sentences.Clear(); _sentences.AddRange(Spans(_full));
            Active = true; Paused = false;
            SpeakFrom(startOff, startOff == 0);
            if (Changed != null) Changed();
        }

        int MapOffset(string raw, int rawOffset)
        {
            if (rawOffset <= 0) return 0;
            int off = Normalize(raw.Substring(0, Math.Min(rawOffset, raw.Length))).Length;
            if (off >= _full.Length) off = _full.Length - 1;
            while (off > 0 && !char.IsWhiteSpace(_full[off - 1])) off--;
            return off;
        }

        int _localBase;                       // spoken-text offset where the local session's text starts

        // The speech layer depends on the voice's language (English words are respelled for Turkish voices only).
        void BuildMap()
        {
            _map = SpeechText.Build(_full, _voice != null ? _voice.Lang2 : "");
            if (_map.Replacements > 0) EngineLog.Log("speech layer: " + _map.Replacements + " change(s), e.g. " + _map.Examples);
        }

        // Speaks from the given on-screen offset. With leadIn, a short first chunk keeps online voices fast.
        void SpeakFrom(int offset, bool leadIn)
        {
            int sOff = _map.ToSpoken(offset);
            string spoken = _map.Spoken;
            if (sOff >= spoken.Length || spoken.Substring(sOff).Trim().Length == 0) { EngineLog.Log("reader: nothing left to speak from " + offset); Stop(true); return; }
            string rest = spoken.Substring(sOff);
            _launched = DateTime.UtcNow; _lastSpeaking = DateTime.UtcNow; _sawSpeaking = false;
            _streamB = -1;
            if (_local != null) { _local.Dispose(); _local = null; }
            if (IsLocal)
            {
                _engine.Stop();
                string error = LocalTrVoice.EnsureLoaded();
                EngineLog.Log("reader start off=" + offset + " len=" + rest.Length + " voice=local");
                if (error != null) { EngineLog.Log("local voice unavailable: " + error); Stop(false); return; }
                _localBase = sOff;
                _local = new LocalSession(rest, _rate());
                return;
            }
            int cut = -1;
            if (leadIn && rest.Length > 150)
            {
                foreach (char sep in new[] { ',', ';', ':', '.', '!', '?' }) { int i = rest.IndexOf(sep, 16); if (i > 0 && i < 64 && (cut < 0 || i < cut)) cut = i; }
                if (cut < 0) { int sp = rest.IndexOf(' ', 40); if (sp > 0 && sp < 72) cut = sp; }
            }
            EngineLog.Log("reader start off=" + offset + " len=" + rest.Length + " lead=" + (cut > 0 ? cut : 0));
            try
            {
                if (cut > 0)
                {
                    _segAOffset = sOff;
                    _streamA = _engine.Speak(rest.Substring(0, cut + 1), _voice, _rate());
                    _segBOffset = sOff + cut + 1;
                    while (_segBOffset < spoken.Length && spoken[_segBOffset] == ' ') _segBOffset++;
                    _streamB = _engine.SpeakQueued(spoken.Substring(_segBOffset));
                }
                else
                {
                    _segAOffset = sOff;
                    _streamA = _engine.Speak(rest, _voice, _rate());
                }
            }
            catch (Exception ex) { EngineLog.Log("reader speak: " + ex.Message); }
        }

        // Current absolute character index within _full, from the engine's word position.
        public int CharIndex
        {
            get
            {
                if (!Active) return 0;
                if (IsLocal)
                {
                    int ls, ll;
                    if (_local != null && _local.CurrentWord(out ls, out ll))
                    {
                        int fs, fl;
                        _map.Locate(_localBase + ls, ll, out fs, out fl);
                        _lastLocalIndex = fs; _lastLocalLen = fl;
                    }
                    return Math.Min(Math.Max(0, _lastLocalIndex), Math.Max(0, _full.Length - 1));
                }
                int stream = _engine.CurrentStream;
                int baseOff = (stream == _streamB && _streamB >= 0) ? _segBOffset : _segAOffset;
                int pos = _engine.WordPosition;
                int fullStart, fullLen;
                _map.Locate(baseOff + Math.Max(0, pos), _engine.WordLength, out fullStart, out fullLen);
                _curLen = fullLen;
                return Math.Min(fullStart, Math.Max(0, _full.Length - 1));
            }
        }

        int _lastLocalIndex, _lastLocalLen;

        public int WordLength { get { return Math.Max(0, IsLocal ? _lastLocalLen : _curLen); } }
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
            if (IsLocal)
            {
                if (!Paused && _local != null)
                {
                    if (_local.StartedPlaying && !_sawSpeaking)
                    {
                        _sawSpeaking = true;
                        EngineLog.Log("time-to-audio " + (int)(DateTime.UtcNow - _launched).TotalMilliseconds + " ms (local)");
                    }
                    if (_local.FinishedPlayback) { Stop(_local.Error == null); return; }
                }
                int li = CharIndex;
                if (li != _lastIndex) { _lastIndex = li; if (Position != null) Position(); }
                return;
            }
            if (!Paused)
            {
                bool speaking = _engine.IsSpeaking;
                if (speaking)
                {
                    if (!_sawSpeaking) EngineLog.Log("time-to-audio " + (int)(DateTime.UtcNow - _launched).TotalMilliseconds + " ms");
                    _sawSpeaking = true; _lastSpeaking = DateTime.UtcNow;
                }
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
            if (Paused) { Paused = false; _lastSpeaking = DateTime.UtcNow; if (IsLocal) { if (_local != null) _local.Resume(); } else _engine.Resume(); }
            else { Paused = true; if (IsLocal) { if (_local != null) _local.Pause(); } else _engine.Pause(); }
            EngineLog.Log(Paused ? "reader paused" : "reader resumed");
            if (PauseAnnounce != null) PauseAnnounce(Paused);
            if (Changed != null) Changed();
        }

        public void Skip() { Jump(1); }
        public void Back() { Jump(-1); }

        void Jump(int delta)
        {
            if (!Active || _sentences.Count == 0) return;
            if (Paused) { Paused = false; if (!IsLocal) try { _engine.Resume(); } catch { } }
            int i = SentenceIndexAt(CharIndex) + delta;
            if (i >= _sentences.Count) { Stop(true); return; }
            if (i < 0) i = 0;
            EngineLog.Log("reader jump to sentence " + (i + 1) + "/" + _sentences.Count);
            SpeakFrom(_sentences[i].Start, false);
            if (Changed != null) Changed();
        }

        // Voice changed from the bar: continue from the start of the current sentence with the new voice.
        public void SwitchVoice(VoiceInfo voice)
        {
            if (voice == null) return;
            _voice = voice;
            if (!Active || _sentences.Count == 0) return;
            BuildMap();
            if (Paused) Paused = false;
            SpeakFrom(_sentences[SentenceIndexAt(CharIndex)].Start, false);
            if (Changed != null) Changed();
        }

        // Rate changed: continue from the start of the current sentence at the new rate.
        public void Restart()
        {
            if (!Active || _sentences.Count == 0) return;
            if (Paused) { Paused = false; if (!IsLocal) try { _engine.Resume(); } catch { } }
            SpeakFrom(_sentences[SentenceIndexAt(CharIndex)].Start, false);
            if (Changed != null) Changed();
        }

        public void Stop(bool finished)
        {
            bool was = Active;
            if (was) { LastStopOffset = CharIndex; EngineLog.Log(finished ? "reader finished" : "reader stopped"); }
            Active = false; Paused = false; _lastIndex = -1; _lastLocalIndex = 0; _lastLocalLen = 0;
            if (_local != null) { _local.Dispose(); _local = null; }
            try { _engine.Resume(); } catch { }
            _engine.Stop();
            if (was && Changed != null) Changed();
            if (was && Finished != null) Finished();
            if (was && Completed != null) Completed(finished);
        }
    }
}
