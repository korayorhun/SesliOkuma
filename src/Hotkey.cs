using System;
using System.Drawing;
using System.Windows.Forms;

namespace SesliOkuma
{
    // "Ctrl+Alt+S" <-> (modifier flags, virtual key) conversions for RegisterHotKey.
    public struct HotkeyDef
    {
        public uint Modifiers;   // MOD_ALT=1, MOD_CONTROL=2, MOD_SHIFT=4, MOD_WIN=8
        public Keys Key;

        public bool IsValid { get { return Modifiers != 0 && Key != Keys.None; } }

        public static HotkeyDef Default { get { return new HotkeyDef { Modifiers = 3, Key = Keys.S }; } }

        public override string ToString()
        {
            string s = "";
            if ((Modifiers & 2) != 0) s += "Ctrl+";
            if ((Modifiers & 1) != 0) s += "Alt+";
            if ((Modifiers & 4) != 0) s += "Shift+";
            if ((Modifiers & 8) != 0) s += "Win+";
            return s + KeyName(Key);
        }

        public static string KeyName(Keys k)
        {
            if (k >= Keys.D0 && k <= Keys.D9) return ((char)('0' + (k - Keys.D0))).ToString();
            if (k >= Keys.NumPad0 && k <= Keys.NumPad9) return "Num" + (k - Keys.NumPad0);
            switch (k)
            {
                case Keys.Oemtilde: return "`";
                case Keys.OemMinus: return "-";
                case Keys.Oemplus: return "=";
                case Keys.OemOpenBrackets: return "[";
                case Keys.OemCloseBrackets: return "]";
                case Keys.OemPipe: return "\\";
                case Keys.OemSemicolon: return ";";
                case Keys.OemQuotes: return "'";
                case Keys.Oemcomma: return ",";
                case Keys.OemPeriod: return ".";
                case Keys.OemQuestion: return "/";
                case Keys.Space: return "Space";
                case Keys.PageUp: return "PageUp";
                case Keys.Next: return "PageDown";
            }
            return k.ToString();
        }

        public static bool TryParse(string text, out HotkeyDef def)
        {
            def = new HotkeyDef();
            if (string.IsNullOrEmpty(text)) return false;
            foreach (string raw in text.Split('+'))
            {
                string part = raw.Trim();
                switch (part.ToLowerInvariant())
                {
                    case "ctrl": case "control": def.Modifiers |= 2; continue;
                    case "alt": def.Modifiers |= 1; continue;
                    case "shift": def.Modifiers |= 4; continue;
                    case "win": def.Modifiers |= 8; continue;
                }
                Keys k;
                if (part.Length == 1 && char.IsDigit(part[0])) k = Keys.D0 + (part[0] - '0');
                else if (part.StartsWith("Num") && part.Length == 4 && char.IsDigit(part[3])) k = Keys.NumPad0 + (part[3] - '0');
                else if (!Enum.TryParse<Keys>(part, true, out k)) return false;
                def.Key = k;
            }
            return def.IsValid;
        }

        public static bool IsModifier(Keys k)
        {
            return k == Keys.ControlKey || k == Keys.LControlKey || k == Keys.RControlKey ||
                   k == Keys.Menu || k == Keys.LMenu || k == Keys.RMenu ||
                   k == Keys.ShiftKey || k == Keys.LShiftKey || k == Keys.RShiftKey ||
                   k == Keys.LWin || k == Keys.RWin;
        }
    }

    // Rounded field showing the current shortcut; click, then press a combination to change it.
    public sealed class HotkeyBox : SmoothControl
    {
        HotkeyDef _value = HotkeyDef.Default;
        bool _capturing;
        public event Action<HotkeyDef> HotkeyChosen;
        public event EventHandler NeedModifier;

        public string HintKey = "HotkeyHint";
        public HotkeyBox() { Height = 48; SetStyle(ControlStyles.Selectable, true); TabStop = true; }

        public HotkeyDef Value { get { return _value; } set { _value = value; Invalidate(); } }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            _capturing = true; Focus(); Invalidate();
        }

        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); _capturing = false; Invalidate(); }

        protected override bool IsInputKey(Keys keyData) { return true; }
        protected override bool ProcessDialogKey(Keys keyData) { return _capturing ? false : base.ProcessDialogKey(keyData); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (!_capturing) return;
            e.Handled = true; e.SuppressKeyPress = true;
            if (e.KeyCode == Keys.Escape) { _capturing = false; Invalidate(); Parent.Focus(); return; }
            if (HotkeyDef.IsModifier(e.KeyCode)) { Invalidate(); return; }
            uint mods = 0;
            if (e.Control) mods |= 2;
            if (e.Alt) mods |= 1;
            if (e.Shift) mods |= 4;
            if ((ModifierKeys & Keys.LWin) != 0 || IsKeyDown(Keys.LWin) || IsKeyDown(Keys.RWin)) mods |= 8;
            if (mods == 0) { if (NeedModifier != null) NeedModifier(this, EventArgs.Empty); return; }
            var def = new HotkeyDef { Modifiers = mods, Key = e.KeyCode };
            _capturing = false; Invalidate();
            if (HotkeyChosen != null) HotkeyChosen(def);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
        static bool IsKeyDown(Keys k) { return (GetAsyncKeyState((int)k) & 0x8000) != 0; }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Prepare(e.Graphics);
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            using (var p = Theme.RoundRect(r, 10))
            {
                using (var b = new SolidBrush(_capturing ? Theme.AccentSoft : (Hover ? Theme.CardHover : Theme.Card))) e.Graphics.FillPath(b, p);
                using (var pen = new Pen(_capturing || Hover ? Theme.Accent : Theme.Border)) e.Graphics.DrawPath(pen, p);
            }
            string main = _capturing ? L.T("HotkeyCapture") : _value.ToString();
            string sub = _capturing ? "" : L.T(HintKey);
            var flags = TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;
            if (_capturing)
                TextRenderer.DrawText(e.Graphics, main, Theme.Body, new Rectangle(14, 0, Width - 28, Height), Theme.Accent, flags | TextFormatFlags.VerticalCenter);
            else
            {
                TextRenderer.DrawText(e.Graphics, main, Theme.Mono, new Rectangle(14, 7, Width - 60, 20), Theme.Text, flags);
                TextRenderer.DrawText(e.Graphics, sub, Theme.Small, new Rectangle(14, 26, Width - 60, 16), Theme.Muted, flags);
                TextRenderer.DrawText(e.Graphics, "\uE70F", Theme.Icon, new Rectangle(Width - 34, 0, 24, Height), Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }
    }
}
