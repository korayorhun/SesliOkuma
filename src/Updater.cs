using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace SesliOkuma
{
    public sealed class UpdateInfo
    {
        public Version Version;
        public string Tag;
        public string PageUrl;
        public string SetupUrl;
        public string ShaUrl;
        public string Notes;
    }

    // Checks GitHub Releases for a newer version, downloads the installer, verifies its SHA-256 and runs it silently.
    public sealed class Updater
    {
        const string Owner = "korayorhun", Repo = "SesliOkuma";
        const string ApiLatest = "https://api.github.com/repos/" + Owner + "/" + Repo + "/releases/latest";
        const string UserAgent = "SesliOkuma-Updater";
        public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

        readonly Control _ui;
        bool _busy;

        public UpdateInfo Available;
        public event Action<UpdateInfo> UpdateFound;
        public event Action<string> CheckFinished;          // status text for the UI ("Güncel", error…)
        public event Action<int> DownloadProgress;          // 0..100
        public event Action<string> UpdateFailed;

        public Updater(Control uiThread) { _ui = uiThread; }

        public static Version CurrentVersion
        {
            get
            {
                string forced = Environment.GetEnvironmentVariable("SESLIOKUMA_FAKE_VERSION");
                Version v;
                if (!string.IsNullOrEmpty(forced) && Version.TryParse(forced, out v)) return v;
                return new Version(Application.ProductVersion);
            }
        }

        public static string CurrentVersionText
        {
            get { var v = CurrentVersion; return v.Major + "." + v.Minor + "." + v.Build; }
        }

        public void CheckAsync(bool manual)
        {
            if (_busy) return;
            _busy = true;
            var t = new Thread(delegate ()
            {
                UpdateInfo info = null; string error = null;
                try { info = FetchLatest(); }
                catch (Exception ex) { error = ex.Message; }
                Post(delegate
                {
                    _busy = false;
                    if (error != null)
                    {
                        Logger.Log("update check failed: " + error);
                        Fire(CheckFinished, manual ? L.T("CheckFailed") : null);
                        return;
                    }
                    if (info != null && info.Version > CurrentVersion)
                    {
                        Available = info;
                        Logger.Log("update available: " + info.Tag);
                        if (UpdateFound != null) UpdateFound(info);
                        Fire(CheckFinished, L.F("NewVersion", info.Version.ToString(3)));
                    }
                    else
                    {
                        Available = null;
                        Fire(CheckFinished, manual ? L.F("UpToDate", CurrentVersionText) : null);
                    }
                });
            });
            t.IsBackground = true;
            t.Start();
        }

        void Fire(Action<string> ev, string text) { if (ev != null && text != null) ev(text); }
        void Post(Action a) { try { if (_ui.IsHandleCreated) _ui.BeginInvoke(a); else a(); } catch { } }

        static UpdateInfo FetchLatest()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | (SecurityProtocolType)12288;
            var req = (HttpWebRequest)WebRequest.Create(ApiLatest);
            req.UserAgent = UserAgent;
            req.Accept = "application/vnd.github+json";
            req.Timeout = 10000;
            string json;
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) json = sr.ReadToEnd();

            var ser = new JavaScriptSerializer();
            var root = ser.Deserialize<Dictionary<string, object>>(json);
            var info = new UpdateInfo();
            info.Tag = Convert.ToString(root["tag_name"]);
            info.PageUrl = Convert.ToString(root["html_url"]);
            info.Notes = root.ContainsKey("body") ? Convert.ToString(root["body"]) : "";
            Version v;
            if (!Version.TryParse(info.Tag.TrimStart('v', 'V'), out v)) throw new FormatException("tag: " + info.Tag);
            info.Version = v;
            var assets = root["assets"] as System.Collections.ArrayList;
            if (assets != null)
                foreach (Dictionary<string, object> a in assets)
                {
                    string name = Convert.ToString(a["name"]), url = Convert.ToString(a["browser_download_url"]);
                    if (name.StartsWith("SesliOkuma-Setup-", StringComparison.OrdinalIgnoreCase))
                    {
                        if (name.EndsWith(".exe.sha256", StringComparison.OrdinalIgnoreCase)) info.ShaUrl = url;
                        else if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) info.SetupUrl = url;
                    }
                }
            if (info.SetupUrl == null) throw new InvalidDataException("setup asset missing");
            return info;
        }

        public void DownloadAndInstall(UpdateInfo info)
        {
            if (_busy || info == null) return;
            _busy = true;
            string dir = Path.Combine(Path.GetTempPath(), "SesliOkuma-Update");
            Directory.CreateDirectory(dir);
            string setup = Path.Combine(dir, "SesliOkuma-Setup-" + info.Version.ToString(3) + ".exe");
            var wc = new WebClient();
            wc.Headers[HttpRequestHeader.UserAgent] = UserAgent;
            wc.DownloadProgressChanged += delegate (object s, DownloadProgressChangedEventArgs e) { if (DownloadProgress != null) DownloadProgress(e.ProgressPercentage); };
            wc.DownloadFileCompleted += delegate (object s, System.ComponentModel.AsyncCompletedEventArgs e)
            {
                wc.Dispose();
                if (e.Cancelled || e.Error != null) { Fail((e.Error != null ? e.Error.Message : L.T("Cancelled"))); return; }
                var t = new Thread(delegate ()
                {
                    string err = null;
                    try { Verify(setup, info.ShaUrl); }
                    catch (Exception ex) { err = ex.Message; }
                    Post(delegate
                    {
                        if (err != null) { try { File.Delete(setup); } catch { } Fail(err); return; }
                        Logger.Log("update verified, launching installer " + info.Version.ToString(3));
                        try
                        {
                            Process.Start(new ProcessStartInfo(setup, "/SILENT /NORESTART /SP- /UPDATE=1") { UseShellExecute = true });
                            Application.Exit();
                        }
                        catch (Exception ex) { Fail(ex.Message); }
                    });
                });
                t.IsBackground = true;
                t.Start();
            };
            try { wc.DownloadFileAsync(new Uri(info.SetupUrl), setup); }
            catch (Exception ex) { Fail(ex.Message); }
        }

        void Fail(string msg) { _busy = false; Logger.Log("update failed: " + msg); if (UpdateFailed != null) UpdateFailed(msg); }

        static void Verify(string file, string shaUrl)
        {
            if (shaUrl == null) throw new InvalidDataException("no .sha256 published for this release");
            var wc = new WebClient();
            wc.Headers[HttpRequestHeader.UserAgent] = UserAgent;
            string expected = wc.DownloadString(shaUrl).Trim();
            if (expected.Length < 64) throw new InvalidDataException("invalid .sha256 file");
            expected = expected.Substring(0, 64);
            string actual;
            using (var sha = SHA256.Create())
            using (var fs = File.OpenRead(file))
                actual = BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "");
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("SHA-256 mismatch");
        }
    }
}
