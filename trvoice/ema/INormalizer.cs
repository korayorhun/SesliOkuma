namespace EmaLightning.Onnx;

// Written notation (numbers, dates, money, units, abbreviations) to the words a Turkish reader says; null when it could not run.
public interface INormalizer
{
    string? Normalize(string text);
}
