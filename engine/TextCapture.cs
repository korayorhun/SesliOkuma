using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace SesliOkuma.Engine
{
    /// <summary>
    /// Gets the text the user means from whatever application is in front: the selection through UI Automation,
    /// a transparent Ctrl+C that restores the clipboard, the paragraph under the pointer, or the clipboard itself.
    /// No window framework is involved; everything goes through Win32 and UI Automation.
    /// </summary>
    public static class TextCapture
    {
        [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")] static extern uint GetClipboardSequenceNumber();
        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT pt);
        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }

        const byte VK_CONTROL = 0x11, VK_MENU = 0x12, VK_SHIFT = 0x10, VK_LWIN = 0x5B, VK_C = 0x43;
        const uint KEYEVENTF_KEYUP = 2;

        /// <summary>Electron editors expose only a small accessibility window through UIA, so their selection is copied instead.</summary>
        public static string[] CopyFirstApps = { "Code", "Cursor", "VSCodium", "windsurf", "Code - Insiders" };

        /// <summary>Where a capture came from, for logging: "uia", "copy", "pointer" or "clipboard".</summary>
        public sealed class Capture
        {
            public string Text;
            public string Source;
            public long UiaMs, CopyMs, TotalMs;
        }

        /// <summary>The full chain: selection via UIA (or copy first in Electron editors), copy, paragraph under the pointer, clipboard. Null when nothing was found.</summary>
        public static Capture Grab(bool allowPointer, bool allowClipboard)
        {
            var sw = Stopwatch.StartNew();
            var result = new Capture();
            bool preferCopy = IsCopyFirstApp();
            result.Source = preferCopy ? "copy!" : "uia";
            result.Text = preferCopy ? SelectionViaCopy() : SelectionViaUia();
            result.UiaMs = sw.ElapsedMilliseconds;
            if (result.Text == null) { result.Source = "copy"; result.Text = SelectionViaCopy(); }
            result.CopyMs = sw.ElapsedMilliseconds;
            if (result.Text == null && allowPointer) { result.Source = "pointer"; result.Text = ParagraphUnderPointer(); }
            if (result.Text == null && allowClipboard) { result.Source = "clipboard"; result.Text = ClipboardText(); }
            result.TotalMs = sw.ElapsedMilliseconds;
            return result.Text != null && result.Text.Trim().Length > 0 ? result : null;
        }

        public static bool IsCopyFirstApp()
        {
            string app = ForegroundApp();
            foreach (string a in CopyFirstApps) if (string.Equals(app, a, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>Process name of the foreground window ("chrome", "WINWORD"), "?" when unknown.</summary>
        public static string ForegroundApp()
        {
            try { uint pid; GetWindowThreadProcessId(GetForegroundWindow(), out pid); return Process.GetProcessById((int)pid).ProcessName; }
            catch { return "?"; }
        }

        /// <summary>The selected text of the focused element, when it exposes UI Automation's TextPattern.</summary>
        public static string SelectionViaUia()
        {
            try
            {
                AutomationElement el = AutomationElement.FocusedElement;
                if (el == null) return null;
                object pat;
                if (el.TryGetCurrentPattern(TextPattern.Pattern, out pat))
                {
                    TextPatternRange[] ranges = ((TextPattern)pat).GetSelection();
                    if (ranges != null && ranges.Length > 0)
                    {
                        string s = ranges[0].GetText(-1);
                        if (s != null && s.Trim().Length > 0) return s;
                    }
                }
            }
            catch (Exception ex) { EngineLog.Log("UIA: " + ex.Message); }
            return null;
        }

        /// <summary>
        /// Sends Ctrl+C to the foreground application and reads what it copied; the previous clipboard text is put back.
        /// Waits up to 900 ms because Electron applications write the clipboard asynchronously.
        /// </summary>
        public static string SelectionViaCopy()
        {
            string old = Win32Clipboard.ReadText();
            // Snapshot the sequence BEFORE injecting keys; Electron apps write the clipboard asynchronously.
            uint seq = GetClipboardSequenceNumber();
            // Release whatever modifiers a hotkey holds down, then send a clean Ctrl+C.
            keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_SHIFT, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            Thread.Sleep(30);
            keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
            keybd_event(VK_C, 0, 0, UIntPtr.Zero);
            keybd_event(VK_C, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            string text = null;
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 900)
            {
                Thread.Sleep(15);
                if (GetClipboardSequenceNumber() == seq) continue;
                for (int r = 0; r < 5 && (text == null || text.Trim().Length == 0); r++)
                {
                    text = Win32Clipboard.ReadText();
                    if (text == null || text.Trim().Length == 0) Thread.Sleep(25);
                }
                break;
            }
            if (GetClipboardSequenceNumber() == seq) return null;              // nothing was copied; clipboard untouched, no restore needed
            if (old != null && text != old) Win32Clipboard.WriteText(old);
            return (text != null && text.Trim().Length > 0) ? text : null;
        }

        /// <summary>The paragraph under the mouse pointer, for applications that expose their text through UI Automation.</summary>
        public static string ParagraphUnderPointer()
        {
            try
            {
                POINT pt;
                if (!GetCursorPos(out pt)) return null;
                AutomationElement el = AutomationElement.FromPoint(new System.Windows.Point(pt.X, pt.Y));
                if (el == null) return null;
                object pat;
                if (!el.TryGetCurrentPattern(TextPattern.Pattern, out pat)) return null;
                var tp = (TextPattern)pat;
                TextPatternRange range = tp.RangeFromPoint(new System.Windows.Point(pt.X, pt.Y));
                if (range == null) return null;
                range.ExpandToEnclosingUnit(TextUnit.Paragraph);
                string s = range.GetText(-1);
                return (s != null && s.Trim().Length > 0) ? s : null;
            }
            catch (Exception ex) { EngineLog.Log("UIA point: " + ex.Message); return null; }
        }

        /// <summary>Plain text currently on the clipboard, or null.</summary>
        public static string ClipboardText()
        {
            string s = Win32Clipboard.ReadText();
            return s != null && s.Trim().Length > 0 ? s : null;
        }
    }

    /// <summary>Unicode text in and out of the clipboard through Win32, with retries while another process holds it.</summary>
    public static class Win32Clipboard
    {
        [DllImport("user32.dll")] static extern bool OpenClipboard(IntPtr owner);
        [DllImport("user32.dll")] static extern bool CloseClipboard();
        [DllImport("user32.dll")] static extern bool EmptyClipboard();
        [DllImport("user32.dll")] static extern bool IsClipboardFormatAvailable(uint format);
        [DllImport("user32.dll")] static extern IntPtr GetClipboardData(uint format);
        [DllImport("user32.dll")] static extern IntPtr SetClipboardData(uint format, IntPtr handle);
        [DllImport("kernel32.dll")] static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
        [DllImport("kernel32.dll")] static extern IntPtr GlobalLock(IntPtr handle);
        [DllImport("kernel32.dll")] static extern bool GlobalUnlock(IntPtr handle);
        [DllImport("kernel32.dll")] static extern IntPtr GlobalFree(IntPtr handle);

        const uint CF_UNICODETEXT = 13, GMEM_MOVEABLE_ZEROINIT = 0x0042;

        static bool Open()
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                if (OpenClipboard(IntPtr.Zero)) return true;
                Thread.Sleep(25);
            }
            return false;
        }

        public static string ReadText()
        {
            if (!Open()) return null;
            try
            {
                if (!IsClipboardFormatAvailable(CF_UNICODETEXT)) return null;
                IntPtr handle = GetClipboardData(CF_UNICODETEXT);
                if (handle == IntPtr.Zero) return null;
                IntPtr data = GlobalLock(handle);
                if (data == IntPtr.Zero) return null;
                try { return Marshal.PtrToStringUni(data); }
                finally { GlobalUnlock(handle); }
            }
            catch { return null; }
            finally { CloseClipboard(); }
        }

        public static bool WriteText(string text)
        {
            if (text == null) return false;
            IntPtr handle = GlobalAlloc(GMEM_MOVEABLE_ZEROINIT, (UIntPtr)((text.Length + 1) * 2));
            if (handle == IntPtr.Zero) return false;
            IntPtr data = GlobalLock(handle);
            if (data == IntPtr.Zero) { GlobalFree(handle); return false; }
            Marshal.Copy(text.ToCharArray(), 0, data, text.Length);
            GlobalUnlock(handle);
            if (!Open()) { GlobalFree(handle); return false; }
            try
            {
                EmptyClipboard();
                if (SetClipboardData(CF_UNICODETEXT, handle) == IntPtr.Zero) { GlobalFree(handle); return false; }
                return true;                                                  // the clipboard owns the memory now
            }
            finally { CloseClipboard(); }
        }
    }
}
