using System;
using System.Reflection;

namespace SesliOkuma
{
    // Short spoken status cues for events that have no visible window (accessibility mode only).
    // Uses its own SAPI voice so it never purges or pauses the main reading channel's queue,
    // and each cue purges the previous cue so they never stack up.
    public static class Announcer
    {
        static object _voice;
        static bool _failed;

        public static bool Enabled;

        public static void Speak(string text)
        {
            if (!Enabled || text == null || text.Length == 0 || _failed) return;
            try
            {
                if (_voice == null)
                {
                    var type = Type.GetTypeFromProgID("SAPI.SpVoice");
                    _voice = Activator.CreateInstance(type);
                }
                _voice.GetType().InvokeMember("Speak", BindingFlags.InvokeMethod, null, _voice, new object[] { text, 3 });   // async + purge own queue
            }
            catch (Exception ex) { _failed = true; Logger.Log("announcer: " + ex.Message); }
        }
    }
}
