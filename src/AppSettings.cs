using System;
using System.IO;
using System.Reflection;

namespace SesliOkuma
{
    public sealed class AppSettings
    {
        public string TrVoiceId = "";
        public string EnVoiceId = "";
        public int Rate = 0;

        public static string AppDir { get { return AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'); } }
        // Per-user data folder; the install folder may be read-only.
        public static string DataDir
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SesliOkuma");
                try { Directory.CreateDirectory(dir); } catch { }
                return dir;
            }
        }
        static string FilePath { get { return Path.Combine(DataDir, "settings.ini"); } }

        public static AppSettings Load()
        {
            var s = new AppSettings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim(), val = line.Substring(eq + 1).Trim();
                    if (key == "TrVoice") s.TrVoiceId = val;
                    else if (key == "EnVoice") s.EnVoiceId = val;
                    else if (key == "Rate") { int r; if (int.TryParse(val, out r)) s.Rate = r; }
                }
            }
            catch (Exception ex) { Logger.Log("settings load: " + ex.Message); }
            return s;
        }

        public void Save()
        {
            try { File.WriteAllLines(FilePath, new[] { "TrVoice=" + TrVoiceId, "EnVoice=" + EnVoiceId, "Rate=" + Rate }); }
            catch (Exception ex) { Logger.Log("settings save: " + ex.Message); }
        }
    }

    public static class StartupShortcut
    {
        static string LinkPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "SesliOkuma.lnk"); } }

        public static bool IsEnabled { get { return File.Exists(LinkPath); } }

        public static void SetEnabled(bool enabled)
        {
            try
            {
                if (!enabled) { if (File.Exists(LinkPath)) File.Delete(LinkPath); return; }
                string exe = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                object shell = Activator.CreateInstance(shellType);
                object link = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { LinkPath });
                Type lt = link.GetType();
                lt.InvokeMember("TargetPath", BindingFlags.SetProperty, null, link, new object[] { exe });
                lt.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, link, new object[] { Path.GetDirectoryName(exe) });
                lt.InvokeMember("Description", BindingFlags.SetProperty, null, link, new object[] { "Sesli Okuma - Ctrl+Alt+S" });
                lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
            }
            catch (Exception ex) { Logger.Log("startup shortcut: " + ex.Message); }
        }
    }

    public static class Logger
    {
        static readonly string LogPath = Path.Combine(AppSettings.DataDir, "SesliOkuma.log");
        static readonly object Gate = new object();

        public static void Log(string msg)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
                    File.AppendAllText(LogPath, DateTime.Now.ToString("s") + " " + msg + Environment.NewLine);
                }
            }
            catch { }
        }
    }
}
