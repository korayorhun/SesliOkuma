using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace SesliOkuma.Engine
{
    // The speech layer between the text on screen and the voice. What is spoken may differ from what is
    // shown (pronunciation dictionary, English words inside Turkish text, symbols such as ° and €, list
    // bullets, ALL-CAPS words), while the highlight still lands on the original word: the map converts
    // offsets both ways. Built once per reading.
    public sealed class SpeechMap
    {
        public string Spoken = "";
        public int Replacements;
        public string Examples = "";
        internal int[] SpokenToFull = new int[0];
        internal int[] FullToSpoken = new int[0];
        internal readonly Dictionary<int, int> ReplacedTokens = new Dictionary<int, int>();   // full token start -> full token length

        public int ToSpoken(int fullOffset)
        {
            if (FullToSpoken.Length == 0) return 0;
            return FullToSpoken[Math.Max(0, Math.Min(fullOffset, FullToSpoken.Length - 1))];
        }

        // Where a spoken word lands on screen. A token the layer rewrote is highlighted as a whole,
        // because the voice's word boundaries no longer correspond to the shown characters.
        public void Locate(int spokenOffset, int spokenLength, out int fullStart, out int fullLength)
        {
            if (SpokenToFull.Length == 0) { fullStart = 0; fullLength = 0; return; }
            fullStart = SpokenToFull[Math.Max(0, Math.Min(spokenOffset, SpokenToFull.Length - 1))];
            int tokenLength;
            fullLength = ReplacedTokens.TryGetValue(fullStart, out tokenLength) ? tokenLength : Math.Max(0, spokenLength);
        }
    }

    public static class SpeechText
    {
        // Characters that only mark a list item; a token made of these is dropped and ends the previous line.
        const string BulletChars = "•◦▪▫‣●○■□►▶▸➢➤✓✔✗✘★☆♦◆◇·→⇒»-–—*";
        const string EdgePunctuation = ".,;:!?\"'()[]{}«»“”‘’…-–—";
        static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

        public static SpeechMap Build(string full, string lang2)
        {
            var map = new SpeechMap();
            full = full ?? "";
            var sb = new StringBuilder(full.Length + 32);
            var s2f = new List<int>(full.Length + 32);
            var f2s = new int[full.Length + 1];
            var examples = new List<string>();
            var user = UserDictionary.Current;
            bool turkish = lang2 == "tr";
            int i = 0;
            while (i < full.Length)
            {
                if (char.IsWhiteSpace(full[i])) { f2s[i] = sb.Length; s2f.Add(i); sb.Append(' '); i++; continue; }
                int start = i;
                while (i < full.Length && !char.IsWhiteSpace(full[i])) i++;
                string token = full.Substring(start, i - start);
                bool breakBefore;
                string spoken = Transform(token, turkish, lang2, user, out breakBefore);
                if (breakBefore && EndsWithWordChar(sb)) { s2f.Add(start); sb.Append('.'); }
                if (spoken == token)
                {
                    for (int k = 0; k < token.Length; k++) { f2s[start + k] = sb.Length + k; s2f.Add(start + k); }
                    sb.Append(token);
                    continue;
                }
                for (int k = 0; k < token.Length; k++) f2s[start + k] = sb.Length;
                map.ReplacedTokens[start] = token.Length;
                map.Replacements++;
                if (examples.Count < 3) examples.Add(token + "→" + (spoken.Length > 0 ? spoken : "(silindi)"));
                if (spoken.Length == 0) continue;
                for (int k = 0; k < spoken.Length; k++) s2f.Add(start);
                sb.Append(spoken);
            }
            f2s[full.Length] = sb.Length;
            s2f.Add(full.Length);
            map.Spoken = sb.ToString();
            map.SpokenToFull = s2f.ToArray();
            map.FullToSpoken = f2s;
            map.Examples = string.Join(", ", examples.ToArray());
            return map;
        }

        static bool EndsWithWordChar(StringBuilder sb)
        {
            for (int k = sb.Length - 1; k >= 0; k--)
            {
                char c = sb[k];
                if (char.IsWhiteSpace(c)) continue;
                return char.IsLetterOrDigit(c);
            }
            return false;
        }

        static string Transform(string token, bool turkish, string lang2, Dictionary<string, string> user, out bool breakBefore)
        {
            breakBefore = false;
            if (IsBulletToken(token)) { breakBefore = true; return ""; }
            string t = StripPictographs(token);
            if (t.Length == 0) return "";
            int p = 0; while (p < t.Length && EdgePunctuation.IndexOf(t[p]) >= 0) p++;
            int q = t.Length; while (q > p && EdgePunctuation.IndexOf(t[q - 1]) >= 0) q--;
            if (q <= p) return t;                                       // punctuation only: the voice pauses on it
            string prefix = t.Substring(0, p), core = t.Substring(p, q - p), suffix = t.Substring(q);
            if (turkish)
            {
                string symbols = TurkishSymbols(core);
                if (symbols != null) return prefix + symbols + suffix;
            }
            core = Decapitalize(core, turkish);
            core = ApplyDictionary(core, turkish, user);
            return prefix + core + suffix;
        }

        static bool IsBulletToken(string token)
        {
            foreach (char c in token) if (BulletChars.IndexOf(c) < 0) return false;
            return token.Length > 0;
        }

        // Emoji and decorative marks are not read; trademark/copyright signs are noise for a listener.
        static string StripPictographs(string token)
        {
            StringBuilder sb = null;
            for (int k = 0; k < token.Length; k++)
            {
                int cp = char.IsHighSurrogate(token[k]) && k + 1 < token.Length ? char.ConvertToUtf32(token[k], token[k + 1]) : token[k];
                bool drop = (cp >= 0x1F000 && cp <= 0x1FAFF) || (cp >= 0x2600 && cp <= 0x27BF && BulletChars.IndexOf((char)cp) < 0)
                    || (cp >= 0x2B00 && cp <= 0x2BFF) || (cp >= 0xFE00 && cp <= 0xFE0F) || cp == 0x200D || cp == 0x20E3
                    || cp == 0x2122 || cp == 0x00AE || cp == 0x00A9;
                if (drop && sb == null) sb = new StringBuilder(token.Substring(0, k));
                else if (!drop && sb != null) { sb.Append(token[k]); if (cp > 0xFFFF) sb.Append(token[k + 1]); }
                if (cp > 0xFFFF) k++;
            }
            return sb == null ? token : sb.ToString();
        }

        static readonly Regex DegreeRx = new Regex(@"^(\d+(?:[.,]\d+)?)\s?[°⁰º]([CcFf])?$", RegexOptions.Compiled);
        static readonly Regex AreaRx = new Regex(@"^(\d+(?:[.,]\d+)?)?(km|cm|mm|dm|m)([²³])$", RegexOptions.Compiled);
        static readonly Regex MoneyRx = new Regex(@"^(?:([€$£₺¥])(\d[\d.,]*)|(\d[\d.,]*)([€$£₺¥]))$", RegexOptions.Compiled);
        static readonly Regex ApproxRx = new Regex(@"^~(\d.*)$", RegexOptions.Compiled);

        // Symbols a Turkish voice would otherwise name ("derece işareti") or skip; null when nothing applies.
        static string TurkishSymbols(string core)
        {
            var m = DegreeRx.Match(core);
            if (m.Success) return m.Groups[1].Value + DegreeWord(m.Groups[2].Value);
            if (core == "°" || core == "⁰" || core == "º") return "derece";
            if (core == "°C" || core == "°c") return "santigrat derece";
            if (core == "°F" || core == "°f") return "fahrenhayt derece";
            m = AreaRx.Match(core);
            if (m.Success)
            {
                string unit = m.Groups[2].Value == "km" ? "kilometre" : m.Groups[2].Value == "cm" ? "santimetre" : m.Groups[2].Value == "mm" ? "milimetre" : m.Groups[2].Value == "dm" ? "desimetre" : "metre";
                return (m.Groups[1].Success ? m.Groups[1].Value + " " : "") + unit + (m.Groups[3].Value == "²" ? "kare" : "küp");
            }
            m = MoneyRx.Match(core);
            if (m.Success)
            {
                string symbol = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[4].Value;
                string amount = m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value;
                return amount + " " + CurrencyWord(symbol);
            }
            if (core.Length == 1 && "€$£₺¥".IndexOf(core[0]) >= 0) return CurrencyWord(core);
            m = ApproxRx.Match(core);
            if (m.Success) return "yaklaşık " + m.Groups[1].Value;
            if (core == "&") return "ve";
            if (core.Length > 1 && (core.EndsWith("²") || core.EndsWith("³")))
                return core.Substring(0, core.Length - 1) + (core.EndsWith("²") ? " kare" : " küp");
            return null;
        }

        static string DegreeWord(string scale)
        {
            if (scale == "C" || scale == "c") return " santigrat derece";
            if (scale == "F" || scale == "f") return " fahrenhayt derece";
            return " derece";
        }

        static string CurrencyWord(string symbol)
        {
            switch (symbol)
            {
                case "€": return "avro";
                case "$": return "dolar";
                case "£": return "sterlin";
                case "₺": return "lira";
                case "¥": return "yen";
            }
            return symbol;
        }

        static readonly Regex RomanRx = new Regex(@"^[IVXLCDM]+$", RegexOptions.Compiled);

        // "ÖNEMLİ" is a word, "TBMM" is an abbreviation: voices spell out capitals, so words of four or more
        // letters with a vowel become Title case; short or vowel-less tokens stay spelled.
        static string Decapitalize(string core, bool turkish)
        {
            int ap = core.IndexOfAny(new[] { '\'', '’' });
            string stem = ap > 0 ? core.Substring(0, ap) : core;
            string tail = ap > 0 ? core.Substring(ap) : "";
            if (stem.Length < 4 || RomanRx.IsMatch(stem)) return core;
            bool hasVowel = false;
            foreach (char c in stem)
            {
                if (!char.IsLetter(c) || !char.IsUpper(c)) return core;
                if ("AEIİOÖUÜÂÎÛY".IndexOf(c) >= 0) hasVowel = true;
            }
            if (!hasVowel) return core;
            var culture = turkish ? Turkish : CultureInfo.InvariantCulture;
            return stem.Substring(0, 1) + stem.Substring(1).ToLower(culture) + tail;
        }

        static string ApplyDictionary(string core, bool turkish, Dictionary<string, string> user)
        {
            int ap = core.IndexOfAny(new[] { '\'', '’' });
            string stem = ap > 0 ? core.Substring(0, ap) : core;
            string tail = ap > 0 ? core.Substring(ap) : "";
            if (stem.Length == 0) return core;
            string key = stem.ToLower(Turkish);
            string value;
            if (user != null && user.TryGetValue(key, out value)) return value + tail;
            if (!turkish) return core;
            if (BuiltIn.TryGetValue(key, out value)) return value + tail;
            if (EnglishDictionary.TryGet(key, out value)) return value + tail;
            if (EnglishRespelling.LooksEnglish(key)) return EnglishRespelling.Respell(key) + tail;
            return core;
        }

        // Loads the big English dictionary off the UI thread so the first Turkish reading does not pay for it.
        public static void WarmUp()
        {
            System.Threading.ThreadPool.QueueUserWorkItem(delegate { string v; EnglishDictionary.TryGet("warmup", out v); });
        }

        // Words Turkish voices read wrong because Turkish letters sound differently (c, w, j, silent e…).
        // Keys are lower case; values are how a Turkish speaker says them.
        static readonly Dictionary<string, string> BuiltIn = Parse(@"
netcad=netkad|autocad=otokad|cad=kad|microstation=maykrosteyşın|arcgis=arkcis|qgis=kücis|geoserver=ceosörvır|postgis=postcis|postgresql=postgreeskuel|sql=sikuıl|json=ceyson|
microsoft=maykrosoft|windows=vindovs|office=ofis|word=vörd|excel=eksel|powerpoint=pavırpoynt|outlook=autluk|onedrive=vandrayv|sharepoint=şerpoynt|teams=tiyms|edge=ec|chrome=krom|firefox=fayrfoks|safari=safari|
google=gugıl|gmail=cimeyl|youtube=yutub|whatsapp=votsap|instagram=instagram|facebook=feysbuk|twitter=tivitır|linkedin=linktin|tiktok=tiktok|telegram=telegram|discord=diskord|skype=skayp|zoom=zum|
github=githab|gitlab=gitlab|jira=cira|confluence=konfluıns|slack=slek|trello=trello|notion=noşın|figma=figma|canva=kanva|
visual=vijuıl|studio=stüdyo|code=kod|copilot=kopaylot|chatgpt=çet ci pi ti|claude=klod|openai=open ey ay|gemini=ceminay|python=paytın|java=cava|javascript=cavaskript|typescript=taypskript|linux=linuks|ubuntu=ubuntu|docker=dokır|
apple=epıl|iphone=ayfon|ipad=aypad|macbook=mekbuk|airpods=eyrpods|samsung=samsung|xiaomi=şaomi|huawei=huavey|playstation=pleysteyşın|xbox=eksboks|nintendo=nintendo|steam=stiym|netflix=netfliks|spotify=spotifay|amazon=amazon|
bluetooth=blutut|wifi=vayfay|wi-fi=vayfay|email=imeyl|e-mail=imeyl|mail=meyl|online=onlayn|offline=oflayn|update=apdeyt|upgrade=apgreyd|download=davnlod|upload=aplod|
file=fayl|folder=foldır|server=sörvır|client=klayınt|browser=bravzır|software=softver|hardware=hardver|login=login|logout=logaut|password=pasvörd|backup=bekap|cloud=klaud|
driver=drayvır|setup=setap|install=instol|default=difolt|release=riliz|feature=fiçır|debug=dibag|deploy=diploy|framework=freymvörk|database=deytabeys|script=skript|plugin=plagin|token=tokın|cache=keş|cookie=kuki|
mouse=maus|keyboard=kiybord|touchpad=taçped|desktop=desktop|laptop=leptop|notebook=notbuk|printer=printır|scanner=skenır|screenshot=skrinşat|firewall=fayırvol|network=netvörk|router=rautır|wireless=vayırles|pixel=piksel|
meeting=miting|deadline=dedlayn|feedback=fidbek|startup=startap|freelance=frilens|business=biznıs|marketing=marketing|influencer=influensır|follower=folovır|hashtag=heştag|selfie=selfi|timeline=taymlayn|weekend=vikend|
happy=hepi|birthday=börtdey|party=parti|game=geym|gamer=geymır|player=pleyır|team=tiym|coach=koç|super=süpır|style=stayl|vintage=vinteyc|outdoor=autdor|indoor=indor|drive=drayv|
share=şeyr|save=seyv|enter=entır|escape=eskeyp|shift=şift|delete=dilit|insert=insört|home=hom|page=peyc|tab=teb|check=çek|cool=kul|show=şov|live=layv|trend=trend|
smartphone=smartfon|touchscreen=taçskrin|streaming=striming|stream=strim|podcast=podkest|playlist=pleylist|fast-food=fastfud|milkshake=milkşeyk|cheesecake=çizkeyk|brunch=branç|shopping=şoping|outlet=autlet");

        static Dictionary<string, string> Parse(string table)
        {
            var dict = new Dictionary<string, string>();
            foreach (string pair in table.Split(new[] { '|', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                if (eq > 0) dict[pair.Substring(0, eq).Trim()] = pair.Substring(eq + 1).Trim();
            }
            return dict;
        }
    }

    // Rule-based reading of English words that are not in the dictionary: a Turkish letter respelling,
    // applied only when the spelling clearly is not Turkish. Approximate by nature; the dictionary wins.
    static class EnglishRespelling
    {
        // Turkish words and loanwords whose spelling trips the English detector.
        static readonly HashSet<string> Blocklist = new HashSet<string>(@"ithal ithalat ithaf methal mushaf fethiye fethetmek koordinat koordine koordinasyon kooperatif zooloji mikroorganizma
reaksiyon realite ideal teorem ring sting king thai".Split(new[] { ' ', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));

        static readonly string[] Strong = { "th", "sh", "ch", "ph", "ck", "ee", "oo", "igh", "tion", "ing" };
        static readonly string[] Weak = { "ea", "ou", "ai", "ay", "gh" };
        static readonly string[][] Rules =
        {
            new[] { "tion", "şın" }, new[] { "ssion", "şın" }, new[] { "sion", "jın" }, new[] { "ought", "ot" }, new[] { "aught", "ot" },
            new[] { "igh", "ay" }, new[] { "eigh", "ey" }, new[] { "ght", "t" }, new[] { "kn", "n" }, new[] { "wr", "r" }, new[] { "wh", "v" },
            new[] { "sch", "sk" }, new[] { "tch", "ç" }, new[] { "ch", "ç" }, new[] { "sh", "ş" }, new[] { "th", "t" }, new[] { "ph", "f" },
            new[] { "ck", "k" }, new[] { "qu", "kv" }, new[] { "ee", "i" }, new[] { "oo", "u" }, new[] { "ea", "i" }, new[] { "ai", "ey" },
            new[] { "ay", "ey" }, new[] { "oa", "o" }, new[] { "ou", "au" }, new[] { "ow", "av" }, new[] { "ew", "yu" }, new[] { "au", "o" }, new[] { "aw", "o" },
            new[] { "x", "ks" }, new[] { "w", "v" }, new[] { "q", "k" },
        };

        public static bool LooksEnglish(string word)
        {
            if (word.Length < 4 || Blocklist.Contains(word)) return false;
            foreach (char c in word) if (c < 'a' || c > 'z') return false;
            int score = 0;
            if (word.IndexOfAny(new[] { 'q', 'w', 'x' }) >= 0) score += 2;
            foreach (string s in Strong) if (word.Contains(s)) { score += 2; break; }
            foreach (string s in Weak) if (word.Contains(s)) { score += 1; break; }
            return score >= 2;
        }

        public static string Respell(string word)
        {
            // A silent final e after a consonant ("update", "make") would otherwise be read as a vowel.
            if (word.Length > 3 && word[word.Length - 1] == 'e' && !IsVowel(word[word.Length - 2])) word = word.Substring(0, word.Length - 1);
            var sb = new StringBuilder(word.Length + 4);
            int i = 0;
            while (i < word.Length)
            {
                bool matched = false;
                foreach (string[] rule in Rules)
                {
                    if (i + rule[0].Length <= word.Length && string.CompareOrdinal(word, i, rule[0], 0, rule[0].Length) == 0)
                    {
                        sb.Append(rule[1]); i += rule[0].Length; matched = true; break;
                    }
                }
                if (matched) continue;
                char c = word[i];
                if (c == 'c') sb.Append(i + 1 < word.Length && "eiy".IndexOf(word[i + 1]) >= 0 ? 's' : 'k');
                else if (c == 'y' && (i + 1 >= word.Length || !IsVowel(word[i + 1]))) sb.Append('i');
                else sb.Append(c);
                i++;
            }
            return sb.ToString();
        }

        static bool IsVowel(char c) { return "aeiouy".IndexOf(c) >= 0; }
    }

    // ~100k English words with the Turkish respelling of their CMU Pronouncing Dictionary reading, built by
    // tools/english-respelling/make.py; Turkish words (zemberek-nlp lexicon) are left out there, so a Turkish
    // word that happens to exist in English ("adam", "para") is never touched. Embedded gzip, loaded once.
    static class EnglishDictionary
    {
        const string Resource = "english-respelling.txt.gz";
        static readonly object Gate = new object();
        static string[] _keys, _values;

        public static bool TryGet(string key, out string value)
        {
            value = null;
            if (_keys == null) Load();
            if (_keys.Length == 0) return false;
            int i = Array.BinarySearch(_keys, key, StringComparer.Ordinal);
            if (i < 0) return false;
            value = _values[i];
            return true;
        }

        static void Load()
        {
            lock (Gate)
            {
                if (_keys != null) return;
                var keys = new List<string>(100000);
                var values = new List<string>(100000);
                try
                {
                    using (var stream = typeof(EnglishDictionary).Assembly.GetManifestResourceStream(Resource))
                    {
                        if (stream != null)
                            using (var gz = new System.IO.Compression.GZipStream(stream, System.IO.Compression.CompressionMode.Decompress))
                            using (var reader = new StreamReader(gz, Encoding.UTF8))
                            {
                                string line;
                                while ((line = reader.ReadLine()) != null)
                                {
                                    int tab = line.IndexOf('\t');
                                    if (tab <= 0) continue;
                                    keys.Add(line.Substring(0, tab));
                                    values.Add(line.Substring(tab + 1));
                                }
                            }
                    }
                }
                catch (Exception ex) { EngineLog.Log("english dictionary: " + ex.Message); }
                _values = values.ToArray();
                _keys = keys.ToArray();
                if (_keys.Length > 0) EngineLog.Log("english dictionary: " + _keys.Length + " words");
            }
        }
    }

    // %LOCALAPPDATA%\SesliOkuma\okunus.txt: the user's own "spelling = pronunciation" lines, for every voice.
    public static class UserDictionary
    {
        static Dictionary<string, string> _entries = new Dictionary<string, string>();
        static DateTime _stamp = DateTime.MinValue;
        static bool _created;

        public static string Path { get { return System.IO.Path.Combine(EnginePaths.DataDir, "okunus.txt"); } }

        public static Dictionary<string, string> Current
        {
            get
            {
                try
                {
                    EnsureFile();
                    DateTime stamp = File.GetLastWriteTimeUtc(Path);
                    if (stamp != _stamp) { _entries = Load(); _stamp = stamp; }
                }
                catch (Exception ex) { EngineLog.Log("okunus.txt: " + ex.Message); }
                return _entries;
            }
        }

        static Dictionary<string, string> Load()
        {
            var dict = new Dictionary<string, string>();
            foreach (string raw in File.ReadAllLines(Path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim().ToLower(CultureInfo.GetCultureInfo("tr-TR"));
                string value = line.Substring(eq + 1).Trim();
                if (key.Length > 0 && key.IndexOf(' ') < 0 && value.Length > 0) dict[key] = value;
            }
            if (dict.Count > 0) EngineLog.Log("okunus.txt: " + dict.Count + " entries");
            return dict;
        }

        static void EnsureFile()
        {
            if (_created || File.Exists(Path)) { _created = true; return; }
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
            File.WriteAllText(Path,
                "# Sesli Okuma — okunuş sözlüğü / pronunciation dictionary\r\n" +
                "# Her satır:  yazılış = okunuş   (büyük/küçük harf önemsiz; # ile başlayan satırlar açıklamadır)\r\n" +
                "# Each line:  spelling = pronunciation   (case-insensitive; lines starting with # are comments)\r\n" +
                "# Kaydettikten sonra bir sonraki okumada geçerli olur. / Takes effect on the next reading.\r\n" +
                "#\r\n" +
                "# Girdiler her ses için geçerlidir. / Entries apply to every voice.\r\n" +
                "#\r\n" +
                "# Örnek / example (başındaki # kaldırılınca etkinleşir / remove the leading # to enable):\r\n" +
                "# Netcad = Netkad\r\n", new UTF8Encoding(true));
            _created = true;
        }

        public static void OpenInEditor()
        {
            try
            {
                EnsureFile();
                System.Diagnostics.Process.Start("notepad.exe", "\"" + Path + "\"");
            }
            catch (Exception ex) { EngineLog.Log("okunus.txt open: " + ex.Message); }
        }
    }
}
