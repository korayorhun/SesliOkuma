using System;
using System.IO;

namespace SesliOkuma.Engine
{
    /// <summary>Where the engine writes its diagnostic lines. Nothing is written until a host sets <see cref="Sink"/>.</summary>
    public static class EngineLog
    {
        /// <summary>Receives one line per event (start of a reading, voice loaded, errors). May be called from worker threads.</summary>
        public static Action<string> Sink;

        public static void Log(string message)
        {
            var sink = Sink;
            if (sink == null) return;
            try { sink(message); } catch { }
        }
    }

    /// <summary>Folders the engine stores per-user data in (local voice package, pronunciation dictionary, ducking state).</summary>
    public static class EnginePaths
    {
        static string _dataDir;

        /// <summary>Defaults to %LOCALAPPDATA%\SesliOkuma; a host may point it elsewhere before using the engine.</summary>
        public static string DataDir
        {
            get
            {
                if (_dataDir == null) _dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SesliOkuma");
                try { Directory.CreateDirectory(_dataDir); } catch { }
                return _dataDir;
            }
            set { _dataDir = value; }
        }
    }
}
