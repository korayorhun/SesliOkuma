namespace EmaLightning.Onnx;

// 16-bit PCM mono WAV files, written whole.
public static class WaveFile
{
    public static void Write(string path, ReadOnlySpan<float> audio, int rate, int trailingSilence = 0)
    {
        var frames = audio.Length + trailingSilence;
        var bytes = frames * 2;
        using var writer = new BinaryWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None));
        writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + bytes);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(rate);
        writer.Write(rate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        writer.Write(bytes);
        foreach (var sample in audio)
            writer.Write((short)Math.Round(Math.Min(Math.Max(sample, -1f), 1f) * 32767f));
        for (var i = 0; i < trailingSilence; i++)
            writer.Write((short)0);
    }
}
