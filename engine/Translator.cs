using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace SesliOkuma.Engine
{
    /// <summary>Why a translation did not happen; the host turns the kind into a message in its own language.</summary>
    public enum TranslationErrorKind { Unsupported, BadKey, DeepLQuota, FreeQuota, Other }

    public sealed class TranslationError
    {
        public TranslationErrorKind Kind;
        public string Message;
        public TranslationError(TranslationErrorKind kind, string message) { Kind = kind; Message = message; }
    }

    /// <summary>Thrown inside the free engine when the daily MyMemory allowance is used up.</summary>
    public sealed class TranslationQuotaException : Exception
    {
        public TranslationQuotaException() : base("free translation quota exhausted") { }
    }

    /// <summary>Translation: the free MyMemory service by default (no account), DeepL when a key is given.</summary>
    public static class Translator
    {
        const string UserAgent = "SesliOkuma";

        public static string DeepLTarget(string lang2)
        {
            switch (lang2)
            {
                case "en": return "EN-US";
                case "pt": return "PT-BR";
                case "zh": return "ZH";
                case "hi": return null;                       // not offered by DeepL
                default: return lang2.ToUpperInvariant();
            }
        }

        static string MyMemoryTarget(string lang2)
        {
            switch (lang2) { case "zh": return "zh-CN"; case "pt": return "pt-BR"; }
            return lang2;
        }

        /// <summary>Blocking translation into the two-letter target language; detected is the source language MyMemory/DeepL report.</summary>
        public static string Translate(string apiKey, string text, string targetLang2, out string detected)
        {
            bool useDeepL = apiKey != null && apiKey.Trim().Length > 0;
            string target = useDeepL ? DeepLTarget(targetLang2) : MyMemoryTarget(targetLang2);
            if (target == null) throw new NotSupportedException("target language not offered: " + targetLang2);
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | (SecurityProtocolType)12288;
            return useDeepL ? DeepL(apiKey, text, target, out detected) : MyMemory(text, targetLang2, out detected);
        }

        /// <summary>
        /// Translates on a worker thread and reports back on the caller's synchronization context (the UI thread when
        /// called from one). Exactly one of done/failed is called.
        /// </summary>
        public static void TranslateAsync(string apiKey, string text, string targetLang2, Action<string, string> done, Action<TranslationError> failed)
        {
            bool useDeepL = apiKey != null && apiKey.Trim().Length > 0;
            string target = useDeepL ? DeepLTarget(targetLang2) : MyMemoryTarget(targetLang2);
            if (target == null) { failed(new TranslationError(TranslationErrorKind.Unsupported, targetLang2)); return; }
            var context = SynchronizationContext.Current;
            var t = new Thread(delegate ()
            {
                string translated = null, detected = "";
                TranslationError error = null;
                try { translated = Translate(apiKey, text, targetLang2, out detected); }
                catch (TranslationQuotaException) { error = new TranslationError(TranslationErrorKind.FreeQuota, "quota"); }
                catch (WebException wex)
                {
                    var r = wex.Response as HttpWebResponse;
                    if (r != null && (int)r.StatusCode == 403) error = new TranslationError(TranslationErrorKind.BadKey, wex.Message);
                    else if (r != null && (int)r.StatusCode == 456) error = new TranslationError(TranslationErrorKind.DeepLQuota, wex.Message);
                    else if (r != null && (int)r.StatusCode == 429) error = new TranslationError(TranslationErrorKind.FreeQuota, wex.Message);
                    else error = new TranslationError(TranslationErrorKind.Other, wex.Message);
                }
                catch (Exception ex) { error = new TranslationError(TranslationErrorKind.Other, ex.Message); }
                Action report = delegate
                {
                    if (error != null) { EngineLog.Log("translate: " + error.Kind + " " + error.Message); failed(error); }
                    else { EngineLog.Log("translated(" + (useDeepL ? "deepl" : "free") + ") " + detected + "->" + target + " len=" + translated.Length); done(translated, detected); }
                };
                try { if (context != null) context.Post(delegate { report(); }, null); else report(); } catch { }
            });
            t.IsBackground = true; t.Start();
        }

        // MyMemory: ~5000 chars/day per IP, 500 bytes per request -> translate chunk by chunk, source auto-detected.
        static string MyMemory(string text, string targetLang2, out string detected)
        {
            detected = "";
            var chunks = new List<string>();
            foreach (string sentence in Reader.Split(text))
            {
                string s = sentence;
                while (Encoding.UTF8.GetByteCount(s) > 450)
                {
                    int cut = s.LastIndexOf(' ', Math.Min(s.Length - 1, 300));
                    if (cut < 40) cut = Math.Min(300, s.Length - 1);
                    chunks.Add(s.Substring(0, cut));
                    s = s.Substring(cut).TrimStart();
                }
                chunks.Add(s);
            }
            var ser = new JavaScriptSerializer();
            var sb = new StringBuilder();
            foreach (string chunk in chunks)
            {
                string url = "https://api.mymemory.translated.net/get?q=" + Uri.EscapeDataString(chunk) + "&langpair=Autodetect|" + MyMemoryTarget(targetLang2);
                Dictionary<string, object> root;
                // The service occasionally returns a transient error status; retry each chunk once.
                try { root = FetchChunk(ser, url); }
                catch (TranslationQuotaException) { throw; }
                catch (InvalidOperationException)
                {
                    Thread.Sleep(700);
                    root = FetchChunk(ser, url);
                }
                var data = (Dictionary<string, object>)root["responseData"];
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(Convert.ToString(data["translatedText"]));
                object det;
                if (detected.Length == 0 && data.TryGetValue("detectedLanguage", out det) && det != null) detected = Convert.ToString(det).ToUpperInvariant();
            }
            return sb.ToString();
        }

        static Dictionary<string, object> FetchChunk(JavaScriptSerializer ser, string url)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Timeout = 15000; req.UserAgent = UserAgent;
            string json;
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) json = sr.ReadToEnd();
            var root = ser.Deserialize<Dictionary<string, object>>(json);
            object qf;
            if (root.TryGetValue("quotaFinished", out qf) && qf is bool && (bool)qf) throw new TranslationQuotaException();
            object statusObj;
            int status = root.TryGetValue("responseStatus", out statusObj) ? Convert.ToInt32(statusObj) : 200;
            if (status == 429) throw new TranslationQuotaException();
            if (status != 200) throw new InvalidOperationException("MyMemory " + status);
            return root;
        }

        static string DeepL(string apiKey, string text, string target, out string detected)
        {
            string host = apiKey.TrimEnd().EndsWith(":fx") ? "https://api-free.deepl.com" : "https://api.deepl.com";
            var req = (HttpWebRequest)WebRequest.Create(host + "/v2/translate");
            req.Method = "POST"; req.Timeout = 15000; req.UserAgent = UserAgent;
            req.Headers["Authorization"] = "DeepL-Auth-Key " + apiKey.Trim();
            req.ContentType = "application/x-www-form-urlencoded";
            byte[] body = Encoding.UTF8.GetBytes("text=" + Uri.EscapeDataString(text) + "&target_lang=" + target);
            using (var rs = req.GetRequestStream()) rs.Write(body, 0, body.Length);
            string json;
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) json = sr.ReadToEnd();
            var root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            var arr = root["translations"] as System.Collections.ArrayList;
            var first = (Dictionary<string, object>)arr[0];
            detected = first.ContainsKey("detected_source_language") ? Convert.ToString(first["detected_source_language"]) : "";
            return Convert.ToString(first["text"]);
        }
    }
}
