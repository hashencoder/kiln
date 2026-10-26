using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ServNook
{
    public class PopupItem
    {
        public string Title, Description, Icon;
        public Action Act;
        public bool Enabled = true, Danger, Checked, Separator;
        public static PopupItem Line() { return new PopupItem { Separator = true }; }
    }

    /// <summary>
    /// ServNook's own context menu: rounded, bordered, shadowed, with icons and optional descriptions.
    /// Closes on Escape or an outside click; Up/Down/Enter work; it keeps itself on screen.
    /// </summary>
    public class PopupMenu : Form
    {
        readonly List<PopupItem> _items;
        readonly string _header;
        int _hot = -1;
        static int Px(float v) { return Ui.Px(v); }

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        PopupMenu(string header, List<PopupItem> items, int width)
        {
            _header = header; _items = items;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            KeyPreview = true;
            BackColor = Ui.Card;
            Font = Ui.Base;
            DoubleBuffered = true;
            Width = width;
            Height = Measure();
            Deactivate += (s, e) => Close();
            for (int i = 0; i < _items.Count; i++) if (Selectable(i)) { _hot = -1; break; }
        }

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ClassStyle |= 0x00020000; /* CS_DROPSHADOW */ return cp; }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // Windows 11: real rounded corners + soft shadow. Windows 10: clip to a rounded region.
            int round = 2, dark = Ui.IsDark ? 1 : 0;
            bool ok = false;
            try { ok = DwmSetWindowAttribute(Handle, 33, ref round, 4) == 0; DwmSetWindowAttribute(Handle, 20, ref dark, 4); } catch { }
            if (!ok || Environment.OSVersion.Version.Build < 22000)
                using (var p = Ui.Rounded(new RectangleF(0, 0, Width, Height), Px(10))) Region = new Region(p);
        }

        int HeaderH { get { return _header == null ? 0 : Px(34); } }
        int ItemH(PopupItem it) { return it.Separator ? Px(9) : string.IsNullOrEmpty(it.Description) ? Px(40) : Px(52); }
        bool Selectable(int i) { return !_items[i].Separator && _items[i].Enabled; }

        int Measure()
        {
            int h = Px(6) + HeaderH;
            foreach (var it in _items) h += ItemH(it);
            return h + Px(6);
        }

        Rectangle ItemRect(int index)
        {
            int y = Px(6) + HeaderH;
            for (int i = 0; i < index; i++) y += ItemH(_items[i]);
            return new Rectangle(Px(6), y, Width - Px(12), ItemH(_items[index]));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Ui.Card);
            using (var p = Ui.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Px(10)))
            using (var pen = new Pen(Ui.Line)) g.DrawPath(pen, p);
            if (_header != null)
                TextRenderer.DrawText(g, _header.ToUpperInvariant(), Ui.Caps, new Rectangle(Px(18), Px(8), Width - Px(30), HeaderH - Px(4)), Ui.Muted,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
            Color hoverInk = Ui.IsDark ? Ui.AccentInk : Ui.AccentHover;
            for (int i = 0; i < _items.Count; i++)
            {
                var it = _items[i];
                var r = ItemRect(i);
                if (it.Separator)
                {
                    using (var pen = new Pen(Ui.Line)) g.DrawLine(pen, r.Left + Px(6), r.Top + r.Height / 2, r.Right - Px(6), r.Top + r.Height / 2);
                    continue;
                }
                bool hot = i == _hot && it.Enabled;
                if (hot)
                    using (var p = Ui.Rounded(new RectangleF(r.X, r.Y + 1, r.Width, r.Height - 2), Px(7)))
                    using (var b = new SolidBrush(Ui.AccentSoft)) g.FillPath(b, p);
                Color ink = !it.Enabled ? Ui.Faint : hot ? hoverInk : it.Danger ? Ui.Red : Ui.Ink;
                Color icon = !it.Enabled ? Ui.Faint : hot ? hoverInk : it.Danger ? Ui.Red : Ui.NavInk;
                int x = r.X + Px(12);
                if (it.Icon != null || it.Checked)
                {
                    int s = Px(18);
                    Icons.Draw(g, it.Checked ? "check" : it.Icon, new RectangleF(x, r.Y + (string.IsNullOrEmpty(it.Description) ? (r.Height - s) / 2f : Px(9)), s, s), icon);
                    x += s + Px(12);
                }
                if (string.IsNullOrEmpty(it.Description))
                    TextRenderer.DrawText(g, it.Title, it.Checked ? Ui.Bold : Ui.Base, new Rectangle(x, r.Y, r.Right - x - Px(8), r.Height), ink,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                else
                {
                    TextRenderer.DrawText(g, it.Title, Ui.Bold, new Rectangle(x, r.Y + Px(7), r.Right - x - Px(8), Px(20)), ink, TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                    TextRenderer.DrawText(g, it.Description, Ui.Small, new Rectangle(x, r.Y + Px(27), r.Right - x - Px(8), Px(18)), it.Enabled ? Ui.Muted : Ui.Faint, TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                }
            }
        }

        int HitTest(Point p)
        {
            for (int i = 0; i < _items.Count; i++) if (ItemRect(i).Contains(p) && Selectable(i)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = HitTest(e.Location);
            if (h != _hot) { _hot = h; Cursor = h >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
            base.OnMouseMove(e);
        }
        protected override void OnMouseLeave(EventArgs e) { _hot = -1; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            int h = HitTest(e.Location);
            if (h >= 0) Invoke(h);
            base.OnMouseUp(e);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys key)
        {
            if (key == Keys.Escape) { Close(); return true; }
            if (key == Keys.Down || key == Keys.Up)
            {
                int step = key == Keys.Down ? 1 : -1, i = _hot;
                for (int n = 0; n < _items.Count; n++)
                {
                    i = (i + step + _items.Count) % _items.Count;
                    if (Selectable(i)) { _hot = i; Invalidate(); break; }
                }
                return true;
            }
            if ((key == Keys.Enter || key == Keys.Space) && _hot >= 0) { Invoke(_hot); return true; }
            return base.ProcessCmdKey(ref msg, key);
        }

        void Invoke(int i)
        {
            var act = _items[i].Act;
            var owner = Owner;
            Close();
            if (act != null)
            {
                if (owner != null && !owner.IsDisposed) owner.BeginInvoke(act); else act();
            }
        }

        public enum Side { Right, Below, BelowRight }

        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);

        /// <summary>
        /// Show at a screen point (the system tray): opens above and to the left of the cursor, like Windows' own
        /// tray menus, and takes the foreground so a click anywhere else closes it.
        /// </summary>
        public static void ShowAt(Point screenPt, string header, List<PopupItem> items, int width = 0)
        {
            var m = new PopupMenu(header, items, width > 0 ? width : Px(280));
            var screen = Screen.FromPoint(screenPt).WorkingArea;
            var p = new Point(screenPt.X - m.Width + Px(8), screenPt.Y - m.Height - Px(6));
            if (p.Y < screen.Top) p.Y = screenPt.Y + Px(6);
            p.X = Math.Max(screen.Left + Px(4), Math.Min(p.X, screen.Right - m.Width - Px(4)));
            p.Y = Math.Max(screen.Top + Px(4), Math.Min(p.Y, screen.Bottom - m.Height - Px(4)));
            m.Location = p;
            m.TopMost = true;
            m.FormClosed += (s, e) => m.Dispose();
            m.Show();
            try { SetForegroundWindow(m.Handle); } catch { }
            m.Activate();
        }

        /// <summary>Show next to <paramref name="anchor"/>; flips or shifts to stay on screen.</summary>
        public static void Show(Control anchor, Side side, string header, List<PopupItem> items, int width = 0)
        {
            var form = anchor.FindForm();
            var m = new PopupMenu(header, items, width > 0 ? width : Px(280));
            var a = anchor.RectangleToScreen(anchor.ClientRectangle);
            var screen = Screen.FromControl(anchor).WorkingArea;
            Point p;
            if (side == Side.Right)
            {
                p = new Point(a.Right + Px(6), a.Top - Px(6));
                if (p.X + m.Width > screen.Right) p.X = a.Left - m.Width - Px(6);
            }
            else
            {
                p = new Point(side == Side.BelowRight ? a.Right - m.Width : a.Left, a.Bottom + Px(4));
                if (p.Y + m.Height > screen.Bottom) p.Y = a.Top - m.Height - Px(4);   // open upwards when there's no room
            }
            p.X = Math.Max(screen.Left + Px(4), Math.Min(p.X, screen.Right - m.Width - Px(4)));
            p.Y = Math.Max(screen.Top + Px(4), Math.Min(p.Y, screen.Bottom - m.Height - Px(4)));
            m.Location = p;
            if (form != null) m.Owner = form;
            m.FormClosed += (s, e) => m.Dispose();
            m.Show();
            m.Activate();
        }
    }

    public enum ToastKind { Success, Info, Warning, Error }

    /// <summary>Small notification in the bottom-right corner of the owning window; fades after a few seconds.</summary>
    public class Toast : Form
    {
        static readonly List<Toast> Open = new List<Toast>();
        readonly ToastKind _kind;
        readonly Timer _life = new Timer();
        static int Px(float v) { return Ui.Px(v); }

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        Toast(string text, ToastKind kind)
        {
            _kind = kind; Text = text;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Ui.Card;
            Font = Ui.Base;
            DoubleBuffered = true;
            var sz = TextRenderer.MeasureText(text, Ui.Base, new Size(Px(320), 0), TextFormatFlags.WordBreak);
            Size = new Size(Math.Min(Px(380), sz.Width + Px(64)), Math.Max(Px(48), sz.Height + Px(26)));
            _life.Interval = kind == ToastKind.Error || kind == ToastKind.Warning ? 6000 : 3500;
            _life.Tick += (s, e) => { _life.Stop(); Close(); };
            Click += (s, e) => Close();
            FormClosed += (s, e) => { Open.Remove(this); _life.Dispose(); Restack(); };
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ClassStyle |= 0x00020000; cp.ExStyle |= 0x08000000 /* WS_EX_NOACTIVATE */; return cp; }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int round = 2; bool ok = false;
            try { ok = DwmSetWindowAttribute(Handle, 33, ref round, 4) == 0; } catch { }
            if (!ok || Environment.OSVersion.Version.Build < 22000)
                using (var p = Ui.Rounded(new RectangleF(0, 0, Width, Height), Px(10))) Region = new Region(p);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Ui.Card);
            Color c = _kind == ToastKind.Success ? Ui.Green : _kind == ToastKind.Warning ? Ui.Amber : _kind == ToastKind.Error ? Ui.Red : Ui.Info;
            using (var p = Ui.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Px(10)))
            using (var pen = new Pen(Ui.Line)) g.DrawPath(pen, p);
            using (var b = new SolidBrush(c)) g.FillRectangle(b, 0, Px(10), Px(3), Height - Px(20));
            Icons.Draw(g, _kind == ToastKind.Success ? "check" : _kind == ToastKind.Info ? "info" : "warning",
                new RectangleF(Px(16), (Height - Px(20)) / 2f, Px(20), Px(20)), c);
            TextRenderer.DrawText(g, Text, Ui.Base, new Rectangle(Px(46), 0, Width - Px(58), Height), Ui.Ink,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        }

        Form _host;

        static void Restack()
        {
            foreach (var group in Open.GroupBy(t => t._host))
            {
                var host = group.Key;
                if (host == null || host.IsDisposed) continue;
                var area = host.RectangleToScreen(host.ClientRectangle);
                int y = area.Bottom - Px(16);
                foreach (var t in group.Reverse())
                {
                    y -= t.Height;
                    t.Location = new Point(area.Right - t.Width - Px(16), y);
                    y -= Px(8);
                }
            }
        }

        /// <summary>Show a toast over <paramref name="host"/>'s bottom-right corner (skipped when it's hidden).</summary>
        public static void Show(Form host, string text, ToastKind kind = ToastKind.Success)
        {
            if (host == null || host.IsDisposed || !host.Visible || host.WindowState == FormWindowState.Minimized) return;
            if (host.InvokeRequired) { try { host.BeginInvoke(new Action(() => Show(host, text, kind))); } catch { } return; }
            while (Open.Count >= 3) Open[0].Close();
            var t = new Toast(text, kind) { _host = host, Owner = host };
            Open.Add(t);
            Restack();
            try { ShowWindow(t.Handle, 4 /* SW_SHOWNOACTIVATE */); } catch { }
            t.Visible = true;
            t._life.Start();
            EventHandler move = null;
            move = (s, e) => Restack();
            host.Move += move; host.Resize += move;
            t.FormClosed += (s, e) => { host.Move -= move; host.Resize -= move; };
        }
    }
}
