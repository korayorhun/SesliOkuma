using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace EmaLightning.Onnx;

// One spoken piece and when each of its words is heard, in seconds from the start of the piece.
public sealed record EmaSpeech(float[] Audio, int Frames, IReadOnlyList<EmaWord> Words);

public sealed record EmaWord(string Text, double Start, double End);

// EMA Lightning (Canberk Aslan, Apache-2.0) on ONNX Runtime: the three graphs exported from the published weights
// (export/export.py), the frame plan of engine.py and seeded noise, one piece at a time. Letters of the model's alphabet in,
// 48 kHz audio out.
public sealed class EmaEngine : IDisposable
{
    private readonly InferenceSession _text;
    private readonly InferenceSession _sound;
    private readonly InferenceSession _decoder;
    private readonly Dictionary<char, long> _stoi = new();
    private readonly int _steps;
    private readonly int _latentDim;
    private readonly int _maxWordFrames;
    private readonly int _maxFrames;

    public EmaEngine(string folder, int threads)
    {
        var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "model-config.json"))).RootElement;
        Vocab = JsonSerializer.Deserialize<string[]>(File.ReadAllText(Path.Combine(folder, "vocab.json"))) ?? throw new InvalidDataException("vocab.json");
        for (var i = 0; i < Vocab.Length; i++)
            if (Vocab[i].Length == 1)
                _stoi[Vocab[i][0]] = i;
        SampleRate = config.GetProperty("sample_rate").GetInt32();
        Hop = config.GetProperty("hop").GetInt32();
        LatentRate = config.GetProperty("latent_rate").GetInt32();
        _latentDim = config.GetProperty("latent_dim").GetInt32();
        _steps = config.GetProperty("times").GetArrayLength();
        _maxWordFrames = config.GetProperty("max_word_frames").GetInt32();
        _maxFrames = config.GetProperty("max_frames").GetInt32();
        Revision = config.TryGetProperty("revision", out var revision) ? revision.GetString() : null;
        using var options = new SessionOptions
        {
            IntraOpNumThreads = threads,
            InterOpNumThreads = 1,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
        };
        _text = new InferenceSession(Path.Combine(folder, "text.onnx"), options);
        try
        {
            _sound = new InferenceSession(Path.Combine(folder, "sound.onnx"), options);
            try { _decoder = new InferenceSession(Path.Combine(folder, "decoder.onnx"), options); }
            catch { _sound.Dispose(); throw; }
        }
        catch { _text.Dispose(); throw; }
    }

    public string[] Vocab { get; }

    public int SampleRate { get; }

    public int Hop { get; }

    public int LatentRate { get; }

    public string? Revision { get; }

    public static string OrtVersion => typeof(InferenceSession).Assembly.GetName().Version?.ToString(3) ?? "?";

    // `spoken` is the frontend's output for one piece: lowercase letters of the alphabet, single spaces, ending in punctuation.
    public EmaSpeech Synthesize(string spoken, double speed, long seed)
    {
        var length = spoken.Length;
        if (length == 0)
            return new EmaSpeech([], 0, []);
        var ids = new long[length];
        for (var i = 0; i < length; i++)
            ids[i] = _stoi.TryGetValue(spoken[i], out var id) ? id : 1;

        // engine.piece: a word starts at every letter that follows a space; the first word starts at 0 whatever it is
        var starts = new List<int>();
        for (var i = 0; i < length; i++)
            if (spoken[i] != ' ' && (i == 0 || spoken[i - 1] == ' '))
                starts.Add(i);
        if (starts.Count == 0)
            starts.Add(0);
        var bounds = new List<int> { 0 };
        bounds.AddRange(starts.Skip(1));
        bounds.Add(length);
        var words = bounds.Count - 1;
        var cw = new long[length];
        var wstart = new long[length];
        for (var w = 0; w < words; w++)
            for (var i = bounds[w]; i < bounds[w + 1]; i++)
            {
                cw[i] = w;
                wstart[i] = bounds[w];
            }

        var mask = new bool[length];
        for (var i = 0; i < length; i++)
            mask[i] = ids[i] != 0;
        float[] h, dur;
        using (var result = _text.Run(new[]
        {
            NamedOnnxValue.CreateFromTensor("ids", new DenseTensor<long>(ids, [1, length])),
            NamedOnnxValue.CreateFromTensor("mask", new DenseTensor<bool>(mask, [1, length])),
        }))
        {
            h = result.First(value => value.Name == "h").AsTensor<float>().ToArray();
            dur = result.First(value => value.Name == "dur").AsTensor<float>().ToArray();
        }

        // engine.plan: frames per word (float32 sums like scatter_add_), half-to-even rounding, 1..max per word, a cap in all
        for (var i = 0; i < length; i++)
            dur[i] = (float)(dur[i] / speed);
        var sums = new float[words];
        for (var i = 0; i < length; i++)
            sums[cw[i]] += dur[i];
        var counts = new long[words];
        long total = 0;
        for (var w = 0; w < words; w++)
        {
            counts[w] = Math.Min(Math.Max((long)Math.Round(sums[w], MidpointRounding.ToEven), 1), _maxWordFrames);
            total += counts[w];
        }
        var frames = (int)Math.Min(total, _maxFrames);
        var fw = new long[frames];
        var fp = new float[frames];
        var frame = 0;
        for (var w = 0; w < words && frame < frames; w++)
            for (var k = 0; k < counts[w] && frame < frames; k++, frame++)
            {
                fw[frame] = w;
                fp[frame] = (float)(k / (double)counts[w]);
            }

        var noise = new float[_steps * frames * _latentDim];
        FillGaussian(noise, seed);
        var letters = new bool[length];
        for (var fi = 0; fi < letters.Length; fi++) letters[fi] = true;
        var fmask = new bool[frames];
        for (var fi = 0; fi < fmask.Length; fi++) fmask[fi] = true;
        float[] latents;
        using (var result = _sound.Run(new[]
        {
            NamedOnnxValue.CreateFromTensor("h", new DenseTensor<float>(h, [1, length, h.Length / length])),
            NamedOnnxValue.CreateFromTensor("dur", new DenseTensor<float>(dur, [1, length])),
            NamedOnnxValue.CreateFromTensor("mask", new DenseTensor<bool>(letters, [1, length])),
            NamedOnnxValue.CreateFromTensor("cw", new DenseTensor<long>(cw, [1, length])),
            NamedOnnxValue.CreateFromTensor("wstart", new DenseTensor<long>(wstart, [1, length])),
            NamedOnnxValue.CreateFromTensor("fw", new DenseTensor<long>(fw, [1, frames])),
            NamedOnnxValue.CreateFromTensor("fp", new DenseTensor<float>(fp, [1, frames])),
            NamedOnnxValue.CreateFromTensor("fmask", new DenseTensor<bool>(fmask, [1, frames])),
            NamedOnnxValue.CreateFromTensor("noise", new DenseTensor<float>(noise, [1, _steps, frames, _latentDim])),
        }))
        {
            latents = result.First().AsTensor<float>().ToArray();
        }

        // the decoder takes [1, latent_dim, T]; the whole piece at once (its receptive field is four frames)
        var z = new float[_latentDim * frames];
        for (var t = 0; t < frames; t++)
            for (var c = 0; c < _latentDim; c++)
                z[c * frames + t] = latents[t * _latentDim + c];
        float[] audio;
        using (var result = _decoder.Run(new[] { NamedOnnxValue.CreateFromTensor("z", new DenseTensor<float>(z, [1, _latentDim, frames])) }))
        {
            audio = result.First().AsTensor<float>().ToArray();
        }
        return new EmaSpeech(audio, frames, WordTimes(spoken, fw, words));
    }

    private IReadOnlyList<EmaWord> WordTimes(string spoken, long[] fw, int words)
    {
        var first = new int[words];
        var last = new int[words];
        for (var i = 0; i < words; i++)
            first[i] = -1;
        for (var frame = 0; frame < fw.Length; frame++)
        {
            var w = (int)fw[frame];
            if (first[w] < 0)
                first[w] = frame;
            last[w] = frame;
        }
        var texts = spoken.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var times = new List<EmaWord>(words);
        for (var w = 0; w < words && w < texts.Length; w++)
            if (first[w] >= 0)
                times.Add(new EmaWord(texts[w], Math.Round(first[w] / (double)LatentRate, 3), Math.Round((last[w] + 1) / (double)LatentRate, 3)));
        return times;
    }

    // Standard normals from a seed (Box-Muller): the same seed reads the same way again.
    private static void FillGaussian(float[] values, long seed)
    {
        var random = new Random(unchecked((int)(seed ^ (seed >> 32))));
        for (var i = 0; i < values.Length; i += 2)
        {
            var u1 = 1.0 - random.NextDouble();
            var u2 = random.NextDouble();
            var magnitude = Math.Sqrt(-2.0 * Math.Log(u1));
            values[i] = (float)(magnitude * Math.Cos(2 * Math.PI * u2));
            if (i + 1 < values.Length)
                values[i + 1] = (float)(magnitude * Math.Sin(2 * Math.PI * u2));
        }
    }

    public void Dispose()
    {
        _decoder.Dispose();
        _sound.Dispose();
        _text.Dispose();
    }
}
