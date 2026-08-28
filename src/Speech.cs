using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace SesliOkuma
{
    public enum VoiceProvider { Sapi }

    public sealed class VoiceInfo
    {
        public VoiceProvider Provider = VoiceProvider.Sapi;
        public string Id;
        public string Description;
        public string Name;          // "Emel", "AndrewMultilingual", "Tolga"
        public string Lang2 = "";    // "tr", "en"…
        public string LanguageName;  // native display name of the voice's culture
        public object Token;

        public bool IsNatural { get { return Description.IndexOf("Natural", StringComparison.OrdinalIgnoreCase) >= 0; } }
        public bool IsMultilingual { get { return Name.IndexOf("Multilingual", StringComparison.OrdinalIgnoreCase) >= 0; } }
        public string ShortName { get { return IsMultilingual ? Name.Replace("Multilingual", "") : Name; } }
    }

    // Thin reflection wrapper over the SAPI.SpVoice COM object (no interop assembly needed).
    public sealed class SpeechEngine
    {
        readonly Type _type;
        readonly object _voice;
        readonly List<VoiceInfo> _voices = new List<VoiceInfo>();

        public IList<VoiceInfo> Voices { get { return _voices; } }
        public bool IsAvailable { get { return _voice != null; } }

        public SpeechEngine()
        {
            try
            {
                _type = Type.GetTypeFromProgID("SAPI.SpVoice");
                _voice = Activator.CreateInstance(_type);
            }
            catch (Exception ex) { Logger.Log("SAPI init failed: " + ex.Message); }
        }

        static object Get(object o, string prop) { return o.GetType().InvokeMember(prop, BindingFlags.GetProperty, null, o, null); }
        static void Set(object o, string prop, object value) { o.GetType().InvokeMember(prop, BindingFlags.SetProperty, null, o, new object[] { value }); }
        static object Call(object o, string method, params object[] args) { return o.GetType().InvokeMember(method, BindingFlags.InvokeMethod, null, o, args); }

        public void RefreshVoices()
        {
            _voices.Clear();
            if (_voice == null) return;
            try
            {
                object tokens = Call(_voice, "GetVoices", "", "");
                int count = (int)Get(tokens, "Count");
                for (int i = 0; i < count; i++)
                {
                    object tok = Call(tokens, "Item", i);
                    var v = new VoiceInfo();
                    v.Token = tok;
                    v.Id = (string)Get(tok, "Id");
                    v.Description = (string)Call(tok, "GetDescription", 0);
                    string langHex = "";
                    try { langHex = Convert.ToString(Call(tok, "GetAttribute", "Language")); } catch { }
                    Parse(v, langHex ?? "");
                    _voices.Add(v);
                }
                Logger.Log("voices: " + _voices.Count);
            }
            catch (Exception ex) { Logger.Log("GetVoices failed: " + ex.Message); }
        }

        static void Parse(VoiceInfo v, string langHex)
        {
            string name = v.Description, langPart = "";
            int dash = v.Description.IndexOf(" - ");
            if (dash > 0) { name = v.Description.Substring(0, dash); langPart = v.Description.Substring(dash + 3); }
            v.Name = name.Replace("Microsoft ", "").Replace(" Online", "").Replace(" (Natural)", "").Replace(" Desktop", "").Trim();

            CultureInfo ci = null;
            string first = langHex.Split(';')[0].Trim();
            int lcid;
            if (first.Length > 0 && int.TryParse(first, NumberStyles.HexNumber, null, out lcid))
                try { ci = new CultureInfo(lcid); } catch { }
            if (ci == null && langPart.Length > 0)
                foreach (var c in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
                    if (string.Equals(c.EnglishName, langPart, StringComparison.OrdinalIgnoreCase)) { ci = c; break; }
            if (ci != null)
            {
                v.Lang2 = ci.TwoLetterISOLanguageName;
                CultureInfo neutral = ci.IsNeutralCulture || ci.Parent == null || ci.Parent.Name.Length == 0 ? ci : ci.Parent;
                string n = neutral.NativeName;
                v.LanguageName = n.Length > 1 ? char.ToUpper(n[0], ci) + n.Substring(1) : n;
            }
            else
            {
                v.LanguageName = langPart.Length > 0 ? langPart : "?";
                if (langPart.StartsWith("Turkish")) v.Lang2 = "tr";
                else if (langPart.StartsWith("English")) v.Lang2 = "en";
            }
        }

        public VoiceInfo FindById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var v in _voices) if (string.Equals(v.Id, id, StringComparison.OrdinalIgnoreCase)) return v;
            return null;
        }

        public VoiceInfo FindByName(string namePart)
        {
            foreach (var v in _voices) if (v.Description.IndexOf(namePart, StringComparison.OrdinalIgnoreCase) >= 0) return v;
            return null;
        }

        // Best voice for a language: natural > multilingual natural > classic.
        public VoiceInfo BestFor(string lang2)
        {
            VoiceInfo natural = null, classic = null, multi = null;
            foreach (var v in _voices)
            {
                if (v.Lang2 == lang2) { if (v.IsNatural) { if (natural == null || (natural.IsMultilingual && !v.IsMultilingual)) natural = v; } else if (classic == null) classic = v; }
                else if (v.IsMultilingual && multi == null) multi = v;
            }
            return natural ?? multi ?? classic;
        }

        public bool HasNaturalVoices { get { foreach (var v in _voices) if (v.IsNatural) return true; return false; } }

        public List<string> LanguagesPresent()
        {
            var list = new List<string>();
            foreach (var v in _voices) if (v.Lang2.Length > 0 && !list.Contains(v.Lang2)) list.Add(v.Lang2);
            return list;
        }

        public bool IsSpeaking
        {
            get
            {
                if (_voice == null) return false;
                try { return (int)Get(Get(_voice, "Status"), "RunningState") == 2; } catch { return false; }
            }
        }

        public void Speak(string text, VoiceInfo voice, int rate)
        {
            if (_voice == null) return;
            if (voice != null) Set(_voice, "Voice", voice.Token);
            Set(_voice, "Rate", Math.Max(-10, Math.Min(10, rate)));
            Call(_voice, "Speak", text, 3);
        }

        public void Stop()
        {
            if (_voice == null) return;
            try { Call(_voice, "Speak", "", 3); } catch { }
        }
    }

    // Script-based language detection: decides whether a text belongs to the primary language.
    public static class TextLanguage
    {
        public static string ScriptOfLanguage(string lang2)
        {
            switch (lang2)
            {
                case "ru": case "uk": case "bg": case "sr": case "kk": return "Cyrl";
                case "ar": case "fa": case "ur": return "Arab";
                case "hi": case "mr": case "ne": return "Deva";
                case "bn": return "Beng";
                case "zh": return "Hani";
                case "ja": return "Jpan";
                case "ko": return "Hang";
                case "el": return "Grek";
                case "he": return "Hebr";
                case "th": return "Thai";
            }
            return "Latn";
        }

        public static string ScriptOfText(string text)
        {
            int latn = 0, cyrl = 0, arab = 0, deva = 0, beng = 0, hani = 0, jpan = 0, hang = 0, grek = 0, hebr = 0, thai = 0;
            foreach (char c in text)
            {
                if (!char.IsLetter(c)) continue;
                if (c < 0x0250) latn++;
                else if (c >= 0x0370 && c <= 0x03FF) grek++;
                else if (c >= 0x0400 && c <= 0x04FF) cyrl++;
                else if (c >= 0x0590 && c <= 0x05FF) hebr++;
                else if (c >= 0x0600 && c <= 0x06FF) arab++;
                else if (c >= 0x0900 && c <= 0x097F) deva++;
                else if (c >= 0x0980 && c <= 0x09FF) beng++;
                else if (c >= 0x0E00 && c <= 0x0E7F) thai++;
                else if ((c >= 0x3040 && c <= 0x30FF)) jpan++;
                else if (c >= 0x4E00 && c <= 0x9FFF) hani++;
                else if (c >= 0xAC00 && c <= 0xD7AF) hang++;
            }
            int max = latn; string s = "Latn";
            if (cyrl > max) { max = cyrl; s = "Cyrl"; }
            if (arab > max) { max = arab; s = "Arab"; }
            if (deva > max) { max = deva; s = "Deva"; }
            if (beng > max) { max = beng; s = "Beng"; }
            if (hang > max) { max = hang; s = "Hang"; }
            if (jpan > 0 && jpan + hani > max) { max = jpan + hani; s = "Jpan"; }
            else if (hani > max) { max = hani; s = "Hani"; }
            if (grek > max) { max = grek; s = "Grek"; }
            if (hebr > max) { max = hebr; s = "Hebr"; }
            if (thai > max) { s = "Thai"; }
            return s;
        }

        public static bool IsPrimary(string text, string primaryLang2)
        {
            string ps = ScriptOfLanguage(primaryLang2), ts = ScriptOfText(text);
            if (ps != ts) return false;
            if (primaryLang2 == "tr")
            {
                const string trChars = "çğışöüÇĞİŞÖÜ";
                foreach (char c in trChars) if (text.IndexOf(c) >= 0) return true;
                return false;
            }
            return true;
        }
    }
}
