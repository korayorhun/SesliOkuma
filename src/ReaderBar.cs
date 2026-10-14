using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using SesliOkuma.Engine;

namespace SesliOkuma
{
    // Floating player: text on top (follows the spoken word with a soft highlight), a centered
    // transport row below, rounded chrome and an edge-to-edge progress hairline.
    public sealed class ReaderBar : Form
    {
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool ReleaseCapture();
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool HideCaret(IntPtr hWnd);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int index);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int index, int value);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
        const int GWL_EXSTYLE = -20, WS_EX_NOACTIVATE = 0x08000000;

        readonly Reader _reader;
        readonly AppSettings _settings;
        readonly Action _rateChanged;
        bool _editable, _userTouchedText;
        public event EventHandler CloseRequested;
        public event Action<string, int> PlayRequested;
        public event Action<string> TranslateRequested;    // translate & read the bar text
        public event Action<FlatButton> VoiceMenuRequested; // open the voice menu anchored to the bar button // play while idle, or paused after touching the text: text + caret offset
        readonly FlatButton _pause = new FlatButton { IconGlyph = true, Borderless = true, Accent = true, Size = new Size(38, 34) };
        readonly FlatButton _back = new FlatButton { IconGlyph = true, Borderless = true, Size = new Size(32, 34), Text = "\uE892" };
        readonly FlatButton _skip = new FlatButton { IconGlyph = true, Borderless = true, Size = new Size(32, 34), Text = "\uE893" };
        readonly FlatButton _speed = new FlatButton { Borderless = true, Size = new Size(48, 34) };
        readonly FlatButton _fontMinus = new FlatButton { IconGlyph = true, Borderless = true, Size = new Size(28, 34), Text = "\uE8E7" };
        readonly FlatButton _fontPlus = new FlatButton { IconGlyph = true, Borderless = true, Size = new Size(28, 34), Text = "\uE8E8" };
        readonly FlatButton _translate = new FlatButton { IconGlyph = true, Borderless = true, Size = new Size(28, 34), Text = "\uE774" };
        readonly FlatButton _voiceBtn = new FlatButton { IconGlyph = true, Borderless = true, Size = new Size(28, 34), Text = "\uE77B" };
        readonly FlatButton _expand = new FlatButton { IconGlyph = true, Borderless = true, Size = new Size(28, 34), Text = "\uE70E" };
        readonly FlatButton _hide = new FlatButton { IconGlyph = true, Borderless = true, Size = new Size(28, 34), Text = "\uE921" };
        readonly FlatButton _close = new FlatButton { IconGlyph = true, Borderless = true, Size = new Size(28, 34), Text = "\uE711" };
        readonly Label _text = new Label { AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.Transparent, AutoEllipsis = true };
        readonly Label _eta = new Label { AutoSize = false, TextAlign = ContentAlignment.MiddleRight, BackColor = Color.Transparent };
        int _queueCount;
        readonly RichTextBox _full = new RichTextBox();
        const int W = 560, RowH = 46, TextMax = 150;
        int _hlStart = -1, _hlLen;
        string _loadedText = "";
        public event EventHandler HideRequested;

        public void SetQueue(int count) { _queueCount = count; if (!IsDisposed) Sync(); }

        public ReaderBar(Reader reader, AppSettings settings, Action rateChanged)
        {
            _reader = reader; _settings = settings; _rateChanged = rateChanged;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Bg; ForeColor = Theme.Text; Font = Theme.Body; ClientSize = new Size(W, RowH + 6); DoubleBuffered = true;
            Text = "SesliOkumaReaderBar";

            _text.Font = Theme.Body; _text.ForeColor = Theme.Text;
            _eta.Font = Theme.Small; _eta.ForeColor = Theme.Muted;
            _full.ReadOnly = true; _full.HideSelection = true; _full.BorderStyle = BorderStyle.None; _full.BackColor = Theme.Bg; _full.ForeColor = Theme.Text;
            _full.Font = Theme.Reading; _full.WordWrap = true; _full.ScrollBars = RichTextBoxScrollBars.None; _full.TabStop = false;
            _full.Visible = false; _full.Cursor = Cursors.Default;
            Controls.AddRange(new Control[] { _back, _pause, _skip, _speed, _fontMinus, _fontPlus, _translate, _voiceBtn, _text, _eta, _expand, _hide, _close, _full });

            _pause.Click += delegate
            {
                if (_reader.Active && !_reader.Paused) { _reader.TogglePause(); return; }
                if (_reader.Active && _reader.Paused && !_userTouchedText) { _reader.TogglePause(); return; }
                string t = _settings.BarExpanded ? _full.Text : _loadedText;
                int off = _settings.BarExpanded && _userTouchedText ? _full.SelectionStart : 0;
                if (_reader.Active) _reader.Stop(false);
                if (t != null && t.Trim().Length > 0 && PlayRequested != null) PlayRequested(t, off);
            };
            _skip.Click += delegate { _reader.Skip(); };
            _back.Click += delegate { _reader.Back(); };
            _close.Click += delegate { _reader.Stop(false); if (CloseRequested != null) CloseRequested(this, EventArgs.Empty); };
            _hide.Click += delegate { if (HideRequested != null) HideRequested(this, EventArgs.Empty); };
            _expand.Click += delegate { _settings.BarExpanded = !_settings.BarExpanded; _settings.Save(); Sync(); };
            _speed.Click += delegate { ShowSpeedMenu(); };
            _fontMinus.Click += delegate { BumpFont(-0.75f); };
            _fontPlus.Click += delegate { BumpFont(0.75f); };
            _voiceBtn.Click += delegate { if (VoiceMenuRequested != null) VoiceMenuRequested(_voiceBtn); };
            _translate.Click += delegate
            {
                string t = _settings.BarExpanded ? _full.Text : _loadedText;
                if (t != null && t.Trim().Length > 0 && TranslateRequested != null) TranslateRequested(t);
            };
            Tips.Set(_back, L.T("Previous")); Tips.Set(_skip, L.T("Next")); Tips.Set(_close, L.T("Stop"));
            Tips.Set(_hide, L.T("HideBar")); Tips.Set(_speed, L.T("SpeedTip"));
            Tips.Set(_fontMinus, L.T("FontSmaller")); Tips.Set(_fontPlus, L.T("FontLarger")); Tips.Set(_translate, L.T("TranslateRead")); Tips.Set(_voiceBtn, L.T("VoiceTip"));

            MouseDown += Drag; _text.MouseDown += Drag;
            _full.GotFocus += delegate { if (!_editable) HideCaret(_full.Handle); };
            _full.MouseDown += delegate { if (!_reader.Active || _reader.Paused) { _userTouchedText = true; EnableEditing(); } };
            _full.KeyDown += delegate { if (_editable) _userTouchedText = true; };
            _full.MouseWheel += delegate(object sn, MouseEventArgs me) { if ((ModifierKeys & Keys.Control) == Keys.Control) BumpFont(me.Delta > 0 ? 0.75f : -0.75f); };
            _reader.Changed += Sync;
            _reader.Position += SyncHighlight;
            Sync();
        }

        void BumpFont(float delta)
        {
            float size = Math.Max(8.25f, Math.Min(16.5f, _settings.FontSize + delta));
            if (size == _settings.FontSize) return;
            _settings.FontSize = size; _settings.Save();
            Theme.SetReadingSize(size);
            _full.Font = Theme.Reading;
            Sync();
        }

        void ShowSpeedMenu()
        {
            var menu = ThemedMenu.Create();
            int[] steps = { -4, -2, 0, 2, 4, 6 };
            foreach (int v in steps)
            {
                string label = (1.0 + v * 0.1).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "\u00d7";
                if (v == 0) label += "  \u00b7  " + L.T("Normal");
                var item = new ToolStripMenuItem(label) { Tag = v, Checked = _settings.Rate == v };
                item.Click += delegate { _settings.Rate = (int)item.Tag; _settings.Save(); if (_rateChanged != null) _rateChanged(); _reader.Restart(); Sync(); };
                menu.Items.Add(item);
            }
            menu.Show(_speed, new Point(0, -6 * 32));
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x08000000 | 0x00000080; cp.ClassStyle |= 0x20000; return cp; }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { int round = 2; DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int)); int dark = Theme.Dark ? 1 : 0; DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int)); } catch { }
        }

        public void Place()
        {
            var wa = Screen.PrimaryScreen.WorkingArea;
            var saved = new Point(_settings.BarX, _settings.BarY);
            bool ok = _settings.BarX > -30000 && _settings.BarX != -1;
            if (ok) { ok = false; foreach (var sc in Screen.AllScreens) if (sc.WorkingArea.Contains(new Rectangle(saved, new Size(60, 30)))) { ok = true; break; } }
            Location = ok ? saved : new Point(wa.Left + (wa.Width - Width) / 2, wa.Bottom - Height - 14);
        }

        protected override void OnMove(EventArgs e)
        {
            base.OnMove(e);
            if (Visible) { _settings.BarX = Location.X; _settings.BarY = Location.Y; }
        }

        static Color Soft(Color a, Color b, float t)
        {
            return Color.FromArgb((int)(a.R * t + b.R * (1 - t)), (int)(a.G * t + b.G * (1 - t)), (int)(a.B * t + b.B * (1 - t)));
        }

        // Explicit click into the text (while idle) hands the window normal activation so the user can edit.
        void EnableEditing()
        {
            if (_editable) return;
            _editable = true;
            SetWindowLong(Handle, GWL_EXSTYLE, GetWindowLong(Handle, GWL_EXSTYLE) & ~WS_EX_NOACTIVATE);
            _full.ReadOnly = false;
            SetForegroundWindow(Handle);
            _full.Focus();
        }

        void DisableEditing()
        {
            if (!_editable) return;
            _editable = false;
            SetWindowLong(Handle, GWL_EXSTYLE, GetWindowLong(Handle, GWL_EXSTYLE) | WS_EX_NOACTIVATE);
            _full.ReadOnly = true;
            HideCaret(_full.Handle);
        }

        void Sync()
        {
            if (IsDisposed) return;
            bool reading = _reader.Active && !_reader.Paused;
            if (reading) { DisableEditing(); _userTouchedText = false; }
            _full.ReadOnly = reading;
            _pause.Glyph = !_reader.Active ? "\uE768" : (_reader.Paused ? "\uE768" : "\uE769");
            Tips.Set(_pause, !_reader.Active ? L.T("Listen") : (_reader.Paused ? L.T("Resume") : L.T("Pause")));
            _speed.Text = (1.0 + _settings.Rate * 0.1).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "\u00d7";
            bool exp = _settings.BarExpanded;
            _expand.Glyph = exp ? "\uE70D" : "\uE70E";
            Tips.Set(_expand, L.T(exp ? "CollapseTip" : "ExpandTip"));
            _text.Visible = !exp;
            _full.Visible = exp;
            _fontMinus.Visible = exp; _fontPlus.Visible = exp; _translate.Visible = exp; _voiceBtn.Visible = exp;
            _eta.Visible = exp;
            UpdateEta();

            int h;
            if (exp)
            {
                if (_loadedText != _reader.FullText)
                {
                    _loadedText = _reader.FullText;
                    _full.Text = _loadedText;
                    _hlStart = -1; _hlLen = 0;
                }
                int th;
                using (var g = CreateGraphics())
                    th = Math.Min(TextMax, Math.Max(30, TextRenderer.MeasureText(g, _loadedText, _full.Font, new Size(W - 44, 2000), TextFormatFlags.WordBreak).Height + 10));
                _full.SetBounds(22, 16, W - 44, th);
                h = 16 + th + 6 + RowH;
                int rowY = h - RowH + 2;
                int cx = W / 2;
                _back.Location = new Point(cx - 16 - 4 - 32, rowY);
                _pause.Location = new Point(cx - 19, rowY);
                _skip.Location = new Point(cx + 19 + 4, rowY);
                _speed.Location = new Point(14, rowY);
                _fontMinus.Location = new Point(66, rowY);
                _fontPlus.Location = new Point(94, rowY);
                _translate.Location = new Point(126, rowY);
                _voiceBtn.Location = new Point(154, rowY);
                _eta.SetBounds(W - 14 - 28 - 4 - 28 - 4 - 28 - 86, rowY + 9, 82, 16);
                _expand.Location = new Point(W - 14 - 28 - 4 - 28 - 4 - 28, rowY);
                _hide.Location = new Point(W - 14 - 28 - 4 - 28, rowY);
                _close.Location = new Point(W - 14 - 28, rowY);
            }
            else
            {
                h = RowH + 6;
                int rowY = 6;
                _back.Location = new Point(10, rowY);
                _pause.Location = new Point(46, rowY);
                _skip.Location = new Point(88, rowY);
                _speed.Location = new Point(124, rowY);
                _text.SetBounds(180, rowY + 6, W - 180 - 104, 22);
                _text.Text = _reader.CurrentSentence;
                _expand.Location = new Point(W - 14 - 28 - 4 - 28 - 4 - 28, rowY);
                _hide.Location = new Point(W - 14 - 28 - 4 - 28, rowY);
                _close.Location = new Point(W - 14 - 28, rowY);
            }
            if (ClientSize.Height != h) ClientSize = new Size(W, h);
            Invalidate();
        }

        // Rough remaining-time from the voice's pace; a queue badge joins it when texts are waiting.
        void UpdateEta()
        {
            string tail = "";
            if (_reader.Active && _loadedText.Length > 200)
            {
                double cps = 15.5 * (1.0 + _settings.Rate * 0.1);
                int secs = (int)((1.0 - _reader.Fraction) * _loadedText.Length / Math.Max(6.0, cps));
                if (secs >= 5) tail = "\u2248" + (secs / 60) + ":" + (secs % 60).ToString("00");
            }
            string badge = _queueCount > 0 ? "+" + _queueCount + (tail.Length > 0 ? "  \u00b7  " : "") : "";
            string text = badge + tail;
            if (_eta.Text != text) _eta.Text = text;
        }

        void SyncHighlight()
        {
            if (IsDisposed || !_reader.Active) return;
            UpdateEta();
            if (_settings.BarExpanded && _full.Visible && _loadedText.Length > 0)
            {
                int start = _reader.CharIndex;
                int len = Math.Max(_reader.WordLength, 1);
                if (start != _hlStart)
                {
                    if (_hlStart >= 0 && _hlStart < _loadedText.Length)
                    {
                        _full.Select(Math.Max(0, _hlStart - 1), Math.Min(_hlLen + 2, _loadedText.Length - Math.Max(0, _hlStart - 1)));
                        _full.SelectionBackColor = _full.BackColor;
                    }
                    if (start < _loadedText.Length)
                    {
                        len = Math.Min(len, _loadedText.Length - start);
                        _full.Select(start, len);
                        _full.SelectionBackColor = Soft(Theme.Accent, Theme.Bg, 0.22f);
                        _hlStart = start; _hlLen = len;
                        // Scroll only when the word leaves the comfortable band; keeps lines from hopping.
                        var pt = _full.GetPositionFromCharIndex(start);
                        if (pt.Y < 4 || pt.Y > _full.Height - 26) { _full.Select(start, 0); _full.ScrollToCaret(); }
                        else _full.Select(start, 0);
                        if (!_editable) HideCaret(_full.Handle);
                    }
                }
            }
            else if (!_settings.BarExpanded) _text.Text = _reader.CurrentSentence;
            Invalidate(new Rectangle(0, Height - 4, Width, 4));
        }

        void Drag(object s, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            ReleaseCapture();
            SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Theme.Prepare(e.Graphics);
            using (var path = Theme.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 8))
            using (var pen = new Pen(Theme.Border)) e.Graphics.DrawPath(pen, path);
            float frac = (float)_reader.Fraction;
            using (var b = new SolidBrush(Theme.Track)) e.Graphics.FillRectangle(b, 1, Height - 3, Width - 2, 2);
            using (var b = new SolidBrush(Theme.Accent)) e.Graphics.FillRectangle(b, 1, Height - 3, (Width - 2) * frac, 2);
        }
    }
}