using SesliOkuma.Engine;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace SesliOkuma
{
    // One-click installer for the open-source NaturalVoiceSAPIAdapter (MIT): downloads the latest release
    // from its own GitHub project into the user's profile and registers the x64/x86 DLLs (one UAC prompt).
    public sealed class NaturalVoicesInstaller
    {
        const string Api = "https://api.github.com/repos/gexgd0419/NaturalVoiceSAPIAdapter/releases/latest";
        const string UserAgent = "SesliOkuma";
        static readonly string TargetDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\NaturalVoiceSAPIAdapter");

        readonly Control _ui;
        bool _busy, _checked;
        public bool Busy { get { return _busy; } }

        /// <summary>Version of the adapter in the user-profile install; null when the voices come from elsewhere or are absent.</summary>
        public static string InstalledVersion
        {
            get
            {
                try
                {
                    string dll = Path.Combine(TargetDir, @"x64\NaturalVoiceSAPIAdapter.dll");
                    if (!File.Exists(dll)) return null;
                    var fv = FileVersionInfo.GetVersionInfo(dll);
                    return new Version(fv.FileMajorPart, fv.FileMinorPart, fv.FileBuildPart).ToString();
                }
                catch { return null; }
            }
        }

        public string LatestVersion { get; private set; }     // from the last release lookup
        public bool RestartNeeded { get; private set; }       // the update replaced a DLL this process had loaded

        public bool UpdateAvailable
        {
            get
            {
                Version cur, latest;
                return InstalledVersion != null && LatestVersion != null
                    && Version.TryParse(InstalledVersion, out cur) && Version.TryParse(LatestVersion, out latest) && latest > cur;
            }
        }

        // Looks the latest release up once per app session (a failed lookup is not retried); done runs on the UI thread.
        public void CheckLatestAsync(Action done)
        {
            if (_checked) { if (done != null) done(); return; }
            _checked = true;
            var t = new Thread(delegate ()
            {
                string tag = null;
                try { tag = TagOf(FetchLatest()); } catch (Exception ex) { Logger.Log("natural voices: version check failed: " + ex.Message); }
                Post(delegate
                {
                    if (tag != null) { LatestVersion = tag; Logger.Log("natural voices: installed " + (InstalledVersion ?? "-") + ", latest " + tag); }
                    if (done != null) done();
                });
            });
            t.IsBackground = true;
            t.Start();
        }

        // Deletes the DLLs an update had to rename because they were loaded at the time (see ExtractAndRegister).
        public static void CleanupOld()
        {
            try
            {
                if (!Directory.Exists(TargetDir)) return;
                foreach (string old in Directory.GetFiles(TargetDir, "*.old", SearchOption.AllDirectories))
                    try { File.Delete(old); } catch { }
            }
            catch { }
        }

        public event Action<int> Progress;        // 0..100 download
        public event Action<string> Status;       // localized status text
        public event Action Completed;
        public event Action<string> Failed;

        public NaturalVoicesInstaller(Control ui) { _ui = ui; }

        void Post(Action a) { try { if (_ui.IsHandleCreated) _ui.BeginInvoke(a); else a(); } catch { } }

        public void Start()
        {
            if (_busy) return;
            _busy = true; RestartNeeded = false;
            bool update = InstalledVersion != null;
            var t = new Thread(delegate ()
            {
                string zipUrl = null, tag = null, err = null;
                try { var release = FetchLatest(); zipUrl = ZipUrlOf(release); tag = TagOf(release); } catch (Exception ex) { err = ex.Message; }
                Post(delegate
                {
                    if (tag != null) { LatestVersion = tag; _checked = true; }
                    if (err != null) { Fail(err); return; }
                    Download(zipUrl, update);
                });
            });
            t.IsBackground = true;
            t.Start();
        }

        static Dictionary<string, object> FetchLatest()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | (SecurityProtocolType)12288;
            var req = (HttpWebRequest)WebRequest.Create(Api);
            req.UserAgent = UserAgent; req.Accept = "application/vnd.github+json"; req.Timeout = 10000;
            string json;
            using (var resp = req.GetResponse())
            using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) json = sr.ReadToEnd();
            return new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
        }

        static string TagOf(Dictionary<string, object> release)
        {
            object tag;
            return release.TryGetValue("tag_name", out tag) && tag != null ? Convert.ToString(tag).TrimStart('v', 'V') : null;
        }

        static string ZipUrlOf(Dictionary<string, object> release)
        {
            var assets = release["assets"] as System.Collections.ArrayList;
            if (assets != null)
                foreach (Dictionary<string, object> a in assets)
                {
                    string name = Convert.ToString(a["name"]);
                    if (name.IndexOf("x86_x64", StringComparison.OrdinalIgnoreCase) >= 0 && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        return Convert.ToString(a["browser_download_url"]);
                }
            throw new InvalidDataException("x86_x64 zip not found in latest release");
        }

        void Download(string url, bool update)
        {
            string tmp = Path.Combine(Path.GetTempPath(), "NaturalVoiceSAPIAdapter.zip");
            var wc = new WebClient();
            wc.Headers[HttpRequestHeader.UserAgent] = UserAgent;
            wc.DownloadProgressChanged += delegate (object s, DownloadProgressChangedEventArgs e) { if (Progress != null) Progress(e.ProgressPercentage); };
            wc.DownloadFileCompleted += delegate (object s, AsyncCompletedEventArgs e)
            {
                wc.Dispose();
                if (e.Cancelled || e.Error != null) { Fail(e.Error != null ? e.Error.Message : L.T("Cancelled")); return; }
                if (Status != null) Status(L.T("NaturalRegistering"));
                var t = new Thread(delegate ()
                {
                    string err = null; bool restart = false;
                    try { restart = ExtractAndRegister(tmp, update); } catch (Exception ex) { err = ex.Message; }
                    Post(delegate
                    {
                        if (err != null) { Fail(err); return; }
                        _busy = false; RestartNeeded = restart;
                        Logger.Log("natural voices installed" + (restart ? " (restart needed)" : ""));
                        if (Completed != null) Completed();
                    });
                });
                t.IsBackground = true;
                t.Start();
            };
            try { wc.DownloadFileAsync(new Uri(url), tmp); }
            catch (Exception ex) { Fail(ex.Message); }
        }

        // Returns true when a loaded DLL had to be renamed aside: the new adapter is only used by a fresh process.
        static bool ExtractAndRegister(string zip, bool update)
        {
            bool replacedInUse = false;
            Directory.CreateDirectory(TargetDir);
            using (var archive = ZipFile.OpenRead(zip))
                foreach (var entry in archive.Entries)
                {
                    string dest = Path.GetFullPath(Path.Combine(TargetDir, entry.FullName));
                    if (!dest.StartsWith(TargetDir, StringComparison.OrdinalIgnoreCase)) continue;
                    if (entry.Name.Length == 0) { Directory.CreateDirectory(dest); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    try { entry.ExtractToFile(dest, true); }
                    catch (IOException)
                    {
                        // A DLL mapped into this process (SAPI loads the adapter in-proc) cannot be overwritten but can be
                        // renamed; the stale copy is deleted by CleanupOld on the next start.
                        if (!File.Exists(dest)) throw;
                        string old = dest + ".old";
                        try { File.Delete(old); } catch { }
                        File.Move(dest, old);
                        entry.ExtractToFile(dest, true);
                        replacedInUse = true;
                    }
                }
            try { File.Delete(zip); } catch { }

            string x64 = Path.Combine(TargetDir, @"x64\NaturalVoiceSAPIAdapter.dll");
            string x86 = Path.Combine(TargetDir, @"x86\NaturalVoiceSAPIAdapter.dll");
            if (!File.Exists(x64) || !File.Exists(x86)) throw new FileNotFoundException("adapter DLLs missing after extraction");

            try { Register(x64, x86); }
            catch (Exception ex)
            {
                // An update keeps the previous version's registration (same path and class ids), so a declined elevation
                // only skips the re-registration; a first install has nothing to fall back on.
                if (!update) throw;
                Logger.Log("natural voices: re-registration skipped (" + ex.Message + ")");
            }
            return replacedInUse;
        }

        // Single elevated cmd registers both DLLs (one UAC prompt); the user may decline.
        static void Register(string x64, string x86)
        {
            var psi = new ProcessStartInfo("cmd.exe", "/c regsvr32 /s \"" + x64 + "\" & regsvr32 /s \"" + x86 + "\"")
            { Verb = "runas", UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden };
            using (var p = Process.Start(psi))
            {
                p.WaitForExit();
                if (p.ExitCode != 0) throw new InvalidOperationException("regsvr32 exit " + p.ExitCode);
            }
        }

        void Fail(string msg) { _busy = false; Logger.Log("natural voices install failed: " + msg); if (Failed != null) Failed(msg); }

        public static void OpenWindowsVoiceSettings()
        {
            try { Process.Start(new ProcessStartInfo("ms-settings:speech") { UseShellExecute = true }); } catch { }
        }
    }
}
