using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;

namespace SesliOkuma
{
    // Installs Microsoft's offline text-to-speech pack for a language (e.g. Tolga for tr-TR) through Windows' own
    // optional-features mechanism: one elevated PowerShell running Add-WindowsCapability.
    public static class WindowsVoicePack
    {
        public static string CultureFor(string lang2)
        {
            switch (lang2)
            {
                case "tr": return "tr-TR";
                case "en": return "en-US";
                case "zh": return "zh-CN";
                case "hi": return "hi-IN";
                case "es": return "es-ES";
                case "fr": return "fr-FR";
                case "ar": return "ar-SA";
                case "pt": return "pt-BR";
                case "de": return "de-DE";
                case "ru": return "ru-RU";
                case "it": return "it-IT";
                case "ja": return "ja-JP";
                case "ko": return "ko-KR";
                case "nl": return "nl-NL";
                case "pl": return "pl-PL";
            }
            return null;
        }

        public static string VoiceNameFor(string lang2)
        {
            switch (lang2)
            {
                case "tr": return "Tolga";
                case "en": return "David, Zira, Mark";
                case "zh": return "Huihui, Kangkang";
                case "hi": return "Hemant, Kalpana";
                case "es": return "Helena, Pablo";
                case "fr": return "Hortense, Paul";
                case "ar": return "Naayf";
                case "pt": return "Maria, Daniel";
                case "de": return "Hedda, Stefan";
                case "ru": return "Irina, Pavel";
            }
            return "";
        }

        public static void InstallAsync(Control ui, string lang2, Action done, Action<string> failed)
        {
            string culture = CultureFor(lang2);
            if (culture == null) { failed("unsupported language"); return; }
            var t = new Thread(delegate ()
            {
                string err = null;
                try
                {
                    string cmd = "$ErrorActionPreference='Stop'; Add-WindowsCapability -Online -Name 'Language.TextToSpeech~~~" + culture + "~0.0.1.0' | Out-Null; exit 0";
                    var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -WindowStyle Hidden -Command \"" + cmd + "\"")
                    { Verb = "runas", UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden };
                    using (var p = Process.Start(psi))
                    {
                        p.WaitForExit();
                        if (p.ExitCode != 0) err = "Add-WindowsCapability exit " + p.ExitCode;
                    }
                }
                catch (Exception ex) { err = ex.Message; }   // includes UAC cancel (Win32Exception 1223)
                try { ui.BeginInvoke(new Action(delegate { if (err != null) { Logger.Log("windows voice pack: " + err); failed(err); } else { Logger.Log("windows voice pack installed: " + culture); done(); } })); } catch { }
            });
            t.IsBackground = true; t.Start();
        }
    }
}
