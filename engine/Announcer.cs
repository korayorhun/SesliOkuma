using System;
using System.Reflection;

namespace SesliOkuma.Engine
{
    // Short spoken status cues for events that have no visible window (accessibility mode only).
    // Uses its own SAPI voice so it never purges or pauses the main reading channel's queue,
    // and each cue purges the previous cue so they never stack up.
    public static class Announcer
    {
        static object _voice;
        static bool _failed;

        public static bool Enabled;
        public static object VoiceToken;                 // SAPI token matching the UI language; set by TrayApp
        public static bool PreferLocal;                  // cues follow the user's local primary voice (consistent + offline)
        public static Func<string, bool> LocalFallback;  // speaks through the local voice

        public static void Speak(string text)
        {
            if (!Enabled || text == null || text.Length == 0 || _failed) return;
            try
            {
                if ((PreferLocal || VoiceToken == null) && LocalFallback != null && LocalFallback(text)) return;
                if (_voice == null)
                {
                    var type = Type.GetTypeFromProgID("SAPI.SpVoice");
                    _voice = Activator.CreateInstance(type);
                }
                if (VoiceToken != null)
                    try { _voice.GetType().InvokeMember("Voice", BindingFlags.SetProperty, null, _voice, new object[] { VoiceToken }); } catch { }
                _voice.GetType().InvokeMember("Speak", BindingFlags.InvokeMethod, null, _voice, new object[] { text, 3 });   // async + purge own queue
            }
            catch (Exception ex) { _failed = true; EngineLog.Log("announcer: " + ex.Message); }
        }

        public static bool IsBusy
        {
            get
            {
                if (LocalTrVoice.IsCuePlaying) return true;
                if (_voice == null) return false;
                try
                {
                    object status = _voice.GetType().InvokeMember("Status", BindingFlags.GetProperty, null, _voice, null);
                    return (int)status.GetType().InvokeMember("RunningState", BindingFlags.GetProperty, null, status, null) == 2;
                }
                catch { return false; }
            }
        }

        // Cuts a cue that is still talking, so real reading never overlaps it.
        public static void Silence()
        {
            try { LocalTrVoice.StopCue(); } catch { }
            if (_voice == null) return;
            try { _voice.GetType().InvokeMember("Speak", BindingFlags.InvokeMethod, null, _voice, new object[] { "", 3 }); } catch { }
        }
    }
}
