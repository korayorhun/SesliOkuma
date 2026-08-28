using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SesliOkuma
{
    public abstract class SmoothControl : Control
    {
        protected bool Hover;
        protected SmoothControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
        }
        protected override void OnMouseEnter(EventArgs e) { Hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { Hover = false; Invalidate(); base.OnMouseLeave(e); }
    }

    public sealed class FlatButton : SmoothControl
    {
        public bool Primary;
        public bool IconGlyph;
        public string Tip = "";
        public FlatButton() { Size = new Size(44, 44); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Prepare(e.Graphics);
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            Color fill = Primary ? (Hover ? Theme.AccentHover : Theme.Accent) : (Hover ? Theme.CardHover : Theme.Card);
            using (var p = Theme.RoundRect(r, 10))
            {
                using (var b = new SolidBrush(fill)) e.Graphics.FillPath(b, p);
                if (!Primary) using (var pen = new Pen(Theme.Border)) e.Graphics.DrawPath(pen, p);
            }
            Color fg = Primary ? Theme.AccentText : (Hover ? Theme.Text : Theme.Muted);
            TextRenderer.DrawText(e.Graphics, Text, IconGlyph ? Theme.Icon : Theme.Body, ClientRectangle, fg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    public sealed class ToggleSwitch : SmoothControl
    {
        bool _checked;
        public event EventHandler CheckedChanged;
        public bool Checked
        {
            get { return _checked; }
            set { if (_checked == value) return; _checked = value; Invalidate(); if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty); }
        }
        public ToggleSwitch() { Size = new Size(44, 24); }
        protected override void OnMouseClick(MouseEventArgs e) { Checked = !Checked; base.OnMouseClick(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Prepare(e.Graphics);
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            using (var p = Theme.RoundRect(r, Height / 2f))
            using (var b = new SolidBrush(_checked ? (Hover ? Theme.AccentHover : Theme.Accent) : (Hover ? Theme.CardHover : Theme.Track)))
                e.Graphics.FillPath(b, p);
            float d = Height - 8;
            float x = _checked ? Width - d - 4 : 4;
            using (var b = new SolidBrush(_checked ? Theme.AccentText : Theme.Muted)) e.Graphics.FillEllipse(b, x, 4, d, d);
        }
    }

    public sealed class Slider : SmoothControl
    {
        public int Minimum = -5, Maximum = 5;
        int _value;
        bool _drag;
        public event EventHandler ValueChanged;
        public int Value
        {
            get { return _value; }
            set { value = Math.Max(Minimum, Math.Min(Maximum, value)); if (_value == value) return; _value = value; Invalidate(); if (ValueChanged != null) ValueChanged(this, EventArgs.Empty); }
        }
        public Slider() { Height = 28; }

        const float Pad = 10f;
        float XFromValue(int v) { return Pad + (Width - 2 * Pad) * (v - Minimum) / (float)(Maximum - Minimum); }
        int ValueFromX(float x) { return Minimum + (int)Math.Round((x - Pad) / (Width - 2 * Pad) * (Maximum - Minimum)); }

        protected override void OnMouseDown(MouseEventArgs e) { _drag = true; Value = ValueFromX(e.X); base.OnMouseDown(e); }
        protected override void OnMouseMove(MouseEventArgs e) { if (_drag) Value = ValueFromX(e.X); base.OnMouseMove(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _drag = false; base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Prepare(e.Graphics);
            float cy = Height / 2f, x0 = Pad, x1 = Width - Pad, xv = XFromValue(_value), xz = XFromValue(0);
            using (var pen = new Pen(Theme.Track, 4f) { StartCap = LineCap.Round, EndCap = LineCap.Round }) e.Graphics.DrawLine(pen, x0, cy, x1, cy);
            using (var pen = new Pen(Theme.Accent, 4f) { StartCap = LineCap.Round, EndCap = LineCap.Round }) e.Graphics.DrawLine(pen, Math.Min(xz, xv), cy, Math.Max(xz, xv), cy);
            for (int v = Minimum; v <= Maximum; v++)
                using (var b = new SolidBrush(Theme.Muted)) e.Graphics.FillEllipse(b, XFromValue(v) - 1, cy - 1, 2, 2);
            float r = Hover || _drag ? 8f : 7f;
            using (var b = new SolidBrush(Theme.Accent)) e.Graphics.FillEllipse(b, xv - r, cy - r, 2 * r, 2 * r);
            using (var b = new SolidBrush(Theme.AccentText)) e.Graphics.FillEllipse(b, xv - 3, cy - 3, 6, 6);
        }
    }

    // Small uppercase caption; optionally carries a clickable accent value with a chevron (e.g. "PRIMARY LANGUAGE · Türkçe ˅").
    public sealed class CaptionLink : SmoothControl
    {
        public string Caption = "";
        public string Value = "";
        public CaptionLink() { Height = 18; Cursor = Cursors.Default; }
        protected override void OnMouseEnter(EventArgs e) { Cursor = Value.Length > 0 ? Cursors.Hand : Cursors.Default; base.OnMouseEnter(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Prepare(e.Graphics);
            var f = TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter;
            int x = 0;
            TextRenderer.DrawText(e.Graphics, Caption, Theme.Caption, new Rectangle(x, 0, Width, Height), Theme.Muted, f);
            if (Value.Length == 0) return;
            x += TextRenderer.MeasureText(e.Graphics, Caption, Theme.Caption, new Size(1000, Height), f).Width;
            string sep = "  ·  ";
            TextRenderer.DrawText(e.Graphics, sep, Theme.Caption, new Rectangle(x, 0, Width - x, Height), Theme.Muted, f);
            x += TextRenderer.MeasureText(e.Graphics, sep, Theme.Caption, new Size(1000, Height), f).Width;
            string val = Value.ToUpperInvariant();
            Color ac = Hover ? Theme.AccentHover : Theme.Accent;
            TextRenderer.DrawText(e.Graphics, val, Theme.Caption, new Rectangle(x, 0, Width - x, Height), ac, f);
            x += TextRenderer.MeasureText(e.Graphics, val, Theme.Caption, new Size(1000, Height), f).Width + 4;
            using (var small = new Font(Theme.Icon.FontFamily, 7f))
                TextRenderer.DrawText(e.Graphics, "\uE70D", small, new Rectangle(x, 1, 14, Height), ac, f);
        }
    }

    // A rounded "select" field; clicking opens a themed dropdown of voices grouped by language.
    public sealed class VoicePicker : SmoothControl
    {
        VoiceInfo _selected;
        IList<VoiceInfo> _voices;
        public Func<VoiceInfo, bool> Filter;
        public string PrimaryLang = "";
        public event EventHandler SelectionChanged;
        public bool MenuOpen;

        public VoiceInfo Selected { get { return _selected; } set { _selected = value; Invalidate(); } }
        public VoicePicker() { Height = 48; }
        public void SetVoices(IList<VoiceInfo> voices) { _voices = voices; }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (_voices == null || _voices.Count == 0) return;
            var menu = ThemedMenu.Create();
            var ordered = new List<VoiceInfo>();
            foreach (var v in _voices) if (Filter == null || Filter(v)) ordered.Add(v);
            string primary = PrimaryLang;
            ordered.Sort(delegate (VoiceInfo a, VoiceInfo b)
            {
                int la = a.Lang2 == primary ? 0 : 1, lb = b.Lang2 == primary ? 0 : 1;
                if (la != lb) return la - lb;
                int c = string.Compare(a.LanguageName, b.LanguageName, StringComparison.CurrentCultureIgnoreCase);
                if (c != 0) return c;
                if (a.IsNatural != b.IsNatural) return a.IsNatural ? -1 : 1;
                if (a.IsMultilingual != b.IsMultilingual) return a.IsMultilingual ? 1 : -1;
                return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
            });
            string lastLang = null;
            foreach (var v in ordered)
            {
                if (v.LanguageName != lastLang)
                {
                    if (lastLang != null) menu.Items.Add(new ToolStripSeparator());
                    menu.Items.Add(new ToolStripMenuItem(v.LanguageName.ToUpperInvariant()) { Enabled = false, Font = Theme.Caption });
                    lastLang = v.LanguageName;
                }
                string tag = v.IsMultilingual ? "  ·  " + L.T("Multilingual") : (v.IsNatural ? "" : "  ·  " + L.T("Classic"));
                var item = new ToolStripMenuItem(v.ShortName + tag) { Tag = v, Checked = _selected != null && v.Id == _selected.Id };
                item.Click += delegate { Selected = (VoiceInfo)item.Tag; if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty); };
                menu.Items.Add(item);
            }
            menu.Opened += delegate { MenuOpen = true; };
            menu.Closed += delegate { MenuOpen = false; };
            menu.Show(this, new Point(0, Height + 4));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Prepare(e.Graphics);
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            using (var p = Theme.RoundRect(r, 10))
            {
                using (var b = new SolidBrush(Hover ? Theme.CardHover : Theme.Card)) e.Graphics.FillPath(b, p);
                using (var pen = new Pen(Hover ? Theme.Accent : Theme.Border)) e.Graphics.DrawPath(pen, p);
            }
            string name = _selected != null ? _selected.ShortName : L.T("NoVoice");
            string sub;
            if (_selected == null) sub = L.T("PickVoice");
            else
            {
                sub = _selected.LanguageName;
                if (_selected.IsMultilingual) sub += "  ·  " + L.T("Multilingual");
                else sub += "  ·  " + (_selected.IsNatural ? L.T("Natural") : L.T("Classic"));
            }
            var flags = TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;
            TextRenderer.DrawText(e.Graphics, name, Theme.Body, new Rectangle(14, 7, Width - 50, 20), Theme.Text, flags);
            TextRenderer.DrawText(e.Graphics, sub, Theme.Small, new Rectangle(14, 26, Width - 50, 16), Theme.Muted, flags);
            TextRenderer.DrawText(e.Graphics, "\uE70D", Theme.Icon, new Rectangle(Width - 34, 0, 24, Height), Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    public static class ThemedMenu
    {
        public static ContextMenuStrip Create()
        {
            return new ContextMenuStrip { Renderer = new MenuRenderer(), ShowImageMargin = false, Font = Theme.Body, BackColor = Theme.Card, ForeColor = Theme.Text };
        }
    }

    public sealed class MenuRenderer : ToolStripProfessionalRenderer
    {
        public MenuRenderer() : base(new MenuColors()) { RoundedEdges = false; }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (var b = new SolidBrush(Theme.Card)) e.Graphics.FillRectangle(b, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using (var pen = new Pen(Theme.Border))
                e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            Theme.Prepare(e.Graphics);
            var r = new RectangleF(4, 0.5f, e.Item.Width - 8, e.Item.Height - 1);
            using (var p = Theme.RoundRect(r, 6))
            using (var b = new SolidBrush(Theme.CardHover)) e.Graphics.FillPath(b, p);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            var item = e.Item as ToolStripMenuItem;
            bool isChecked = item != null && item.Checked;
            e.TextColor = !e.Item.Enabled ? Theme.Muted : (isChecked ? Theme.Accent : Theme.Text);
            var r = e.TextRectangle; r.X += 8; e.TextRectangle = r;
            base.OnRenderItemText(e);
            if (isChecked)
                TextRenderer.DrawText(e.Graphics, "\uE73E", Theme.Icon, new Rectangle(e.Item.Width - 30, 0, 20, e.Item.Height), Theme.Accent,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) { }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using (var pen = new Pen(Theme.Border)) e.Graphics.DrawLine(pen, 10, e.Item.Height / 2, e.Item.Width - 10, e.Item.Height / 2);
        }

        sealed class MenuColors : ProfessionalColorTable
        {
            public override Color MenuBorder { get { return Theme.Border; } }
            public override Color MenuItemBorder { get { return Color.Transparent; } }
            public override Color ToolStripDropDownBackground { get { return Theme.Card; } }
            public override Color ImageMarginGradientBegin { get { return Theme.Card; } }
            public override Color ImageMarginGradientMiddle { get { return Theme.Card; } }
            public override Color ImageMarginGradientEnd { get { return Theme.Card; } }
        }
    }

    // Accent card with title, one-line text and a primary action; can switch to a progress state.
    public sealed class ActionCard : SmoothControl
    {
        readonly FlatButton _action = new FlatButton { Primary = true, Size = new Size(96, 34) };
        readonly FlatButton _dismiss = new FlatButton { Text = "\uE711", IconGlyph = true, Size = new Size(26, 26) };
        public string Title = "", Text2 = "", Note = "";
        public bool ShowDismiss;
        int _progress = -1;
        string _progressText = "";
        public bool Busy { get { return _progress >= 0; } }
        public event EventHandler ActionClicked, DismissClicked, BodyClicked;

        public ActionCard()
        {
            Cursor = Cursors.Default;
            Controls.Add(_action); Controls.Add(_dismiss);
            _action.Click += delegate { if (ActionClicked != null) ActionClicked(this, EventArgs.Empty); };
            _dismiss.Click += delegate { if (DismissClicked != null) DismissClicked(this, EventArgs.Empty); };
        }

        public string ActionText { get { return _action.Text; } set { _action.Text = value; _action.Invalidate(); } }

        public void SetIdle() { _progress = -1; _action.Visible = true; _dismiss.Visible = ShowDismiss; Invalidate(); }
        public void SetProgress(int percent, string text) { _progress = Math.Max(0, percent); _progressText = text; _action.Visible = false; _dismiss.Visible = false; Invalidate(); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (_progress < 0 && e.X < Width - 150 && BodyClicked != null) BodyClicked(this, EventArgs.Empty);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            int right = ShowDismiss ? 40 : 12;
            _action.Location = new Point(Width - _action.Width - right, (Height - _action.Height) / 2);
            _dismiss.Location = new Point(Width - 26 - 8, (Height - 26) / 2);
            _dismiss.Visible = ShowDismiss && _progress < 0;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Prepare(e.Graphics);
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            using (var p = Theme.RoundRect(r, 10))
            {
                using (var b = new SolidBrush(Theme.AccentSoft)) e.Graphics.FillPath(b, p);
                using (var pen = new Pen(Theme.Accent)) e.Graphics.DrawPath(pen, p);
            }
            bool busy = _progress >= 0;
            string head = busy ? _progressText : Title;
            string sub = busy ? Note : Text2;
            int textW = busy ? Width - 28 : Width - (ShowDismiss ? 160 : 130);
            var flags = TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;
            TextRenderer.DrawText(e.Graphics, head, Theme.Body, new Rectangle(14, 10, textW, 20), Theme.Text, flags);
            TextRenderer.DrawText(e.Graphics, sub, Theme.Small, new Rectangle(14, 31, textW, 16), busy ? Theme.Muted : Theme.Accent, flags);
            if (busy)
            {
                var track = new RectangleF(14, Height - 8, Width - 28, 3);
                using (var b = new SolidBrush(Theme.Track)) e.Graphics.FillRectangle(b, track);
                using (var b = new SolidBrush(Theme.Accent)) e.Graphics.FillRectangle(b, track.X, track.Y, track.Width * Math.Min(100, _progress) / 100f, track.Height);
            }
        }
    }
}
