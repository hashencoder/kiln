using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Kiln
{
    /// <summary>Shared look and feel.</summary>
    public static class Ui
    {
        public static readonly Color Bg = Color.FromArgb(250, 248, 245);
        public static readonly Color Card = Color.White;
        public static readonly Color Ink = Color.FromArgb(31, 29, 26);
        public static readonly Color Muted = Color.FromArgb(107, 101, 93);
        public static readonly Color Line = Color.FromArgb(231, 225, 216);
        public static readonly Color Accent = Color.FromArgb(194, 65, 12);
        public static readonly Color AccentHover = Color.FromArgb(234, 88, 12);
        public static readonly Color Green = Color.FromArgb(22, 163, 74);
        public static readonly Color Amber = Color.FromArgb(217, 119, 6);
        public static readonly Color Red = Color.FromArgb(220, 38, 38);
        public static readonly Color Grey = Color.FromArgb(168, 162, 158);

        public static readonly Font Base = new Font("Segoe UI", 9.75f);
        public static readonly Font Bold = new Font("Segoe UI Semibold", 9.75f);
        public static readonly Font Small = new Font("Segoe UI", 8.75f);
        public static readonly Font Title = new Font("Segoe UI Semibold", 17f);
        public static readonly Font Mono = new Font("Consolas", 9f);

        static Icon _icon;
        public static Icon AppIcon
        {
            get
            {
                if (_icon == null)
                {
                    try { _icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
                    if (_icon == null) _icon = Icon.FromHandle(DrawLogo(32).GetHicon());
                }
                return _icon;
            }
        }

        /// <summary>The Kiln mark: a flame in a rounded square.</summary>
        public static Bitmap DrawLogo(int size)
        {
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float r = size * 0.22f;
                using (var path = Rounded(new RectangleF(0, 0, size - 1, size - 1), r))
                using (var b = new SolidBrush(Accent)) g.FillPath(b, path);
                float s = size / 32f;
                using (var flame = new GraphicsPath())
                {
                    flame.AddBezier(16 * s, 5 * s, 22 * s, 11 * s, 25 * s, 16 * s, 24 * s, 21 * s);
                    flame.AddBezier(24 * s, 21 * s, 23 * s, 25.5f * s, 19.5f * s, 28 * s, 16 * s, 28 * s);
                    flame.AddBezier(16 * s, 28 * s, 12.5f * s, 28 * s, 9 * s, 25.5f * s, 8 * s, 21 * s);
                    flame.AddBezier(8 * s, 21 * s, 7.5f * s, 17 * s, 10 * s, 14 * s, 12 * s, 12.5f * s);
                    flame.AddBezier(12 * s, 12.5f * s, 12 * s, 15 * s, 13 * s, 17 * s, 14.5f * s, 17.5f * s);
                    flame.AddBezier(14.5f * s, 17.5f * s, 14 * s, 13 * s, 15 * s, 9 * s, 16 * s, 5 * s);
                    flame.CloseFigure();
                    using (var w = new SolidBrush(Color.White)) g.FillPath(w, flame);
                }
            }
            return bmp;
        }

        public static GraphicsPath Rounded(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static Button Btn(string text, EventHandler click, bool primary = false, int width = 0)
        {
            var b = new Button
            {
                Text = text,
                FlatStyle = FlatStyle.Flat,
                Font = primary ? Bold : Base,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(width, 30),
                Padding = new Padding(8, 3, 8, 3),
                Margin = new Padding(0, 0, 6, 0),
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false,
                BackColor = primary ? Accent : Card,
                ForeColor = primary ? Color.White : Ink
            };
            b.FlatAppearance.BorderColor = primary ? Accent : Line;
            b.FlatAppearance.MouseOverBackColor = primary ? AccentHover : Color.FromArgb(245, 241, 235);
            if (click != null) b.Click += click;
            return b;
        }

        public static Label Lbl(string text, Font font = null, Color? color = null)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                Font = font ?? Base,
                ForeColor = color ?? Ink,
                Margin = new Padding(0, 6, 8, 0)
            };
        }

        public static void Style(Form f)
        {
            f.Font = Base;
            f.BackColor = Bg;
            f.ForeColor = Ink;
            f.Icon = AppIcon;
            f.AutoScaleDimensions = new SizeF(96F, 96F);
            f.AutoScaleMode = AutoScaleMode.Dpi;
        }

        public static void Error(IWin32Window owner, string title, string message)
        {
            MessageBox.Show(owner, message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        public static void EnableDpi() { try { SetProcessDPIAware(); } catch { } }

        /// <summary>Thread-safe append to a log box, with colour for warnings and errors.</summary>
        public static void AppendLog(RichTextBox box, string line)
        {
            if (box.IsDisposed) return;
            if (box.InvokeRequired) { try { box.BeginInvoke(new Action(() => AppendLog(box, line))); } catch { } return; }
            box.SelectionStart = box.TextLength;
            box.SelectionLength = 0;
            box.SelectionColor = line.Contains("ERROR:") ? Red : line.Contains("WARN:") ? Amber : Ink;
            box.AppendText(line + Environment.NewLine);
            box.SelectionColor = Ink;
            if (box.Lines.Length > 2000) { box.Select(0, box.GetFirstCharIndexFromLine(500)); box.SelectedText = ""; }
            box.ScrollToCaret();
        }

        public static RichTextBox LogBox()
        {
            return new RichTextBox
            {
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                BackColor = Card,
                Font = Mono,
                Dock = DockStyle.Fill,
                DetectUrls = true,
                WordWrap = true
            };
        }

        /// <summary>Small prompt dialog returning text or null.</summary>
        public static string Prompt(IWin32Window owner, string title, string label, bool password = false)
        {
            using (var f = new Form { Text = title, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(380, 130), ShowInTaskbar = false })
            {
                Style(f);
                var l = Lbl(label); l.Location = new Point(14, 12);
                var t = new TextBox { Location = new Point(16, 40), Width = 348, UseSystemPasswordChar = password };
                var ok = Btn("OK", null, true, 90); ok.Location = new Point(176, 84); ok.DialogResult = DialogResult.OK;
                var cancel = Btn("Cancel", null, false, 90); cancel.Location = new Point(274, 84); cancel.DialogResult = DialogResult.Cancel;
                f.Controls.AddRange(new Control[] { l, t, ok, cancel });
                f.AcceptButton = ok; f.CancelButton = cancel;
                return f.ShowDialog(owner) == DialogResult.OK ? t.Text : null;
            }
        }
    }
}
