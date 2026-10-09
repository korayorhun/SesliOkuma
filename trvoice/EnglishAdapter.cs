using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SesliOkuma.TrVoice;

// EMA Lightning reads Turkish letters only; an English word left as-is comes out letter-by-letter Turkish.
// This maps English-looking tokens to an approximate Turkish phonetic respelling before the frontend:
// a curated dictionary first, then light letter rules. One token in, one token out (never introduces spaces).
internal static class EnglishAdapter
{
    private const string TurkishLetters = "çğışöüÇĞİŞÖÜ";

    private static readonly Dictionary<string, string> Words = new()
    {
        // tech / computing
        ["file"] = "fayl", ["server"] = "sörvır", ["update"] = "apdeyt", ["upgrade"] = "apgreyd",
        ["download"] = "davnlod", ["upload"] = "aplod", ["browser"] = "bravzır", ["click"] = "klik",
        ["mouse"] = "maus", ["software"] = "softver", ["hardware"] = "hardver", ["online"] = "onlayn",
        ["offline"] = "oflayn", ["email"] = "imeyl", ["e-mail"] = "imeyl", ["mail"] = "meyl",
        ["web"] = "veb", ["website"] = "vebsayt", ["site"] = "sayt", ["link"] = "link",
        ["login"] = "login", ["password"] = "pasvörd", ["backup"] = "bekap", ["cloud"] = "klaud",
        ["driver"] = "drayvır", ["setup"] = "setap", ["install"] = "instol", ["desktop"] = "desktop",
        ["laptop"] = "leptop", ["tablet"] = "tablet", ["wifi"] = "vayfay", ["wi-fi"] = "vayfay",
        ["bluetooth"] = "blututh", ["firewall"] = "fayırvol", ["network"] = "netvörk", ["notebook"] = "notbuk",
        ["keyboard"] = "kiybord", ["touchpad"] = "taçped", ["folder"] = "foldır", ["byte"] = "bayt",
        ["cache"] = "keş", ["cookie"] = "kuki", ["default"] = "difolt", ["developer"] = "divelopır",
        ["release"] = "riliz", ["feature"] = "fiçır", ["framework"] = "freymvörk", ["debug"] = "dibag",
        ["deploy"] = "diploy", ["frontend"] = "frontend", ["backend"] = "bekend", ["database"] = "deytabeys",
        ["script"] = "skript", ["plugin"] = "plagin", ["token"] = "tokın", ["streaming"] = "striming",
        ["stream"] = "strim", ["podcast"] = "podkest", ["playlist"] = "pleylist", ["smartphone"] = "smartfon",
        ["bug"] = "bag", ["crash"] = "kreş", ["freelance"] = "frilens", ["startup"] = "startap",
        // brands / products
        ["windows"] = "vindovs", ["word"] = "vörd", ["excel"] = "eksel", ["powerpoint"] = "pavırpoint",
        ["outlook"] = "autluk", ["google"] = "gugıl", ["chrome"] = "krom", ["youtube"] = "yutyub",
        ["whatsapp"] = "votsap", ["twitter"] = "tivitır", ["facebook"] = "feysbuk", ["instagram"] = "instagram",
        ["linkedin"] = "linktin", ["netflix"] = "netfliks", ["spotify"] = "spotifay", ["iphone"] = "ayfon",
        ["ipad"] = "ayped", ["android"] = "android", ["microsoft"] = "maykrosoft", ["apple"] = "epıl",
        ["amazon"] = "amazon", ["github"] = "githab", ["gmail"] = "cimeyl", ["teams"] = "tiyms",
        ["zoom"] = "zum", ["skype"] = "skayp", ["telegram"] = "telegram", ["discord"] = "diskord",
        ["steam"] = "stiym", ["playstation"] = "pleysteyşın", ["xbox"] = "eksboks", ["visual"] = "vijuıl",
        ["studio"] = "stüdyo", ["code"] = "kod", ["copilot"] = "kopaylot", ["linux"] = "linuks",
        // everyday loanwords
        ["ok"] = "okey", ["okay"] = "okey", ["cool"] = "kul", ["check"] = "çek", ["check-in"] = "çekin",
        ["meeting"] = "miting", ["deadline"] = "dedlayn", ["feedback"] = "fidbek", ["brunch"] = "branç",
        ["fast-food"] = "fastfud", ["fastfood"] = "fastfud", ["milkshake"] = "milkşeyk", ["cheesecake"] = "çizkeyk",
        ["show"] = "şov", ["talk"] = "tok", ["reality"] = "riyaliti", ["fitness"] = "fitnes",
        ["jogging"] = "coging", ["shopping"] = "şoping", ["outlet"] = "autlet", ["business"] = "biznıs",
        ["marketing"] = "marketing", ["branding"] = "brending", ["influencer"] = "influensır",
        ["follower"] = "folovır", ["story"] = "stori", ["live"] = "layv", ["trend"] = "trend",
        ["hashtag"] = "heştag", ["selfie"] = "selfi", ["timeline"] = "taymlayn", ["weekend"] = "vikend",
        ["happy"] = "hepi", ["birthday"] = "börtdey", ["party"] = "parti", ["game"] = "geym",
        ["gamer"] = "geymır", ["player"] = "pleyır", ["team"] = "tiym", ["coach"] = "koç",
        ["goal"] = "gol", ["super"] = "süpır", ["star"] = "star", ["single"] = "singıl",
        ["album"] = "albüm", ["remix"] = "rimiks", ["cover"] = "kavır", ["sound"] = "saund",
        ["bass"] = "beys", ["beat"] = "bit", ["style"] = "stayl", ["vintage"] = "vinteyc",
        ["outdoor"] = "autdor", ["indoor"] = "indor", ["offroad"] = "ofrod", ["drive"] = "drayv",
        ["test"] = "test", ["project"] = "procekt", ["print"] = "print", ["printer"] = "printır",
        ["scanner"] = "skenır", ["screenshot"] = "skrinşat", ["share"] = "şeyr", ["save"] = "seyv",
        ["enter"] = "entır", ["escape"] = "eskeyp", ["shift"] = "şift", ["control"] = "kontrol",
        ["space"] = "speys", ["delete"] = "dilit", ["insert"] = "insört", ["home"] = "hom",
        ["end"] = "end", ["page"] = "peyc", ["tab"] = "teb",
    };

    // Digraphs and clusters first, then single letters; applied only to tokens that scored as English.
    private static readonly (string From, string To)[] Rules =
    {
        ("tion", "şın"), ("sion", "jın"), ("ssion", "şın"), ("ought", "ot"), ("aught", "ot"),
        ("igh", "ay"), ("eigh", "ey"), ("ght", "t"), ("kn", "n"), ("wr", "r"), ("wh", "v"),
        ("sch", "sk"), ("tch", "ç"), ("ch", "ç"), ("sh", "ş"), ("th", "t"), ("ph", "f"),
        ("ck", "k"), ("qu", "kv"), ("ee", "i"), ("oo", "u"), ("ea", "i"), ("ai", "ey"),
        ("ay", "ey"), ("ey", "ey"), ("oa", "o"), ("ou", "au"), ("ow", "av"), ("oy", "oy"),
        ("ew", "yu"), ("au", "o"), ("aw", "o"), ("x", "ks"), ("w", "v"), ("q", "k"),
    };

    public static string Adapt(string token)
    {
        if (token.Length < 2 || token.Any(ch => TurkishLetters.IndexOf(ch) >= 0))
            return token;
        var core = token.Trim('.', ',', ';', ':', '!', '?', '"', '\'', '(', ')', '-');
        if (core.Length < 2 || !core.All(ch => ch is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or '-'))
            return token;
        var lower = core.ToLowerInvariant();
        string? replacement = null;
        if (Words.TryGetValue(lower, out var known))
            replacement = known;
        else if (Score(lower) >= 2)
            replacement = Respell(lower);
        if (replacement == null || replacement == lower)
            return token;
        var start = token.IndexOf(core, System.StringComparison.Ordinal);
        return token.Substring(0, start) + replacement + token.Substring(start + core.Length);
    }

    private static int Score(string word)
    {
        var score = 0;
        if (word.IndexOfAny(new[] { 'q', 'w', 'x' }) >= 0) score += 2;
        foreach (var strong in new[] { "th", "sh", "ch", "ph", "ck", "ee", "oo", "igh", "tion", "ing" })
            if (word.Contains(strong)) { score += 2; break; }
        foreach (var weak in new[] { "ea", "ou", "ai", "ay", "gh" })
            if (word.Contains(weak)) { score += 1; break; }
        return score;
    }

    private static string Respell(string word)
    {
        // silent final e after a consonant ("update", "make"): drop it so it is not read as a vowel
        if (word.Length > 3 && word[word.Length - 1] == 'e' && !IsVowel(word[word.Length - 2]))
            word = word.Substring(0, word.Length - 1);
        var result = new StringBuilder(word.Length + 4);
        var i = 0;
        while (i < word.Length)
        {
            var matched = false;
            foreach (var (from, to) in Rules)
                if (i + from.Length <= word.Length && string.CompareOrdinal(word, i, from, 0, from.Length) == 0)
                {
                    result.Append(to);
                    i += from.Length;
                    matched = true;
                    break;
                }
            if (!matched)
            {
                var ch = word[i];
                if (ch == 'c')
                    result.Append(i + 1 < word.Length && word[i + 1] is 'e' or 'i' or 'y' ? 's' : 'k');
                else if (ch == 'y' && (i + 1 >= word.Length || !IsVowel(word[i + 1])))
                    result.Append('i');
                else
                    result.Append(ch);
                i++;
            }
        }
        return result.ToString();
    }

    private static bool IsVowel(char ch) => ch is 'a' or 'e' or 'i' or 'o' or 'u' or 'y';
}
