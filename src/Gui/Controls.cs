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
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
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
                Active ? Ui.AccentInk : Ui.Ink, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }
    }

    public enum PillState { Running, Stopped, Busy, Partial, Error }

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
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
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
                TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, _sub, Ui.Small, new Rectangle(tx, S(33), tw, S(18)), Ui.Muted,
                TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
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
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
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
