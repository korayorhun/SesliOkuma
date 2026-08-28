using System;
using System.Collections.Generic;
using System.Reflection;

namespace SesliOkuma
{
    public sealed class VoiceInfo
    {
        public string Id;
        public string Description;
        public string Name;
        public string Language;
        public string LangHex;
        public object Token;

        public bool IsTurkish { get { return LangHex.IndexOf("41F", StringComparison.OrdinalIgnoreCase) >= 0 || Description.IndexOf("Turkish", StringComparison.OrdinalIgnoreCase) >= 0; } }
        public bool IsNatural { get { return Description.IndexOf("Natural", StringComparison.OrdinalIgnoreCase) >= 0; } }
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
                    try { v.LangHex = Convert.ToString(Call(tok, "GetAttribute", "Language")); } catch { v.LangHex = ""; }
                    if (v.LangHex == null) v.LangHex = "";
                    ParseDescription(v);
                    _voices.Add(v);
                }
                Logger.Log("voices: " + _voices.Count);
            }
            catch (Exception ex) { Logger.Log("GetVoices failed: " + ex.Message); }
        }

        static void ParseDescription(VoiceInfo v)
        {
            string name = v.Description, lang = "";
            int dash = v.Description.IndexOf(" - ");
            if (dash > 0) { name = v.Description.Substring(0, dash); lang = v.Description.Substring(dash + 3); }
            name = name.Replace("Microsoft ", "").Replace(" Online", "").Replace(" (Natural)", "").Replace(" Desktop", "").Trim();
            v.Name = name;
            v.Language = PrettyLanguage(lang);
        }

        static string PrettyLanguage(string lang)
        {
            if (lang.StartsWith("Turkish")) return "Türkçe";
            if (lang == "English (United States)") return "İngilizce (ABD)";
            if (lang == "English (United Kingdom)") return "İngilizce (İngiltere)";
            if (lang == "English (India)") return "İngilizce (Hindistan)";
            if (lang.StartsWith("German")) return "Almanca";
            if (lang.StartsWith("French")) return "Fransızca";
            if (lang.StartsWith("Spanish")) return "İspanyolca";
            return lang.Length == 0 ? "Diğer" : lang;
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
}
