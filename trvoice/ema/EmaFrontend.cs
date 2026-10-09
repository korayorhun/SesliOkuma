using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace EmaLightning.Onnx;

// Any written Turkish in, text in the model's own alphabet out: EMA Lightning's frontend.py step for step. Nothing here throws
// on text. Pre-process the text yourself before Spoken() for domain-specific readings (e.g. capitalised names that are words).
public sealed class EmaFrontend
{
    private const int BlockBytes = 8 * 1024;
    private const string Turkish = "çğıöşüÇĞİÖŞÜ";
    private static readonly Dictionary<char, string> Typography = new()
    {
        ['’'] = "'", ['‘'] = "'", ['ʼ'] = "'", ['´'] = "'", ['`'] = "'",
        ['“'] = "\"", ['”'] = "\"", ['„'] = "\"", ['«'] = "\"", ['»'] = "\"",
        ['–'] = "-", ['—'] = "-", ['−'] = "-", ['…'] = "...",
    };
    private static readonly Regex Unsafe = new("[\x00-\x08\x0b-\x1f\x7f-\x9f؜‎‏‪-‮⁦-⁩]", RegexOptions.Compiled);
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);

    private readonly HashSet<char> _vocab;
    private readonly INormalizer? _normalizer;

    public EmaFrontend(IEnumerable<string> vocab, INormalizer? normalizer)
    {
        _vocab = new HashSet<char>(vocab.Where(entry => entry.Length == 1).Select(entry => entry[0]));
        _normalizer = normalizer;
    }

    public string Spoken(string text)
    {
        text = Unsafe.Replace(text, " ");
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
        return Alphabet(string.Join(" ", Blocks(text).Select(block => _normalizer?.Normalize(block) ?? block)));
    }

    // Typographic marks to ASCII, Turkish lowercasing, accents off the letters that are not Turkish, anything the model cannot
    // read becomes a space.
    public string Alphabet(string text)
    {
        var ascii = new StringBuilder(text.Length);
        foreach (var ch in text)
            ascii.Append(Typography.TryGetValue(ch, out var plain) ? plain : ch.ToString());
        text = ascii.ToString().Replace('İ', 'i').Replace('I', 'ı').ToLowerInvariant();
        var output = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            var piece = char.IsSurrogate(ch) ? string.Empty : Turkish.IndexOf(ch) >= 0 ? ch.ToString() : StripMarks(ch);
            output.Append(piece.Length > 0 && piece.All(_vocab.Contains) ? piece : " ");
        }
        return Spaces.Replace(output.ToString(), " ").Trim();
    }

    private static string StripMarks(char ch)
    {
        var decomposed = ch.ToString().Normalize(NormalizationForm.FormKD);
        var kept = new StringBuilder(decomposed.Length);
        foreach (var part in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(part) is not (UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark))
                kept.Append(part);
        return kept.ToString();
    }

    // Split at whitespace into pieces the normalizer accepts in one call.
    public static IEnumerable<string> Blocks(string text)
    {
        var block = new List<string>();
        var size = 0;
        foreach (var word in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var bytes = Encoding.UTF8.GetByteCount(word) + 1;
            if (block.Count > 0 && size + bytes > BlockBytes)
            {
                yield return string.Join(" ", block);
                block.Clear();
                size = 0;
            }
            block.Add(word);
            size += bytes;
        }
        if (block.Count > 0)
            yield return string.Join(" ", block);
    }
}
