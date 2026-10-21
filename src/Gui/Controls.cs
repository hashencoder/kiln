using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WebHut
{
    /// <summary>Base for the owner-drawn controls: double-buffered, transparent corners, hover tracking.</summary>
    public abstract class Painted : Control
    {
        protected bool Hover;

        protected Painted()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        protected override void OnMouseEnter(EventArgs e) { Hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { Hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        /// <summary>Scale a 96-dpi length to the screen DPI.</summary>
        protected static int S(float px) { return Ui.Px(px); }

        protected static void Smooth(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        }
    }

    public enum ButtonKind { Primary, Outline, Ghost }

    /// <summary>Rounded button with an optional icon on the left.</summary>
    public class IconButton : Painted, IButtonControl
    {
        string _icon;
        ButtonKind _kind;
        public int MinWidth, MinHeight;
        public Color? IconColor;

        public IconButton(string text, string icon, ButtonKind kind)
        {
            Text = text; _icon = icon; _kind = kind;
            Font = Ui.Bold;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
            Margin = new Padding(0, 0, 8, 0);
            Fit();
        }

        public string IconName { get { return _icon; } set { _icon = value; Fit(); Invalidate(); } }
        public ButtonKind Kind { get { return _kind; } set { _kind = value; Fit(); Invalidate(); } }

        public void Set(string text, string icon, ButtonKind kind)
        {
            Text = text; _icon = icon; _kind = kind;
            Fit(); Invalidate();
        }

        protected override void OnTextChanged(EventArgs e) { Fit(); base.OnTextChanged(e); }
        protected override void OnFontChanged(EventArgs e) { Fit(); base.OnFontChanged(e); }

        void Fit()
        {
            int h = Math.Max(Math.Max(S(34), MinHeight), TextRenderer.MeasureText("Ag", Font).Height + S(14));
            if (string.IsNullOrEmpty(Text)) { Size = new Size(Math.Max(h, MinWidth), h); return; } // icon-only: square
            var t = TextRenderer.MeasureText(Text, Font);
            int icon = _icon == null ? 0 : S(16) + S(8);
            int w = S(13) + icon + t.Width + S(13);
            Size = new Size(Math.Max(w, MinWidth), h);
        }

        public void SetMin(int w, int h) { MinWidth = w; MinHeight = h; Fit(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Smooth(g);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Color fill, border, ink;
            if (_kind == ButtonKind.Primary)
            {
                fill = Hover && Enabled ? Ui.AccentHover : Ui.Accent; border = fill; ink = Color.White;
            }
            else if (_kind == ButtonKind.Outline)
            {
                fill = Hover && Enabled ? Ui.Hover : Ui.Card; border = Hover && Enabled ? Ui.AccentSoftLine : Ui.Line; ink = Ui.Ink;
            }
            else
            {
                fill = Hover && Enabled ? Ui.Hover : Color.Transparent; border = fill; ink = Ui.Ink;
            }
            if (!Enabled) { fill = Blend(fill, 0.55f); border = Blend(border, 0.55f); ink = Blend(ink, 0.45f); }
            using (var p = Ui.Rounded(r, S(7)))
            {
                if (fill.A > 0) using (var b = new SolidBrush(fill)) g.FillPath(b, p);
                if (border.A > 0) using (var pen = new Pen(border)) g.DrawPath(pen, p);
            }
            var ts = string.IsNullOrEmpty(Text) ? Size.Empty : TextRenderer.MeasureText(Text, Font);
            int iconSize = S(16), gap = _icon == null || ts.Width == 0 ? 0 : S(8);
            int total = (_icon == null ? 0 : iconSize) + gap + ts.Width;
            int x = (Width - total) / 2;
            if (_icon != null)
            {
                var ic = IconColor ?? (_kind == ButtonKind.Primary ? Color.White : Ui.AccentInk);
                if (!Enabled) ic = Blend(ic, 0.45f);
                Icons.Draw(g, _icon, new RectangleF(x, (Height - iconSize) / 2f, iconSize, iconSize), ic);
                x += iconSize + gap;
            }
            TextRenderer.DrawText(g, Text, Font, new Rectangle(x, 0, ts.Width + 2, Height), ink,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            if (Focused && ShowFocusCues)
                using (var p = Ui.Rounded(new RectangleF(2.5f, 2.5f, Width - 5.5f, Height - 5.5f), S(5)))
                using (var pen = new Pen(Color.FromArgb(120, Ui.Accent)) { DashStyle = DashStyle.Dot }) g.DrawPath(pen, p);
        }

        static Color Blend(Color c, float alpha) { return c.A == 0 ? c : Color.FromArgb((int)(255 * alpha), c); }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) PerformClick();
            base.OnKeyUp(e);
        }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        // IButtonControl, so it can be a form's AcceptButton
        public DialogResult DialogResult { get; set; }
        public void NotifyDefault(bool value) { }
        public void PerformClick() { if (Enabled) OnClick(EventArgs.Empty); }
        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (DialogResult != DialogResult.None) { var f = FindForm(); if (f != null) f.DialogResult = DialogResult; }
        }
    }

    /// <summary>White rounded card on the page background. Put children on Ui.Card.</summary>
    public class Card : Panel
    {
        public int Radius = 12;
        public Card()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Ui.Bg;
            Padding = new Padding(Ui.Px(20), Ui.Px(16), Ui.Px(20), Ui.Px(18));
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
        }
        public Color Fill = Ui.Card;
        public Color Border = Ui.Line;
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = Ui.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Radius))
            {
                using (var b = new SolidBrush(Fill)) g.FillPath(b, p);
                if (Border.A > 0) using (var pen = new Pen(Border)) g.DrawPath(pen, p);
            }
        }
    }

    /// <summary>Sidebar entry: icon + label; active entries get a tinted background and an accent bar.</summary>
    public class NavItem : Painted
    {
        readonly string _icon;
        public bool Active;
        public NavItem(string text, string icon)
        {
            Text = text; _icon = icon;
            Font = Ui.Nav;
            Cursor = Cursors.Hand;
            Height = S(42);
            Margin = new Padding(0, 0, 0, 4);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Smooth(g);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            if (Active || Hover)
                using (var p = Ui.Rounded(r, S(8)))
                using (var b = new SolidBrush(Active ? Ui.AccentSoft : Ui.Hover)) g.FillPath(b, p);
            if (Active)
                using (var b = new SolidBrush(Ui.AccentInk)) g.FillRectangle(b, 0, S(8), S(3), Height - S(16));
            int ic = S(20);
            Icons.Draw(g, _icon, new RectangleF(S(16), (Height - ic) / 2f, ic, ic), Active ? Ui.AccentInk : Ui.NavInk);
            TextRenderer.DrawText(g, Text, Active ? Ui.NavBold : Font, new Rectangle(S(50), 0, Width - S(54), Height),
                Active ? Ui.AccentInk : Ui.Ink, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    public enum PillState { Running, Stopped, Busy, Partial, Error, Info }

    /// <summary>Status badge: tinted rounded pill with a dot.</summary>
    public class Pill : Painted
    {
        PillState _state = PillState.Stopped;
        readonly Timer _spin = new Timer { Interval = 60 };
        float _angle;
        public bool Fit;   // size to the text instead of a fixed width
        public Pill()
        {
            Font = Ui.Nav; Size = new Size(S(104), S(30));
            _spin.Tick += (s, e) => { _angle = (_angle + 24) % 360; Invalidate(); };
        }
        public PillState State { get { return _state; } }
        public void Set(PillState state, string text)
        {
            if (_state == state && Text == text) return;
            _state = state; Text = text;
            if (state == PillState.Busy) _spin.Start(); else _spin.Stop();
            if (Fit)
            {
                var ts = TextRenderer.MeasureText(Text ?? "", Font);
                Width = S(11) + S(10) + S(8) + ts.Width + S(13);
            }
            Invalidate();
        }
        protected override void Dispose(bool disposing) { if (disposing) _spin.Dispose(); base.Dispose(disposing); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Smooth(g);
            Color fg, bg;
            switch (_state)
            {
                case PillState.Running: fg = Ui.Green; bg = Ui.GreenSoft; break;
                case PillState.Busy: case PillState.Partial: fg = Ui.Amber; bg = Ui.AmberSoft; break;
                case PillState.Error: fg = Ui.Red; bg = Ui.RedSoft; break;
                case PillState.Info: fg = Ui.Info; bg = Ui.Mix(Ui.Info, Ui.Card, Ui.IsDark ? 0.22f : 0.12f); break;
                default: fg = Ui.Grey; bg = Ui.GreySoft; break;
            }
            var ts = TextRenderer.MeasureText(Text ?? "", Font);
            int dot = S(9), pad = S(11), gap = S(8);
            int w = Math.Min(Width - 1, pad + dot + gap + ts.Width + pad);
            using (var p = Ui.Rounded(new RectangleF(0.5f, 0.5f, w, Height - 1.5f), (Height - 1.5f) / 2f))
            using (var b = new SolidBrush(bg)) g.FillPath(b, p);
            if (_state == PillState.Busy)
            {
                // spinner: a 3/4 ring rotating while the service starts or stops
                float d = S(10);
                using (var pen = new Pen(fg, Math.Max(1.6f, S(2))) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round })
                    g.DrawArc(pen, pad - 0.5f, (Height - d) / 2f, d, d, _angle, 270);
            }
            else using (var b = new SolidBrush(fg)) g.FillEllipse(b, pad, (Height - dot) / 2f, dot, dot);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(pad + dot + gap, 0, ts.Width + 2, Height), fg,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>Quick-action tile: icon on the left, title and a muted subtitle on the right.</summary>
    public class Tile : Painted
    {
        readonly string _icon, _sub;
        public Tile(string title, string sub, string icon)
        {
            Text = title; _sub = sub; _icon = icon;
            Font = Ui.Bold;
            Cursor = Cursors.Hand;
            Size = new Size(S(160), S(62));
            Margin = new Padding(0, 0, S(10), S(10));
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Smooth(g);
            using (var p = Ui.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), S(10)))
            {
                using (var b = new SolidBrush(Hover ? Ui.TileHover : Ui.Card)) g.FillPath(b, p);
                using (var pen = new Pen(Hover ? Ui.AccentSoftLine : Ui.Line)) g.DrawPath(pen, p);
            }
            int ic = S(20), x = S(14);
            Icons.Draw(g, _icon, new RectangleF(x, (Height - ic) / 2f, ic, ic), Ui.AccentInk);
            int tx = x + ic + S(12), tw = Width - tx - S(8);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(tx, S(11), tw, S(20)), Ui.Ink,
                TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, _sub, Ui.Small, new Rectangle(tx, S(33), tw, S(18)), Ui.Muted,
                TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>Segmented filter: a row of small options, one selected.</summary>
    public class Segmented : Painted
    {
        readonly string[] _items;
        int _sel, _hot = -1;
        public event EventHandler SelectedChanged;
        public Segmented(params string[] items)
        {
            _items = items; Font = Ui.Small; Cursor = Cursors.Hand;
            int w = S(8);
            foreach (var i in items) w += TextRenderer.MeasureText(i, Font).Width + S(20);
            Size = new Size(w, S(32));
            Margin = new Padding(0, 0, S(12), 0);
        }
        public int SelectedIndex
        {
            get { return _sel; }
            set { if (_sel == value) return; _sel = value; Invalidate(); var h = SelectedChanged; if (h != null) h(this, EventArgs.Empty); }
        }
        Rectangle Seg(int i)
        {
            int x = S(4);
            for (int k = 0; k < i; k++) x += TextRenderer.MeasureText(_items[k], Font).Width + S(20);
            return new Rectangle(x, S(4), TextRenderer.MeasureText(_items[i], Font).Width + S(20), Height - S(8));
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            int hot = -1;
            for (int i = 0; i < _items.Length; i++) if (Seg(i).Contains(e.Location)) hot = i;
            if (hot != _hot) { _hot = hot; Invalidate(); }
            base.OnMouseMove(e);
        }
        protected override void OnMouseLeave(EventArgs e) { _hot = -1; base.OnMouseLeave(e); }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            for (int i = 0; i < _items.Length; i++) if (Seg(i).Contains(e.Location)) SelectedIndex = i;
            base.OnMouseClick(e);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Smooth(g);
            using (var p = Ui.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), S(8)))
            using (var b = new SolidBrush(Ui.Surface)) g.FillPath(b, p);
            for (int i = 0; i < _items.Length; i++)
            {
                var r = Seg(i);
                if (i == _sel || i == _hot)
                    using (var p = Ui.Rounded(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), S(6)))
                    {
                        using (var b = new SolidBrush(i == _sel ? Ui.Card : Ui.Hover)) g.FillPath(b, p);
                        if (i == _sel) using (var pen = new Pen(Ui.Line)) g.DrawPath(pen, p);
                    }
                TextRenderer.DrawText(g, _items[i], i == _sel ? Ui.SmallBold : Font, r, i == _sel ? Ui.Ink : Ui.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            }
        }
    }

    /// <summary>Rounded dropdown in the WebHut style; opens a themed menu.</summary>
    public class Dropdown : Painted
    {
        public readonly System.Collections.Generic.List<string> Items = new System.Collections.Generic.List<string>();
        int _sel = -1;
        public event EventHandler SelectedIndexChanged;
        public Dropdown(int width)
        {
            Font = Ui.Base; Cursor = Cursors.Hand; TabStop = true;
            SetStyle(ControlStyles.Selectable, true);
            Size = new Size(width, S(38));
        }
        public int SelectedIndex
        {
            get { return _sel; }
            set { if (value == _sel) return; _sel = value; Invalidate(); var h = SelectedIndexChanged; if (h != null) h(this, EventArgs.Empty); }
        }
        public string SelectedItem
        {
            get { return _sel >= 0 && _sel < Items.Count ? Items[_sel] : null; }
            set { SelectedIndex = value == null ? -1 : Items.IndexOf(value); }
        }
        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (Items.Count == 0) return;
            var list = new System.Collections.Generic.List<PopupItem>();
            for (int i = 0; i < Items.Count; i++)
            {
                int idx = i;
                list.Add(new PopupItem { Title = Items[i], Checked = i == _sel, Icon = i == _sel ? null : "", Act = () => SelectedIndex = idx });
            }
            PopupMenu.Show(this, PopupMenu.Side.Below, null, list, Math.Max(Width, S(200)));
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Down && _sel < Items.Count - 1) SelectedIndex = _sel + 1;
            else if (e.KeyCode == Keys.Up && _sel > 0) SelectedIndex = _sel - 1;
            else if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter || (e.Alt && e.KeyCode == Keys.Down)) OnClick(EventArgs.Empty);
            base.OnKeyDown(e);
        }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Smooth(g);
            using (var p = Ui.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), S(8)))
            {
                using (var b = new SolidBrush(Enabled ? (Hover ? Ui.Hover : Ui.Card) : Ui.Surface)) g.FillPath(b, p);
                using (var pen = new Pen(Focused || Hover ? Ui.AccentSoftLine : Ui.Line)) g.DrawPath(pen, p);
            }
            TextRenderer.DrawText(g, SelectedItem ?? (Items.Count == 0 ? "None installed" : ""), Font,
                new Rectangle(S(12), 0, Width - S(40), Height), SelectedItem == null ? Ui.Faint : Ui.Ink,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            int c = S(14);
            var st = g.Save();
            g.TranslateTransform(Width - S(26), (Height - c) / 2f);
            g.RotateTransform(90, System.Drawing.Drawing2D.MatrixOrder.Prepend);
            g.TranslateTransform(0, -c);
            Icons.Draw(g, "chevron", new RectangleF(0, 0, c, c), Ui.Muted);
            g.Restore(st);
        }
    }

    /// <summary>Rounded numeric text field (digits only).</summary>
    public class NumberField : Panel
    {
        readonly TextBox _box;
        readonly int _min, _max;
        public event EventHandler ValueChanged;
        public NumberField(int min, int max, int width)
        {
            _min = min; _max = max;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Ui.Card;
            Size = new Size(width, Ui.Px(38));
            Padding = new Padding(Ui.Px(12), 0, Ui.Px(10), 0);
            _box = new TextBox { BorderStyle = BorderStyle.None, Font = Ui.Mono, BackColor = Ui.Card, ForeColor = Ui.Ink, MaxLength = Math.Max(1, max.ToString().Length), Anchor = AnchorStyles.Left | AnchorStyles.Right };
            _box.KeyPress += (s, e) => { if (!char.IsDigit(e.KeyChar) && !char.IsControl(e.KeyChar)) e.Handled = true; };
            _box.TextChanged += (s, e) => { var h = ValueChanged; if (h != null) h(this, EventArgs.Empty); Invalidate(); };
            _box.GotFocus += (s, e) => Invalidate();
            _box.LostFocus += (s, e) => { _box.Text = Value.ToString(); Invalidate(); };
            Controls.Add(_box);
        }
        /// <summary>Colour behind the rounded corners (fields normally sit on a card).</summary>
        public Color Under = Color.Empty;
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (_box == null) return;
            _box.SetBounds(Padding.Left, Math.Max(0, (Height - _box.Height) / 2), Math.Max(10, Width - Padding.Horizontal), _box.Height);
        }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); PerformLayout(); }
        public int Value
        {
            get { int v; return int.TryParse(_box.Text, out v) ? Math.Max(_min, Math.Min(_max, v)) : _min; }
            set { _box.Text = value.ToString(); }
        }
        public bool IsValid { get { int v; return int.TryParse(_box.Text, out v) && v >= _min && v <= _max; } }
        protected override void OnEnabledChanged(EventArgs e) { _box.Enabled = Enabled; Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            if (_box.BackColor != Ui.Card) _box.BackColor = Ui.Card;
            if (_box.ForeColor != Ui.Ink) _box.ForeColor = Ui.Ink;
            g.Clear(Under.IsEmpty ? Ui.Card : Under);
            using (var p = Ui.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Ui.Px(8)))
            {
                using (var b = new SolidBrush(Ui.Card)) g.FillPath(b, p);
                using (var pen = new Pen(!IsValid ? Ui.Red : _box.Focused ? Ui.AccentSoftLine : Ui.Line)) g.DrawPath(pen, p);
            }
        }
    }

    /// <summary>Rounded single-line text field; <see cref="Validate"/> marks it red when it returns false.</summary>
    public class TextField : Panel
    {
        readonly TextBox _box;
        public Func<string, bool> Validate;
        public event EventHandler ValueChanged;
        public TextField(int width, bool mono)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Ui.Card;
            Size = new Size(width, Ui.Px(38));
            Padding = new Padding(Ui.Px(12), 0, Ui.Px(10), 0);
            _box = new TextBox { BorderStyle = BorderStyle.None, Font = mono ? Ui.Mono : Ui.Base, BackColor = Ui.Card, ForeColor = Ui.Ink };
            _box.TextChanged += (s, e) => { var h = ValueChanged; if (h != null) h(this, EventArgs.Empty); Invalidate(); };
            _box.GotFocus += (s, e) => Invalidate();
            _box.LostFocus += (s, e) => Invalidate();
            Controls.Add(_box);
        }
        public string Value { get { return _box.Text.Trim(); } set { _box.Text = value ?? ""; } }
        public bool IsValid { get { return Validate == null || Validate(Value); } }
        public TextBox Box { get { return _box; } }
        protected override void OnEnabledChanged(EventArgs e) { _box.Enabled = Enabled; Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (_box == null) return;
            _box.SetBounds(Padding.Left, Math.Max(0, (Height - _box.Height) / 2), Math.Max(10, Width - Padding.Horizontal), _box.Height);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            Color fill = Enabled ? Ui.Card : Ui.Surface;
            if (_box.BackColor != fill) _box.BackColor = fill;
            if (_box.ForeColor != Ui.Ink) _box.ForeColor = Ui.Ink;
            g.Clear(Ui.Card);
            using (var p = Ui.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Ui.Px(8)))
            {
                using (var b = new SolidBrush(fill)) g.FillPath(b, p);
                using (var pen = new Pen(!IsValid ? Ui.Red : _box.Focused ? Ui.AccentSoftLine : Ui.Line)) g.DrawPath(pen, p);
            }
        }
    }

    public enum CalloutKind { Info, Warning, Danger }

    /// <summary>Tinted message box inside a page: icon + wrapped text.</summary>
    public class Callout : Painted
    {
        CalloutKind _kind;
        public Callout(CalloutKind kind, string text, int width)
        {
            _kind = kind; Text = text; Font = Ui.Small; Width = width;
            Margin = new Padding(0, S(4), 0, S(8));
            Fit();
        }
        public void Set(CalloutKind kind, string text) { _kind = kind; Text = text; Fit(); Invalidate(); }
        void Fit()
        {
            var sz = TextRenderer.MeasureText(Text ?? "", Font, new Size(Width - S(52), 0), TextFormatFlags.WordBreak);
            Height = Math.Max(S(40), sz.Height + S(22));
        }
        protected override void OnResize(EventArgs e) { Fit(); base.OnResize(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Smooth(g);
            Color fg = _kind == CalloutKind.Danger ? Ui.Red : _kind == CalloutKind.Warning ? Ui.Amber : Ui.Info;
            Color bg = _kind == CalloutKind.Danger ? Ui.RedSoft : _kind == CalloutKind.Warning ? Ui.AmberSoft : Ui.Surface;
            using (var p = Ui.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), S(8)))
            using (var b = new SolidBrush(bg)) g.FillPath(b, p);
            Icons.Draw(g, _kind == CalloutKind.Info ? "info" : "warning", new RectangleF(S(14), S(11), S(18), S(18)), fg);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(S(42), S(11), Width - S(52), Height - S(16)), Ui.Ink,
                TextFormatFlags.WordBreak | TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>Menu colours that follow the WebHut theme.</summary>
    public class ThemeMenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Ui.Card; } }
        public override Color MenuBorder { get { return Ui.Line; } }
        public override Color MenuItemBorder { get { return Ui.Hover; } }
        public override Color MenuItemSelected { get { return Ui.Hover; } }
        public override Color MenuItemSelectedGradientBegin { get { return Ui.Hover; } }
        public override Color MenuItemSelectedGradientEnd { get { return Ui.Hover; } }
        public override Color ImageMarginGradientBegin { get { return Ui.Card; } }
        public override Color ImageMarginGradientMiddle { get { return Ui.Card; } }
        public override Color ImageMarginGradientEnd { get { return Ui.Card; } }
        public override Color SeparatorDark { get { return Ui.Line; } }
        public override Color SeparatorLight { get { return Ui.Card; } }
    }

    /// <summary>On/off switch.</summary>
    public class Switch : Painted
    {
        bool _on;
        public event EventHandler CheckedChanged;
        public Switch() { Size = new Size(S(40), S(22)); Cursor = Cursors.Hand; Margin = new Padding(0, S(6), S(8), 0); }
        public bool Checked
        {
            get { return _on; }
            set { if (_on == value) return; _on = value; Invalidate(); var h = CheckedChanged; if (h != null) h(this, EventArgs.Empty); }
        }
        protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Smooth(g);
            float h = Height - 1.5f;
            using (var p = Ui.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, h), h / 2))
            using (var b = new SolidBrush(_on ? Ui.Accent : Ui.Grey)) g.FillPath(b, p);
            float d = h - 5;
            float x = _on ? Width - 3 - d : 3;
            using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, x, 3, d, d);
        }
    }

    /// <summary>Rounded check box in the WebHut style.</summary>
    public class CheckMark : Painted
    {
        bool _on;
        public event EventHandler CheckedChanged;
        public CheckMark() { Size = new Size(S(20), S(20)); Cursor = Cursors.Hand; TabStop = true; SetStyle(ControlStyles.Selectable, true); }
        public bool Checked
        {
            get { return _on; }
            set { if (_on == value) return; _on = value; Invalidate(); var h = CheckedChanged; if (h != null) h(this, EventArgs.Empty); }
        }
        protected override void OnClick(EventArgs e) { if (Enabled) Checked = !Checked; base.OnClick(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space && Enabled) Checked = !Checked; base.OnKeyDown(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Smooth(g);
            var r = new RectangleF(1f, 1f, Width - 2.5f, Height - 2.5f);
            using (var p = Ui.Rounded(r, S(5)))
            {
                if (!Enabled) { using (var b = new SolidBrush(Ui.Surface)) g.FillPath(b, p); using (var pen = new Pen(Ui.Line)) g.DrawPath(pen, p); return; }
                if (_on) using (var b = new SolidBrush(Ui.Accent)) g.FillPath(b, p);
                else
                {
                    using (var b = new SolidBrush(Ui.Card)) g.FillPath(b, p);
                    using (var pen = new Pen(Focused || Hover ? Ui.AccentSoftLine : Ui.Faint, Math.Max(1f, S(1.2f)))) g.DrawPath(pen, p);
                }
            }
            if (_on)
                using (var pen = new Pen(Color.White, Math.Max(1.6f, S(2))) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                    g.DrawLines(pen, new[] { new PointF(Width * 0.27f, Height * 0.52f), new PointF(Width * 0.44f, Height * 0.68f), new PointF(Width * 0.74f, Height * 0.34f) });
        }
    }

    /// <summary>Thin rounded progress bar; Value &lt; 0 shows an indeterminate sweep.</summary>
    public class ProgressLine : Painted
    {
        int _value = -1; float _phase;
        readonly Timer _t = new Timer { Interval = 40 };
        public ProgressLine() { Height = S(4); _t.Tick += (s, e) => { _phase = (_phase + 0.025f) % 1.3f; Invalidate(); }; }
        public int Value
        {
            get { return _value; }
            set { _value = value; if (value < 0 && Visible) _t.Start(); else _t.Stop(); Invalidate(); }
        }
        protected override void OnVisibleChanged(EventArgs e) { if (!Visible) _t.Stop(); else if (_value < 0) _t.Start(); base.OnVisibleChanged(e); }
        protected override void Dispose(bool disposing) { if (disposing) _t.Dispose(); base.Dispose(disposing); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Smooth(g);
            float h = Height - 0.5f;
            using (var p = Ui.Rounded(new RectangleF(0, 0, Width - 0.5f, h), h / 2)) using (var b = new SolidBrush(Ui.Surface)) g.FillPath(b, p);
            float x, w;
            if (_value >= 0) { x = 0; w = (Width - 0.5f) * Math.Min(100, _value) / 100f; }
            else { w = Width * 0.3f; x = (_phase - 0.3f) * Width; }
            if (w <= 1) return;
            var clip = g.Clip; g.SetClip(new RectangleF(0, 0, Width, Height));
            using (var p = Ui.Rounded(new RectangleF(x, 0, w, h), h / 2)) using (var b = new SolidBrush(Ui.Accent)) g.FillPath(b, p);
            g.Clip = clip;
        }
    }

    /// <summary>
    /// Hosts an AutoScroll panel and replaces its Windows scroll bar with a slim, rounded one in the WebHut
    /// colours. The panel keeps its own scrolling (mouse wheel, keyboard, ScrollControlIntoView); it is simply made
    /// wider than the host so the native bar is clipped, and the overlay mirrors and drives its scroll position.
    /// </summary>
    public class ScrollHost : Panel
    {
        public readonly ScrollableControl Inner;
        readonly ScrollThumb _bar;
        readonly Timer _watch = new Timer { Interval = 100 };
        int _lastPos = -1, _lastMax = -1;
        bool _fitting;

        public static ScrollHost Wrap(ScrollableControl inner)
        {
            return new ScrollHost(inner);
        }

        ScrollHost(ScrollableControl inner)
        {
            Inner = inner;
            Dock = inner.Dock == DockStyle.None ? DockStyle.Fill : inner.Dock;
            BackColor = inner.BackColor;
            Visible = inner.Visible;
            inner.Visible = true;
            inner.Dock = DockStyle.None;
            _bar = new ScrollThumb(this) { BackColor = inner.BackColor };
            Controls.Add(inner);
            Controls.Add(_bar);
            _bar.BringToFront();
            Resize += (s, e) => Fit();
            inner.Layout += (s, e) => { if (!_fitting) BeginFit(); };
            inner.Scroll += (s, e) => _bar.Invalidate();
            inner.MouseWheel += (s, e) => _bar.Invalidate();
            _watch.Tick += (s, e) => Poll();
            VisibleChanged += (s, e) => { _watch.Enabled = Visible; if (Visible) Fit(); };
            _watch.Enabled = Visible;
        }

        void BeginFit()
        {
            if (!IsHandleCreated) { Fit(); return; }
            try { BeginInvoke(new Action(Fit)); } catch { }
        }

        public int ContentHeight { get { return Inner.DisplayRectangle.Height; } }
        public int ViewHeight { get { return Inner.ClientSize.Height; } }
        public int Position { get { return -Inner.AutoScrollPosition.Y; } }
        public bool Needed { get { return Inner.VerticalScroll.Visible && ContentHeight > ViewHeight; } }

        public void ScrollTo(int y)
        {
            int max = Math.Max(0, ContentHeight - ViewHeight);
            Inner.AutoScrollPosition = new Point(-Inner.AutoScrollPosition.X, Math.Max(0, Math.Min(max, y)));
            _bar.Invalidate();
        }

        void Fit()
        {
            if (_fitting || IsDisposed) return;
            _fitting = true;
            try
            {
                int extra = Inner.VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0;
                var want = new Rectangle(0, 0, ClientSize.Width + extra, ClientSize.Height);
                if (Inner.Bounds != want) Inner.Bounds = want;
                int w = S(8);
                _bar.Bounds = new Rectangle(ClientSize.Width - w - S(3), S(3), w, Math.Max(0, ClientSize.Height - S(6)));
                _bar.Visible = Needed;
                _bar.Invalidate();
            }
            finally { _fitting = false; }
        }

        void Poll()
        {
            int pos = Position, max = ContentHeight;
            bool vis = Needed;
            if (vis != _bar.Visible || (Inner.VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0) != Inner.Width - ClientSize.Width) Fit();
            if (pos != _lastPos || max != _lastMax) { _lastPos = pos; _lastMax = max; _bar.Invalidate(); }
        }

        static int S(float v) { return Ui.Px(v); }

        protected override void Dispose(bool disposing) { if (disposing) _watch.Dispose(); base.Dispose(disposing); }

        /// <summary>The slim bar: transparent track, rounded thumb that darkens on hover and while dragged.</summary>
        class ScrollThumb : Control
        {
            readonly ScrollHost _h;
            bool _hover, _drag;
            int _dragStart, _posStart;
            public ScrollThumb(ScrollHost h)
            {
                _h = h;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
                Cursor = Cursors.Default;
            }

            Rectangle Thumb()
            {
                int content = Math.Max(1, _h.ContentHeight), view = _h.ViewHeight;
                int track = Height;
                int len = Math.Max(S(32), (int)((long)track * view / content));
                int range = Math.Max(1, content - view);
                int y = (int)((long)(track - len) * _h.Position / range);
                return new Rectangle(0, Math.Max(0, Math.Min(track - len, y)), Width, len);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(BackColor);
                if (!_h.Needed) return;
                var t = Thumb();
                bool strong = _hover || _drag;
                float w = strong ? Width - 1 : Width * 0.6f;
                var r = new RectangleF(Width - w - 0.5f, t.Y + 1, w, t.Height - 2);
                Color c = _drag ? Ui.Muted : strong ? Ui.Faint : Ui.Mix(Ui.Faint, BackColor, 0.6f);
                using (var p = Ui.Rounded(r, w / 2)) using (var b = new SolidBrush(c)) g.FillPath(b, p);
            }

            protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left) return;
                var t = Thumb();
                if (t.Contains(e.Location)) { _drag = true; _dragStart = e.Y; _posStart = _h.Position; Capture = true; }
                else _h.ScrollTo(_h.Position + (e.Y < t.Y ? -1 : 1) * Math.Max(S(40), _h.ViewHeight - S(40)));   // page up / down
                Invalidate();
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                if (_drag)
                {
                    var t = Thumb();
                    int track = Math.Max(1, Height - t.Height);
                    int range = Math.Max(0, _h.ContentHeight - _h.ViewHeight);
                    _h.ScrollTo(_posStart + (int)((long)(e.Y - _dragStart) * range / track));
                }
                base.OnMouseMove(e);
            }

            protected override void OnMouseUp(MouseEventArgs e) { _drag = false; Capture = false; Invalidate(); base.OnMouseUp(e); }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                _h.ScrollTo(_h.Position - e.Delta * SystemInformation.MouseWheelScrollLines * S(16) / 120);
                base.OnMouseWheel(e);
            }
        }
    }

    /// <summary>Small icon drawn on a transparent background.</summary>
    public class IconBox : Painted
    {
        string _icon; Color _color;
        public IconBox(string icon, Color color, int size)
        {
            _icon = icon; _color = color; Size = new Size(size, size); Margin = new Padding(0);
        }
        public void Set(string icon, Color color) { _icon = icon; _color = color; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Smooth(g);
            Icons.Draw(g, _icon, new RectangleF(1, 1, Width - 2, Height - 2), _color);
        }
    }

    /// <summary>The WebHut mark as a control.</summary>
    public class Logo : Painted
    {
        public Logo(int size) { Size = new Size(size, size); }
        protected override void OnPaint(PaintEventArgs e)
        {
            using (var bmp = Ui.DrawLogo(Math.Min(Width, Height)))
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.DrawImage(bmp, 0, 0);
            }
        }
    }
}
