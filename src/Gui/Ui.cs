using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WebHut
{
    /// <summary>Shared look and feel.</summary>
    public static class Ui
    {
        // Palette: set by ApplyTheme. Every control reads these when it paints or is created.
        public static Color Bg, Sidebar, Card, Surface, Line, Ink, Muted, Faint;
        public static Color Accent, AccentHover, AccentInk, AccentSoft, AccentSoftLine;
        public static Color Hover, TileHover, HeadBg, NavInk;
        public static Color Green, GreenSoft, Amber, AmberSoft, Red, RedSoft, Grey, GreySoft, Info;
        public static Color LogBg, LogInk, LogTime, LogGreen, LogAmber, LogRed;
        public static bool IsDark;

        static Ui() { ApplyTheme(false); }

        static Color H(string hex) { return ColorTranslator.FromHtml(hex); }

        /// <summary>WebHut brand palette. Brand orange #EA4B0C, Midnight Slate #151C26, Cloud White #F6F8FB.</summary>
        public static void ApplyTheme(bool dark)
        {
            IsDark = dark;
            Accent = H("#EA4B0C");
            Green = H("#16A36A"); Amber = H("#D99516"); Red = H("#DC4545"); Grey = H("#8A96A6"); Info = H("#3284D6");
            if (!dark)
            {
                Bg = H("#F6F8FB"); Sidebar = H("#FFFFFF"); Card = H("#FFFFFF"); Surface = H("#F0F3F7");
                Line = H("#E2E7ED"); Ink = H("#17212F"); Muted = H("#667487"); Faint = H("#8793A3");
                AccentHover = H("#C93F08"); AccentInk = H("#EA4B0C"); AccentSoft = H("#FFF0E8"); AccentSoftLine = H("#F7B899");
                Hover = H("#F0F3F7"); TileHover = H("#FFF8F4"); HeadBg = Surface; NavInk = Muted;
                GreenSoft = H("#E7F7EF"); AmberSoft = H("#FCF3E0"); RedSoft = H("#FCEBEB"); GreySoft = H("#F0F3F7");
                LogBg = H("#151C26");
            }
            else
            {
                Bg = H("#10151D"); Sidebar = H("#151C26"); Card = H("#1B2430"); Surface = H("#242F3D");
                Line = H("#344152"); Ink = H("#F5F7FA"); Muted = H("#A5B2C2"); Faint = H("#7D8B9D");
                AccentHover = H("#FF762E"); AccentInk = H("#FF762E"); AccentSoft = H("#38251E"); AccentSoftLine = H("#8A3E1C");
                Hover = H("#242F3D"); TileHover = H("#222C38"); HeadBg = Surface; NavInk = Muted;
                GreenSoft = H("#173528"); AmberSoft = H("#3A2F17"); RedSoft = H("#3D2226"); GreySoft = H("#242F3D");
                LogBg = H("#0D1218");
            }
            LogInk = H("#F5F7FA"); LogTime = H("#7D8B9D"); LogGreen = H("#3DD68C"); LogAmber = H("#F2B544"); LogRed = H("#F27474");
        }

        /// <summary>Resolve the "theme" setting: system follows Windows' app light/dark mode.</summary>
        public static bool WantsDark(string theme)
        {
            theme = WebHutConfig.NormTheme(theme);
            if (theme == "dark") return true;
            if (theme == "light") return false;
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    var v = k == null ? null : k.GetValue("AppsUseLightTheme");
                    return v is int && (int)v == 0;
                }
            }
            catch { return false; }
        }

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        /// <summary>Dark or light title bar (Windows 10 2004+ / 11); ignored elsewhere.</summary>
        public static void TitleBar(Form f)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) return;
            try { int on = IsDark ? 1 : 0; if (DwmSetWindowAttribute(f.Handle, 20, ref on, 4) != 0) DwmSetWindowAttribute(f.Handle, 19, ref on, 4); } catch { }
        }

        /// <summary>Draw ListView column headers in the theme (the system header ignores BackColor).</summary>
        static void ThemeListView(ListView lv)
        {
            if (lv.View != View.Details || lv.OwnerDraw) return;
            lv.OwnerDraw = true;
            lv.DrawItem += (s, e) => { e.DrawDefault = true; };
            lv.DrawSubItem += (s, e) => { e.DrawDefault = true; };
            lv.DrawColumnHeader += (s, e) =>
            {
                using (var b = new SolidBrush(HeadBg)) e.Graphics.FillRectangle(b, e.Bounds);
                using (var p = new Pen(Line))
                {
                    e.Graphics.DrawLine(p, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
                    e.Graphics.DrawLine(p, e.Bounds.Right - 1, e.Bounds.Top + 4, e.Bounds.Right - 1, e.Bounds.Bottom - 5);
                }
                var r = new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height);
                TextRenderer.DrawText(e.Graphics, (e.Header.Text ?? "").ToUpperInvariant(), Caps, r, Muted,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            };
        }

        /// <summary>Give standard WinForms controls (text boxes, lists, combos...) the theme colours.</summary>
        [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hwnd, string subApp, string subIdList);
        [System.Runtime.InteropServices.DllImport("uxtheme.dll", EntryPoint = "#133")]
        static extern bool AllowDarkModeForWindow(IntPtr hwnd, bool allow);
        [System.Runtime.InteropServices.DllImport("uxtheme.dll", EntryPoint = "#135")]
        static extern int SetPreferredAppMode(int mode);
        static bool _appModeSet;

        /// <summary>
        /// Windows' own scroll bars in dark or light: the "DarkMode_Explorer" visual style (Windows 10 1809+ / 11).
        /// Older Windows keeps the classic bar. Page-level scrolling uses <see cref="ScrollHost"/> instead.
        /// </summary>
        public static void ThemeScrollBars(Control c, bool dark)
        {
            if (!Sys.IsWindows) return;
            Action apply = () =>
            {
                try
                {
                    if (!_appModeSet) { _appModeSet = true; try { SetPreferredAppMode(1); } catch { } }   // 1 = AllowDark
                    try { AllowDarkModeForWindow(c.Handle, dark); } catch { }
                    SetWindowTheme(c.Handle, dark ? "DarkMode_Explorer" : "Explorer", null);
                }
                catch { }
            };
            if (c.IsHandleCreated) apply();
            else c.HandleCreated += (s, e) => apply();
        }

        public static void ThemeTree(Control root)
        {
            foreach (Control c in root.Controls)
            {
                // Native scroll bars (text boxes, lists, the activity log) follow the theme; the log console is always dark.
                if (c is TextBoxBase || c is ListView || c is ListBox || c is DataGridView)
                    ThemeScrollBars(c, IsDark || c.BackColor == LogBg || (c.BackColor.GetBrightness() < 0.3f && c.BackColor.A == 255));
                if (c.Parent is NumberField || c.Parent is TextField || c.Tag as string == "keep-colors") continue;
                if (c is TextBoxBase || c is ListView || c is ListBox || c is ComboBox || c is NumericUpDown || c is DataGridView)
                {
                    if (c is RichTextBox && c.BackColor == LogBg) { }
                    else { c.BackColor = Surface; c.ForeColor = Ink; }
                    if (c is ComboBox) ((ComboBox)c).FlatStyle = FlatStyle.Flat;
                    if (c is ListView) ThemeListView((ListView)c);
                }
                else if (c is CheckBox || c is RadioButton || c is GroupBox)
                    c.ForeColor = Ink;
                if (c.HasChildren) ThemeTree(c);
            }
        }

        // Typography (Inter + JetBrains Mono, embedded; see Fonts.cs). Sizes are the design's px at 96 dpi.
        public static readonly Font Base = Fonts.Make("Inter", Fonts.Px(14));                       // body 14/400
        public static readonly Font Bold = Fonts.Make("Inter SemiBold", Fonts.Px(14));              // buttons 14/600
        public static readonly Font Small = Fonts.Make("Inter", Fonts.Px(12));                      // secondary 12/400
        public static readonly Font Title = Fonts.Make("Inter SemiBold", Fonts.Px(20));             // dialog titles
        public static readonly Font Mono = Fonts.Make("JetBrains Mono", Fonts.Px(12));              // ports / PID 12/400
        public static readonly Font H1 = Fonts.Make("Inter", Fonts.Px(28), FontStyle.Bold);         // app title 28/700
        public static readonly Font H2 = Fonts.Make("Inter SemiBold", Fonts.Px(20));                // section headings 20/600
        public static readonly Font Brand = Fonts.Make("Inter", Fonts.Px(22), FontStyle.Bold);      // sidebar wordmark
        public static readonly Font Nav = Fonts.Make("Inter Medium", Fonts.Px(14));                 // sidebar 14/500
        public static readonly Font NavBold = Fonts.Make("Inter SemiBold", Fonts.Px(14));           // active nav 14/600
        public static readonly Font ServiceName = Fonts.Make("Inter SemiBold", Fonts.Px(15));       // service names 15/600
        public static readonly Font Detail = Fonts.Make("Inter", Fonts.Px(13));                    // service versions 13/400
        public static readonly Font BtnSmall = Fonts.Make("Inter SemiBold", Fonts.Px(13));          // table buttons 13/600
        public static readonly Font SmallBold = Fonts.Make("Inter SemiBold", Fonts.Px(12));
        public static readonly Font SectionTitle = Fonts.Make("Inter SemiBold", Fonts.Px(16));     // settings section titles
        public static readonly Font Caps = Fonts.Make("Inter SemiBold", Fonts.Px(11));              // table headers 11/600
        public static readonly Font LogFont = Fonts.Make("JetBrains Mono", Fonts.Px(12));           // activity log 12/400

        static float _dpi;
        /// <summary>Screen DPI (the process is DPI-aware, so this is the real value).</summary>
        public static float Dpi
        {
            get
            {
                if (_dpi <= 0)
                {
                    // Ask GDI directly; GDI+ may have cached a pre-awareness 96.
                    try
                    {
                        if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                        {
                            IntPtr dc = GetDC(IntPtr.Zero);
                            if (dc != IntPtr.Zero) { _dpi = GetDeviceCaps(dc, 88 /* LOGPIXELSX */); ReleaseDC(IntPtr.Zero, dc); }
                        }
                    }
                    catch { }
                    if (_dpi <= 0) try { using (var g = Graphics.FromHwnd(IntPtr.Zero)) _dpi = g.DpiX; } catch { }
                    if (_dpi <= 0) _dpi = 96;
                }
                return _dpi;
            }
        }
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
        [DllImport("gdi32.dll")] static extern int GetDeviceCaps(IntPtr dc, int index);

        /// <summary>A 96-dpi length in physical pixels.</summary>
        public static int Px(float v) { return (int)Math.Round(v * Dpi / 96f); }

        static Icon _icon;
        public static Icon AppIcon
        {
            get
            {
                if (_icon == null)
                {
                    // The multi-size .ico embedded in the exe (16-256 px), read directly: Windows then picks the right size
                    // for the title bar, Alt+Tab and the taskbar at any scaling. ExtractAssociatedIcon would give a single
                    // 32 px icon from Explorer's icon cache, which can still hold an older WebHut icon after an upgrade.
                    try
                    {
                        using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("logo/webhut.ico"))
                            if (s != null) _icon = new Icon(s);
                    }
                    catch { }
                    if (_icon == null) try { _icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
                    if (_icon == null) _icon = Icon.FromHandle(DrawLogo(32).GetHicon());
                }
                return _icon;
            }
        }

        static readonly int[] LogoSizes = { 16, 24, 32, 48, 64, 128, 256 };
        static readonly Dictionary<int, Bitmap> LogoCache = new Dictionary<int, Bitmap>();

        /// <summary>The WebHut mark (assets/logo/webhut-icon.svg), from the PNG renders embedded as logo/webhut-&lt;n&gt;.png:
        /// the exact size when there is one (crisp at 16-32 px), otherwise the next larger one scaled down.</summary>
        public static Bitmap DrawLogo(int size)
        {
            int src = LogoSizes.FirstOrDefault(n => n >= size);
            if (src == 0) src = LogoSizes[LogoSizes.Length - 1];
            Bitmap master;
            lock (LogoCache)
            {
                if (!LogoCache.TryGetValue(src, out master))
                {
                    var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("logo/webhut-" + src + ".png");
                    master = s == null ? null : new Bitmap(s);
                    LogoCache[src] = master;
                }
            }
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                if (master != null) g.DrawImage(master, new Rectangle(0, 0, size, size));
                else using (var path = Rounded(new RectangleF(0, 0, size - 1, size - 1), size * 0.22f))
                     using (var b = new SolidBrush(Color.FromArgb(0xEA, 0x4B, 0x0C))) g.FillPath(b, path);
            }
            return bmp;
        }

        /// <summary>Blend <paramref name="a"/> over <paramref name="b"/> (amount 0..1 of a).</summary>
        public static Color Mix(Color a, Color b, float amount)
        {
            return Color.FromArgb((int)(a.R * amount + b.R * (1 - amount)), (int)(a.G * amount + b.G * (1 - amount)), (int)(a.B * amount + b.B * (1 - amount)));
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
            b.FlatAppearance.MouseOverBackColor = primary ? AccentHover : Hover;
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
            f.HandleCreated += (s, e) => TitleBar(f);
            f.Load += (s, e) => ThemeTree(f);
        }

        public static void Error(IWin32Window owner, string title, string message)
        {
            MessageBox.Show(owner, message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        public static void EnableDpi() { try { SetProcessDPIAware(); } catch { } }

        /// <summary>A context menu in the WebHut theme.</summary>
        public static ContextMenuStrip Menu()
        {
            var m = new ContextMenuStrip { Font = Base, BackColor = Card, ForeColor = Ink, ShowImageMargin = false, Padding = new Padding(Px(4)) };
            m.Renderer = new ToolStripProfessionalRenderer(new ThemeMenuColors()) { RoundedEdges = false };
            return m;
        }

        public static ToolStripMenuItem Item(ContextMenuStrip m, string text, Action act, bool enabled = true)
        {
            var i = new ToolStripMenuItem(text) { ForeColor = Ink, Enabled = enabled, Padding = new Padding(Px(4), Px(4), Px(4), Px(4)) };
            if (act != null) i.Click += (s, e) => act();
            m.Items.Add(i);
            return i;
        }

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

        /// <summary>Append to the dark activity log: grey [time], then the message coloured by kind.</summary>
        public static void AppendDarkLog(RichTextBox box, string line, bool scroll)
        {
            if (box.IsDisposed) return;
            if (box.InvokeRequired) { try { box.BeginInvoke(new Action(() => AppendDarkLog(box, line, scroll))); } catch { } return; }
            string time = line.Length > 8 && line[2] == ':' && line[5] == ':' ? line.Substring(0, 8) : null;
            string msg = time != null ? line.Substring(8).TrimStart() : line;
            Color c = LogInk;
            if (msg.StartsWith("ERROR:")) c = LogRed;
            else if (msg.StartsWith("WARN:")) c = LogAmber;
            else if (msg.Contains(" is running") || msg.StartsWith("Setup complete") || msg.StartsWith("WebHut ") || msg.Contains(" stopped") || msg.StartsWith("Installed ") || msg.Contains("Backup saved")) c = LogGreen;
            box.SelectionStart = box.TextLength;
            box.SelectionLength = 0;
            if (time != null)
            {
                box.SelectionColor = LogTime;
                box.AppendText("[" + time + "]  ");
            }
            box.SelectionColor = c;
            box.AppendText(msg + Environment.NewLine);
            if (box.Lines.Length > 2000) { box.Select(0, box.GetFirstCharIndexFromLine(500)); box.SelectedText = ""; }
            if (scroll) { box.SelectionStart = box.TextLength; box.ScrollToCaret(); }
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
            using (var f = new Form { Text = title, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(420, 140), ShowInTaskbar = false })
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
