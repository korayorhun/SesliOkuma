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
        bool _busy;
        public bool Busy { get { return _busy; } }

        public event Action<int> Progress;        // 0..100 download
        public event Action<string> Status;       // localized status text
        public event Action Completed;
        public event Action<string> Failed;

        public NaturalVoicesInstaller(Control ui) { _ui = ui; }

        void Post(Action a) { try { if (_ui.IsHandleCreated) _ui.BeginInvoke(a); else a(); } catch { } }

        public void Start()
        {
            if (_busy) return;
            _busy = true;
            var t = new Thread(delegate ()
            {
                string zipUrl = null, err = null;
                try { zipUrl = FindZipUrl(); } catch (Exception ex) { err = ex.Message; }
                Post(delegate
                {
                    if (err != null) { Fail(err); return; }
                    Download(zipUrl);
                });
            });
            t.IsBackground = true;
            t.Start();
        }

        static string FindZipUrl()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | (SecurityProtocolType)12288;
            var req = (HttpWebRequest)WebRequest.Create(Api);
            req.UserAgent = UserAgent; req.Accept = "application/vnd.github+json"; req.Timeout = 10000;
            string json;
            using (var resp = req.GetResponse())
            using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) json = sr.ReadToEnd();
            var root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            var assets = root["assets"] as System.Collections.ArrayList;
            if (assets != null)
                foreach (Dictionary<string, object> a in assets)
                {
                    string name = Convert.ToString(a["name"]);
                    if (name.IndexOf("x86_x64", StringComparison.OrdinalIgnoreCase) >= 0 && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        return Convert.ToString(a["browser_download_url"]);
                }
            throw new InvalidDataException("x86_x64 zip not found in latest release");
        }

        void Download(string url)
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
                    string err = null;
                    try { ExtractAndRegister(tmp); } catch (Exception ex) { err = ex.Message; }
                    Post(delegate { if (err != null) Fail(err); else { _busy = false; Logger.Log("natural voices installed"); if (Completed != null) Completed(); } });
                });
                t.IsBackground = true;
                t.Start();
            };
            try { wc.DownloadFileAsync(new Uri(url), tmp); }
            catch (Exception ex) { Fail(ex.Message); }
        }

        static void ExtractAndRegister(string zip)
        {
            Directory.CreateDirectory(TargetDir);
            using (var archive = ZipFile.OpenRead(zip))
                foreach (var entry in archive.Entries)
                {
                    string dest = Path.GetFullPath(Path.Combine(TargetDir, entry.FullName));
                    if (!dest.StartsWith(TargetDir, StringComparison.OrdinalIgnoreCase)) continue;
                    if (entry.Name.Length == 0) { Directory.CreateDirectory(dest); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    entry.ExtractToFile(dest, true);
                }
            try { File.Delete(zip); } catch { }

            string x64 = Path.Combine(TargetDir, @"x64\NaturalVoiceSAPIAdapter.dll");
            string x86 = Path.Combine(TargetDir, @"x86\NaturalVoiceSAPIAdapter.dll");
            if (!File.Exists(x64) || !File.Exists(x86)) throw new FileNotFoundException("adapter DLLs missing after extraction");

            // Single elevated cmd registers both DLLs (one UAC prompt); the user may decline.
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
