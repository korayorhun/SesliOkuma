using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using EmaLightning.Onnx;

namespace SesliOkuma.TrVoice;

// Reflection-friendly bridge for SesliOkuma.exe (C# 5 / csc): static methods, primitive arrays only.
// One prepared text at a time; the app serialises calls.
public static class EmaTts
{
    private static readonly object Gate = new();
    private static EmaEngine? _engine;
    private static EmaFrontend? _frontend;
    private static NormalizerTr? _normalizer;

    private sealed record Chunk(int Start, int Length, double Pause);

    private static string _text = "";
    private static List<Chunk> _chunks = new();

    private static readonly Regex Token = new(@"\S+", RegexOptions.Compiled);
    private static readonly (Regex Pattern, double Pause)[] Cuts =
    {
        (new Regex(@"[.!?]+[""')]*(?= )", RegexOptions.Compiled), TextChunker.SentencePause),
        (new Regex(@"[,;:](?= )", RegexOptions.Compiled), TextChunker.ClausePause),
        (new Regex(@"\S(?= )", RegexOptions.Compiled), TextChunker.ClausePause),
    };

    // "" on success, otherwise the reason. Safe to call again (no-op once loaded).
    public static string Load(string modelsDir, int threads)
    {
        lock (Gate)
        {
            if (_engine != null)
                return "";
            try
            {
                var engine = new EmaEngine(modelsDir, threads);
                _normalizer = NormalizerTr.TryCreate(out var error);
                if (_normalizer == null)
                    Log("normalizer unavailable: " + error);
                _frontend = new EmaFrontend(engine.Vocab, _normalizer);
                _engine = engine;
                return "";
            }
            catch (Exception ex)
            {
                return ex.GetType().Name + ": " + ex.Message;
            }
        }
    }

    public static int GetSampleRate() => _engine?.SampleRate ?? 0;

    public static string GetInfo()
    {
        var engine = _engine;
        return engine == null ? "" : "EMA Lightning " + (engine.Revision ?? "?").Substring(0, Math.Min(8, (engine.Revision ?? "?").Length)) + " / ONNX Runtime " + EmaEngine.OrtVersion;
    }

    // Splits the ORIGINAL text into ~10 s pieces (the model's own chunking rules) and returns how many there are.
    public static int Prepare(string text, double speed)
    {
        lock (Gate)
        {
            _text = text ?? "";
            _chunks = new List<Chunk>();
            var limit = (int)Math.Min(TextChunker.MaxLetters, TextChunker.LettersPerSecond * TextChunker.MaxSeconds * speed);
            var position = 0;
            while (position < _text.Length)
            {
                while (position < _text.Length && char.IsWhiteSpace(_text[position]))
                    position++;
                var remaining = _text.Length - position;
                if (remaining <= 0)
                    break;
                var cut = remaining;
                var pause = 0.0;
                if (remaining > limit)
                {
                    cut = limit;
                    var window = _text.Substring(position, Math.Min(remaining, limit + 1));
                    foreach (var (pattern, gap) in Cuts)
                    {
                        var last = -1;
                        foreach (Match match in pattern.Matches(window))
                            last = match.Index + match.Length;
                        if (last >= 0)
                        {
                            cut = last;
                            pause = gap;
                            break;
                        }
                    }
                }
                var length = cut;
                while (length > 0 && char.IsWhiteSpace(_text[position + length - 1]))
                    length--;
                if (length > 0 && Regex.IsMatch(_text.Substring(position, length), @"\p{L}"))
                    _chunks.Add(new Chunk(position, length, pause));
                position += cut;
            }
            if (_chunks.Count > 0)
                _chunks[_chunks.Count - 1] = _chunks[_chunks.Count - 1] with { Pause = 0.0 };
            return _chunks.Count;
        }
    }

    // [0] int start, [1] int length — the chunk's place in the text given to Prepare.
    public static int[] GetChunkRange(int index)
    {
        lock (Gate)
        {
            var chunk = _chunks[index];
            return new[] { chunk.Start, chunk.Length };
        }
    }

    // Synthesizes one chunk. Returns:
    // [0] float[] audio (mono, GetSampleRate), [1] double pauseSeconds,
    // [2] int[] wordStart, [3] int[] wordLen (offsets in the Prepare text), [4] double[] wordT0, [5] double[] wordT1 (seconds).
    public static object[] Synth(int index, double speed, long seed)
    {
        lock (Gate)
        {
            var engine = _engine ?? throw new InvalidOperationException("not loaded");
            var frontend = _frontend!;
            var chunk = _chunks[index];
            var slice = _text.Substring(chunk.Start, chunk.Length);

            var tokens = Token.Matches(slice).Cast<Match>().ToList();
            var adapted = tokens.Select(match => EnglishAdapter.Adapt(match.Value)).ToList();
            var joined = string.Join(" ", adapted);
            var core = joined.TrimEnd('"', '\'', ')');
            if (core.Length == 0 || core[core.Length - 1] is not ('.' or '!' or '?'))
                joined = joined.TrimEnd(',', ';', ':', '-', ' ') + ".";

            var spoken = frontend.Spoken(joined);
            var speech = engine.Synthesize(spoken, speed, seed);

            // Each token's own spoken form tells how many spoken words it produced; walk the synth word list with that.
            var counts = adapted.Select(token => frontend.Spoken(token).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length).ToList();
            var starts = new List<int>();
            var lengths = new List<int>();
            var t0 = new List<double>();
            var t1 = new List<double>();
            var mismatch = counts.Sum() != speech.Words.Count;
            var cursor = 0;
            for (var i = 0; i < tokens.Count; i++)
            {
                int take = mismatch
                    ? (int)Math.Round((i + 1) * (double)speech.Words.Count / tokens.Count) - (int)Math.Round(i * (double)speech.Words.Count / tokens.Count)
                    : counts[i];
                if (take <= 0 || cursor >= speech.Words.Count)
                    continue;
                take = Math.Min(take, speech.Words.Count - cursor);
                starts.Add(chunk.Start + tokens[i].Index);
                lengths.Add(tokens[i].Length);
                t0.Add(speech.Words[cursor].Start);
                t1.Add(speech.Words[cursor + take - 1].End);
                cursor += take;
            }
            return new object[] { speech.Audio, chunk.Pause, starts.ToArray(), lengths.ToArray(), t0.ToArray(), t1.ToArray() };
        }
    }

    // Blocking synthesis of everything into one 16-bit PCM WAV (for "save as audio").
    public static string SynthToWav(string text, double speed, long seed, string path)
    {
        try
        {
            var count = Prepare(text, speed);
            var audio = new List<float>();
            var rate = GetSampleRate();
            for (var i = 0; i < count; i++)
            {
                var result = Synth(i, speed, seed);
                audio.AddRange((float[])result[0]);
                var pause = (double)result[1];
                if (pause > 0)
                    audio.AddRange(new float[(int)(pause * rate)]);
            }
            WaveFile.Write(path, audio.ToArray(), rate);
            return "";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private static void Log(string message)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SesliOkuma");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "SesliOkuma.log"), DateTime.Now.ToString("s") + " trvoice: " + message + Environment.NewLine);
        }
        catch
        {
        }
    }
}
