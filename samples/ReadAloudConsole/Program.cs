using System;
using System.Threading;
using SesliOkuma.Engine;

// The smallest possible use of SesliOkuma.Engine: read a text aloud with the best voice for its language and
// print each word as it is spoken. Build with build.ps1 in this folder (after ..\..\build.ps1).
static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        string text = args.Length > 0 ? string.Join(" ", args)
            : "Merhaba! Bu metin Sesli Okuma motoru ile okunuyor; 25°C sıcakta Netcad kullanmak keyifli.";

        EngineLog.Sink = delegate(string line) { Console.Error.WriteLine("  [engine] " + line); };

        var engine = new SpeechEngine();
        engine.RefreshVoices();
        bool turkish = TextLanguage.IsPrimary(text, "tr");
        VoiceInfo voice = engine.BestFor(turkish ? "tr" : "en") ?? (engine.Voices.Count > 0 ? engine.Voices[0] : null);
        if (voice == null) { Console.Error.WriteLine("No voice is installed."); return 1; }
        Console.WriteLine("voice: " + voice.Name + " (" + voice.Lang2 + (voice.Provider == VoiceProvider.Local ? ", offline" : "") + ")");

        // What the voice will actually say differs from the text (symbols, English words, ALL CAPS); show the changes.
        var map = SpeechText.Build(text, voice.Lang2);
        if (map.Replacements > 0) Console.WriteLine("speech layer: " + map.Examples);

        var reader = new Reader(engine, delegate { return 0; });        // rate 0 = normal speed
        bool done = false;
        reader.Completed += delegate(bool finished) { done = true; };
        int lastStart = -1;
        reader.Position += delegate
        {
            int start = reader.CharIndex, length = reader.WordLength;
            if (start != lastStart && length > 0 && start + length <= reader.FullText.Length)
            {
                lastStart = start;
                Console.WriteLine("  " + reader.FullText.Substring(start, length));
            }
        };
        reader.Start(text, voice);
        while (!done) { Thread.Sleep(120); reader.Tick(); }       // Tick drives position events and end detection
        return 0;
    }
}
