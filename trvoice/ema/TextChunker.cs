using System.Text.RegularExpressions;

namespace EmaLightning.Onnx;

// Greedy chunking (EMA Lightning's chunker.py): text that fits in about ten seconds of speech stays one piece; longer text is cut
// at the last good spot inside each window: a sentence end, then a clause mark, then a space, and only then exactly at the limit.
public static class TextChunker
{
    public const double LettersPerSecond = 18.0;
    public const double MaxSeconds = 10.0;
    public const int MaxLetters = 250;
    public const double SentencePause = 0.25;
    public const double ClausePause = 0.12;

    private static readonly (Regex Pattern, double Pause)[] Cuts =
    [
        (new Regex(@"[.!?]+[""')]*(?= )", RegexOptions.Compiled), SentencePause),
        (new Regex(@"[,;:](?= )", RegexOptions.Compiled), ClausePause),
        (new Regex(@"\S(?= )", RegexOptions.Compiled), ClausePause),
    ];
    private static readonly Regex Letter = new(@"\p{L}", RegexOptions.Compiled);

    // (piece, seconds of silence after it); every piece ends in terminal punctuation, the last one has no pause.
    public static List<(string Text, double Pause)> Chunk(string text, double speed)
    {
        var limit = (int)Math.Min(MaxLetters, LettersPerSecond * MaxSeconds * speed);
        var pieces = new List<(string, double)>();
        var rest = text.Trim();
        while (rest.Length > 0)
        {
            var cut = rest.Length;
            var pause = 0.0;
            if (rest.Length > limit)
            {
                cut = limit;
                // Like Python's finditer(rest, 0, limit + 1): the text ends at limit + 1 for the lookahead too.
                var window = rest.Substring(0, Math.Min(rest.Length, limit + 1));
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
            var piece = rest.Substring(0, cut).Trim();
            rest = rest.Substring(cut).Trim();
            if (Letter.IsMatch(piece))
                pieces.Add((Finish(piece), pause));
        }
        if (pieces.Count > 0)
            pieces[pieces.Count - 1] = (pieces[pieces.Count - 1].Item1, 0.0);
        return pieces;
    }

    // End every piece the way the model was trained: on a sentence end.
    private static string Finish(string piece)
    {
        var core = piece.TrimEnd('"', '\'', ')');
        if (core.Length > 0 && core[core.Length - 1] is '.' or '!' or '?')
            return piece;
        return piece.TrimEnd(',', ';', ':', '-', ' ') + ".";
    }
}
