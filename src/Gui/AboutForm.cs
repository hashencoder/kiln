using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WebHut
{
    /// <summary>About WebHut: logo, version, what's included, the install folder, license and the project page.</summary>
    public class AboutForm : Form
    {
        const string Repo = "https://github.com/hashencoder/webhut";
        static int Px(float v) { return Ui.Px(v); }
        readonly Label _update;
        readonly LinkLabel _download;
        readonly ToolTip _tips = new ToolTip();
        readonly List<string[]> _comps;

        public AboutForm()
        {
            Ui.Style(this);
            AutoScaleMode = AutoScaleMode.None;
            Text = "About WebHut";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            BackColor = Ui.Card;
            int w = Px(480), pad = Px(24), inner = w - 2 * pad;

            var body = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, Dock = DockStyle.Fill, BackColor = Ui.Card, Padding = new Padding(pad, Px(22), pad, Px(8)) };

            // ---- header: logo, name, version and open-source badges, update status
            var hero = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, Px(14)) };
            hero.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Px(70)));
            hero.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            hero.Controls.Add(new Logo(Px(56)) { Margin = new Padding(0, Px(2), 0, 0) }, 0, 0);
            var titles = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, BackColor = Color.Transparent, Margin = new Padding(0) };
            titles.Controls.Add(new Label { Text = "WebHut", Font = Ui.Title, ForeColor = Ui.Ink, AutoSize = true, Margin = new Padding(0, 0, 0, Px(4)) });
            var badges = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Color.Transparent, Margin = new Padding(0) };
            badges.Controls.Add(new Chip("Version " + K.AppVersion, null, ChipKind.Accent) { Margin = new Padding(0, 0, Px(6), 0) });
            badges.Controls.Add(new Chip("Open source", null, ChipKind.Green) { Margin = new Padding(0) });
            titles.Controls.Add(badges);
            var upd = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Color.Transparent, Margin = new Padding(0, Px(6), 0, 0) };
            _update = new Label { Text = "Checking for updates…", Font = Ui.Small, ForeColor = Ui.Muted, AutoSize = true, Margin = new Padding(0, 0, Px(6), 0), UseMnemonic = false };
            _download = Link("Download", Ui.Small, () => Sys.OpenUrl(Repo + "/releases/latest"));
            _download.Visible = false;
            upd.Controls.Add(_update); upd.Controls.Add(_download);
            titles.Controls.Add(upd);
            hero.Controls.Add(titles, 1, 0);
            body.Controls.Add(hero);

            body.Controls.Add(new Label { Text = "Your portable local development environment for Windows. Everything you need to build, run and test web applications, in one folder.",
                                          Font = Ui.Base, ForeColor = Ui.Ink, AutoSize = true, MaximumSize = new Size(inner, 0), Margin = new Padding(0, 0, 0, Px(18)) });

            // ---- included technologies: one badge each, with the installed version
            var cfg = Services.Cfg;
            _comps = new List<string[]>
            {
                new[] { "Apache", Ver(K.Apache), "Apache License 2.0" },
                new[] { "PHP", string.IsNullOrEmpty(cfg.Php) ? null : Ver(cfg.PhpDir), "PHP License 3.01" },
                new[] { "MariaDB", Ver(K.MariaDb), "GPL v2" },
                new[] { "phpMyAdmin", Ver(K.Pma), "GPL v2" },
                new[] { "Node.js", cfg.NodeDir == null ? null : Ver(cfg.NodeDir), "MIT" },
            };
            body.Controls.Add(Heading("Included technologies"));
            var stack = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(inner, 0), BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, Px(16)) };
            foreach (var c in _comps)
            {
                var chip = new Chip(c[0], c[1] == null ? "not installed" : Short(c[1]), c[1] == null ? ChipKind.Off : ChipKind.Plain) { Margin = new Padding(0, 0, Px(6), Px(6)) };
                _tips.SetToolTip(chip, c[0] + (c[1] == null ? " isn't installed yet" : " " + c[1]) + " · " + c[2]);
                stack.Controls.Add(chip);
            }
            body.Controls.Add(stack);

            // ---- installation directory with copy / open
            body.Controls.Add(Heading("Installation directory"));
            var path = new PathBox(K.Root) { Width = inner, Margin = new Padding(0, 0, 0, Px(16)) };
            path.Copied += () => Toast.Show(this, "Folder path copied.", ToastKind.Info);
            _tips.SetToolTip(path, "Copy the path, or open the folder");
            body.Controls.Add(path);

            // ---- license line and small links
            var lic = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, Px(6)) };
            lic.Controls.Add(new Label { Text = "Free and open source  ·", Font = Ui.Base, ForeColor = Ui.Muted, AutoSize = true, Margin = new Padding(0, 0, Px(4), 0) });
            lic.Controls.Add(Link("MIT License", Ui.Base, OpenLicense));
            body.Controls.Add(lic);
            var more = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Color.Transparent, Margin = new Padding(0) };
            more.Controls.Add(Link("What's new", Ui.Small, () => Sys.OpenUrl(Repo + "/releases")));
            more.Controls.Add(Link("Report a problem", Ui.Small, () => Sys.OpenUrl(Repo + "/issues/new")));
            var copyInfo = Link("Copy system info", Ui.Small, () =>
            {
                try { Clipboard.SetText(SystemInfo(_comps)); Toast.Show(this, "System info copied. Paste it into a bug report.", ToastKind.Info); } catch { }
            });
            _tips.SetToolTip(copyInfo, "Versions, Windows build, ports and folder, for bug reports. Never passwords.");
            more.Controls.Add(copyInfo);
            body.Controls.Add(more);

            // ---- footer: View on GitHub + Close
            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = Px(68), BackColor = Ui.Bg, Padding = new Padding(pad - Px(4), Px(14), pad - Px(4), Px(14)) };
            footer.Paint += (s, e) => { using (var pen = new Pen(Ui.Line)) e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0); };
            var close = new IconButton("Close", null, ButtonKind.Primary) { Margin = new Padding(0) };
            close.SetMin(Px(96), Px(40));
            close.Click += (s, e) => Close();
            var gh = new IconButton("View on GitHub", "external", ButtonKind.Outline) { Margin = new Padding(0, 0, Px(10), 0), IconColor = Ui.NavInk };
            gh.SetMin(0, Px(40));
            gh.Click += (s, e) => Sys.OpenUrl(Repo);
            footer.Controls.Add(close);
            footer.Controls.Add(gh);
            AcceptButton = close; CancelButton = close;

            Controls.Add(body);
            Controls.Add(footer);
            int h = body.Padding.Vertical + body.Controls.Cast<Control>().Sum(c => c.GetPreferredSize(new Size(inner, 0)).Height + c.Margin.Vertical) + footer.Height;
            ClientSize = new Size(w, Math.Min(h + Px(8), Screen.PrimaryScreen.WorkingArea.Height - Px(80)));
            Shown += (s, e) => CheckLatest();
        }

        static string Ver(string dir)
        {
            if (!Directory.Exists(dir)) return null;
            var v = WebHutConfig.FullVersion(dir);
            // Without the .webhut-version marker FullVersion falls back to the folder name.
            return v == Path.GetFileName(dir) && !char.IsDigit(v.Length > 0 ? v[0] : 'x') ? "installed" : v;
        }

        /// <summary>"2.4.65 (VS17)" → "2.4.65" for the badge; the tooltip keeps the full text.</summary>
        static string Short(string v) { int i = v.IndexOf(' '); return i > 0 ? v.Substring(0, i) : v; }

        static Label Heading(string text)
        {
            return new Label { Text = text.ToUpperInvariant(), Font = Ui.Caps, ForeColor = Ui.Muted, AutoSize = true, Margin = new Padding(0, 0, 0, Px(8)) };
        }

        static LinkLabel Link(string text, Font font, Action act)
        {
            var l = new LinkLabel { Text = text, Font = font, AutoSize = true, LinkColor = Ui.AccentInk, ActiveLinkColor = Ui.AccentHover, LinkBehavior = LinkBehavior.HoverUnderline, Margin = new Padding(0, 0, Px(14), 0), UseMnemonic = false };
            l.LinkClicked += (s, e) => act();
            return l;
        }

        static void OpenLicense()
        {
            var f = Path.Combine(K.Root, "LICENSE");
            if (File.Exists(f)) Sys.OpenUrl(f); else Sys.OpenUrl(Repo + "/blob/main/LICENSE");
        }

        /// <summary>Ask GitHub for the latest release tag (one request, only when this window opens).</summary>
        void CheckLatest()
        {
            Task.Run(() =>
            {
                Sys.EnableModernTls();
                var req = (HttpWebRequest)WebRequest.Create("https://api.github.com/repos/hashencoder/webhut/releases/latest");
                req.UserAgent = "WebHut/" + K.AppVersion;
                req.Accept = "application/vnd.github+json";
                req.Timeout = 8000;
                using (var resp = req.GetResponse())
                using (var sr = new StreamReader(resp.GetResponseStream()))
                    return Json.Str(Json.Parse(sr.ReadToEnd()), "tag_name");
            }).ContinueWith(t =>
            {
                if (IsDisposed) return;
                if (t.IsFaulted || string.IsNullOrEmpty(t.Result)) { _update.Text = "Couldn't check for updates (offline?)"; return; }
                if (WebHutConfig.VersionKey(t.Result) > WebHutConfig.VersionKey(K.AppVersion))
                {
                    _update.Text = "WebHut " + t.Result.TrimStart('v') + " is available";
                    _update.ForeColor = Ui.AccentInk;
                    _update.Font = Ui.SmallBold;
                    _download.Visible = true;
                }
                else { _update.Text = "✓ You're up to date"; _update.ForeColor = Ui.Green; }
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        static string SystemInfo(List<string[]> comps)
        {
            var cfg = Services.Cfg;
            var sb = new StringBuilder();
            sb.AppendLine("WebHut " + K.AppVersion);
            sb.AppendLine("Windows " + Environment.OSVersion.Version + (Environment.Is64BitOperatingSystem ? " x64" : "") + " · .NET " + Environment.Version);
            sb.AppendLine("Folder: " + K.Root);
            foreach (var c in comps) sb.AppendLine(c[0] + ": " + (c[1] ?? "not installed"));
            sb.AppendLine("Ports: HTTP " + cfg.HttpPort + ", MariaDB " + cfg.MysqlPort + (cfg.LanAccess ? " · network access on" : ""));
            sb.AppendLine("Running: Apache " + (Services.Apache.IsRunning ? "yes" : "no") + ", MariaDB " + (Services.MariaDb.IsRunning ? "yes" : "no"));
            sb.AppendLine("Visual C++ runtime: " + (Installer.VcRedistOk() ? "OK" : "missing or old"));
            return sb.ToString();
        }
    }

    public enum ChipKind { Plain, Off, Accent, Green }

    /// <summary>Small rounded badge: a label and an optional muted detail ("PHP 8.5.11").</summary>
    public class Chip : Painted
    {
        readonly string _detail; readonly ChipKind _kind;
        public Chip(string text, string detail, ChipKind kind)
        {
            Text = text; _detail = detail; _kind = kind;
            Font = kind == ChipKind.Plain || kind == ChipKind.Off ? Ui.BtnSmall : Ui.SmallBold;
            var a = TextRenderer.MeasureText(text, Font, Size.Empty, TextFormatFlags.NoPadding);
            int dw = detail == null ? 0 : S(6) + TextRenderer.MeasureText(detail, Ui.Small, Size.Empty, TextFormatFlags.NoPadding).Width;
            Size = new Size(S(10) + a.Width + dw + S(10) + 1, kind == ChipKind.Plain || kind == ChipKind.Off ? S(30) : S(24));
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Smooth(g);
            Color fill, line, ink, sub = Ui.Muted;
            switch (_kind)
            {
                case ChipKind.Accent: ink = Ui.AccentInk; fill = Ui.AccentSoft; line = Ui.AccentSoftLine; break;
                case ChipKind.Green: ink = Ui.Green; fill = Ui.GreenSoft; line = Ui.GreenSoft; break;
                case ChipKind.Off: ink = Ui.Faint; fill = Ui.Card; line = Ui.Line; sub = Ui.Faint; break;
                default: ink = Ui.Ink; fill = Ui.Bg; line = Ui.Line; break;
            }
            float r = _kind == ChipKind.Plain || _kind == ChipKind.Off ? S(8) : (Height - 1) / 2f;
            using (var p = Ui.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), r))
            {
                using (var b = new SolidBrush(fill)) g.FillPath(b, p);
                using (var pen = new Pen(line)) g.DrawPath(pen, p);
            }
            var f = TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
            var a = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(S(10), 0, a.Width + 2, Height), ink, f);
            if (_detail != null)
                TextRenderer.DrawText(g, _detail, Ui.Small, new Rectangle(S(10) + a.Width + S(6), 0, Width, Height), sub, f);
        }
    }

    /// <summary>A path in a rounded box with copy and open-folder buttons on the right.</summary>
    public class PathBox : Painted
    {
        readonly string _path;
        int _hot = -1;   // 0 = copy, 1 = open
        public event Action Copied;
        public PathBox(string path) { _path = path; Height = S(40); Cursor = Cursors.Default; }

        Rectangle Btn(int i) { int s = S(30); return new Rectangle(Width - S(5) - (2 - i) * (s + S(2)), (Height - s) / 2, s, s); }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int hot = Btn(0).Contains(e.Location) ? 0 : Btn(1).Contains(e.Location) ? 1 : -1;
            if (hot != _hot) { _hot = hot; Cursor = hot >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
            base.OnMouseMove(e);
        }
        protected override void OnMouseLeave(EventArgs e) { _hot = -1; base.OnMouseLeave(e); }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (Btn(0).Contains(e.Location)) { try { Clipboard.SetText(_path); if (Copied != null) Copied(); } catch { } }
            else if (Btn(1).Contains(e.Location)) Sys.OpenFolder(_path);
            base.OnMouseClick(e);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Smooth(g);
            using (var p = Ui.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), S(8)))
            {
                using (var b = new SolidBrush(Ui.Bg)) g.FillPath(b, p);
                using (var pen = new Pen(Ui.Line)) g.DrawPath(pen, p);
            }
            TextRenderer.DrawText(g, _path, Ui.Mono, new Rectangle(S(12), 0, Btn(0).Left - S(16), Height), Ui.Ink,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.PathEllipsis | TextFormatFlags.SingleLine);
            string[] icons = { "copy", "folder" };
            for (int i = 0; i < 2; i++)
            {
                var r = Btn(i);
                if (_hot == i) using (var hp = Ui.Rounded(new RectangleF(r.X, r.Y, r.Width, r.Height), S(6))) using (var b = new SolidBrush(Ui.Hover)) g.FillPath(b, hp);
                float isz = S(16);
                Icons.Draw(g, icons[i], new RectangleF(r.X + (r.Width - isz) / 2f, r.Y + (r.Height - isz) / 2f, isz, isz), _hot == i ? Ui.Ink : Ui.Muted);
            }
        }
    }
}
