using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WebHut
{
    /// <summary>About WebHut: version (with a check for a newer release), installed components, folder, links, credits.</summary>
    public class AboutForm : Form
    {
        const string Repo = "https://github.com/hashencoder/webhut";
        static int Px(float v) { return Ui.Px(v); }
        readonly Label _update;
        readonly LinkLabel _download;
        readonly ToolTip _tips = new ToolTip();

        public AboutForm()
        {
            Ui.Style(this);
            AutoScaleMode = AutoScaleMode.None;
            Text = "About WebHut";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            BackColor = Ui.Bg;
            int w = Px(560), inner = w - Px(56);

            var body = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, Dock = DockStyle.Fill, AutoScroll = true, BackColor = Ui.Bg, Padding = new Padding(Px(28), Px(26), Px(28), Px(12)) };

            // ---- hero
            var hero = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, Px(16)) };
            hero.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Px(80)));
            hero.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            hero.Controls.Add(new Logo(Px(64)) { Margin = new Padding(0) }, 0, 0);
            var titles = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, BackColor = Color.Transparent, Margin = new Padding(0, Px(2), 0, 0) };
            titles.Controls.Add(new Label { Text = "WebHut", Font = Ui.H1, ForeColor = Ui.Ink, AutoSize = true, Margin = new Padding(0) });
            titles.Controls.Add(new Label { Text = "Version " + K.AppVersion + "  ·  Free and open source (MIT)", Font = Ui.Base, ForeColor = Ui.Muted, AutoSize = true, Margin = new Padding(Px(2), Px(2), 0, 0) });
            var upd = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Color.Transparent, Margin = new Padding(Px(2), Px(6), 0, 0) };
            _update = new Label { Text = "Checking for a newer WebHut…", Font = Ui.Small, ForeColor = Ui.Muted, AutoSize = true, Margin = new Padding(0, 0, Px(8), 0), UseMnemonic = false };
            _download = Link("Download", () => Sys.OpenUrl(Repo + "/releases/latest"));
            _download.Visible = false;
            upd.Controls.Add(_update); upd.Controls.Add(_download);
            titles.Controls.Add(upd);
            hero.Controls.Add(titles, 1, 0);
            body.Controls.Add(hero);
            body.Controls.Add(new Label { Text = "A local web development stack for Windows: Apache, PHP, MariaDB, phpMyAdmin and Node.js in one folder, with one-click start, automatic database backups and safe configuration.",
                                          Font = Ui.Base, ForeColor = Ui.Ink, AutoSize = true, MaximumSize = new Size(inner, 0), Margin = new Padding(0, 0, 0, Px(16)) });

            // ---- installed components
            var comps = Card(inner, "Installed components");
            var cfg = Services.Cfg;
            var list = new List<string[]>
            {
                new[] { "Apache httpd", Ver(K.Apache), "Apache License 2.0" },
                new[] { "PHP" + (string.IsNullOrEmpty(cfg.Php) ? "" : " (active)"), string.IsNullOrEmpty(cfg.Php) ? null : Ver(cfg.PhpDir), "PHP License 3.01" },
                new[] { "MariaDB", Ver(K.MariaDb), "GPL v2" },
                new[] { "phpMyAdmin", Ver(K.Pma), "GPL v2" },
                new[] { "Node.js", cfg.NodeDir == null ? null : Ver(cfg.NodeDir), "MIT" },
            };
            var grid = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, BackColor = Color.Transparent, Margin = new Padding(0, Px(4), 0, 0) };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Px(170)));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Px(150)));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            foreach (var c in list)
            {
                int r = grid.RowCount++;
                grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                grid.Controls.Add(new Label { Text = c[0], Font = Ui.Base, ForeColor = Ui.Ink, AutoSize = true, Margin = new Padding(0, Px(4), 0, Px(4)) }, 0, r);
                grid.Controls.Add(new Label { Text = c[1] ?? "not installed", Font = c[1] == null ? Ui.Small : Ui.Mono, ForeColor = c[1] == null ? Ui.Faint : Ui.Ink, AutoSize = true, Margin = new Padding(0, Px(5), 0, Px(4)) }, 1, r);
                grid.Controls.Add(new Label { Text = c[2], Font = Ui.Small, ForeColor = Ui.Muted, AutoSize = true, Margin = new Padding(0, Px(5), 0, Px(4)) }, 2, r);
            }
            comps.Controls.Add(grid);
            body.Controls.Add(comps);

            // ---- folder
            var folder = Card(inner, "WebHut folder");
            folder.Controls.Add(new Label { Text = K.Root, Font = Ui.Mono, ForeColor = Ui.Ink, AutoSize = true, MaximumSize = new Size(inner - Px(40), 0), Margin = new Padding(0, Px(4), 0, Px(8)), UseMnemonic = false });
            folder.Controls.Add(Row(Btn("Open folder", "folder", () => Sys.OpenFolder(K.Root)), Btn("Licenses", "file", () => Sys.OpenFolder(Directory.Exists(Path.Combine(K.Root, "licenses")) ? Path.Combine(K.Root, "licenses") : K.Root))));
            body.Controls.Add(folder);

            // ---- links + credits
            body.Controls.Add(Row(Link("GitHub", () => Sys.OpenUrl(Repo)), Link("Documentation", () => Sys.OpenUrl(Repo + "/tree/main/docs")),
                                  Link("What's new", () => Sys.OpenUrl(Repo + "/releases")), Link("Report a problem", () => Sys.OpenUrl(Repo + "/issues/new"))));
            body.Controls.Add(new Label { Text = "Uses the official Windows builds of Apache (Apache Lounge), PHP, MariaDB, phpMyAdmin and Node.js, downloaded from their publishers. Fonts: Inter and JetBrains Mono (SIL Open Font License).",
                                          Font = Ui.Small, ForeColor = Ui.Muted, AutoSize = true, MaximumSize = new Size(inner, 0), Margin = new Padding(0, Px(12), 0, 0) });

            // ---- footer
            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = Px(68), BackColor = Ui.Card, Padding = new Padding(Px(20), Px(14), Px(20), Px(14)) };
            footer.Paint += (s, e) => { using (var pen = new Pen(Ui.Line)) e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0); };
            var close = new IconButton("Close", null, ButtonKind.Primary) { Margin = new Padding(0) };
            close.SetMin(Px(100), Px(40));
            close.Click += (s, e) => Close();
            var copy = new IconButton("Copy system info", "copy", ButtonKind.Outline) { Margin = new Padding(0, 0, Px(10), 0), IconColor = Ui.NavInk };
            copy.SetMin(0, Px(40));
            copy.Click += (s, e) =>
            {
                try { Clipboard.SetText(SystemInfo(list)); Toast.Show(this, "System info copied. Paste it into a bug report.", ToastKind.Info); } catch { }
            };
            _tips.SetToolTip(copy, "Versions, Windows build, ports and folder, for bug reports. No passwords.");
            footer.Controls.Add(close);
            footer.Controls.Add(copy);
            AcceptButton = close; CancelButton = close;

            Controls.Add(body);
            Controls.Add(footer);
            int h = body.Padding.Vertical + body.Controls.Cast<Control>().Sum(c => c.GetPreferredSize(new Size(inner, 0)).Height + c.Margin.Vertical) + footer.Height;
            ClientSize = new Size(w, Math.Min(h + Px(16), Screen.PrimaryScreen.WorkingArea.Height - Px(80)));
            Shown += (s, e) => CheckLatest();
        }

        static string Ver(string dir)
        {
            if (!Directory.Exists(dir)) return null;
            var v = WebHutConfig.FullVersion(dir);
            // Without the .webhut-version marker FullVersion falls back to the folder name.
            return v == Path.GetFileName(dir) && !char.IsDigit(v.Length > 0 ? v[0] : 'x') ? "installed" : v;
        }

        FlowLayoutPanel Card(int width, string title)
        {
            var p = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, MinimumSize = new Size(width, 0), BackColor = Ui.Bg,
                                          Padding = new Padding(Px(18), Px(14), Px(18), Px(12)), Margin = new Padding(0, 0, 0, Px(12)) };
            p.Paint += (s, e) =>
            {
                var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = Ui.Rounded(new RectangleF(0.5f, 0.5f, p.Width - 1.5f, p.Height - 1.5f), Px(12)))
                {
                    using (var b = new SolidBrush(Ui.Card)) g.FillPath(b, path);
                    using (var pen = new Pen(Ui.Line)) g.DrawPath(pen, path);
                }
            };
            p.Controls.Add(new Label { Text = title, Font = Ui.SectionTitle, ForeColor = Ui.Ink, AutoSize = true, BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, Px(4)) });
            return p;
        }

        static FlowLayoutPanel Row(params Control[] items)
        {
            var f = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Color.Transparent, Margin = new Padding(0) };
            foreach (var c in items) f.Controls.Add(c);
            return f;
        }

        IconButton Btn(string text, string icon, Action act)
        {
            var b = new IconButton(text, icon, ButtonKind.Outline) { Font = Ui.BtnSmall, IconColor = Ui.NavInk, Margin = new Padding(0, 0, Px(8), 0) };
            b.SetMin(0, Px(34));
            b.Click += (s, e) => act();
            return b;
        }

        static LinkLabel Link(string text, Action act)
        {
            var l = new LinkLabel { Text = text, Font = Ui.Base, AutoSize = true, LinkColor = Ui.AccentInk, ActiveLinkColor = Ui.AccentHover, LinkBehavior = LinkBehavior.HoverUnderline, Margin = new Padding(0, 0, Px(18), 0) };
            l.LinkClicked += (s, e) => act();
            return l;
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
                if (t.IsFaulted || string.IsNullOrEmpty(t.Result)) { _update.Text = "Couldn't check for a newer version (offline?)."; return; }
                var latest = WebHutConfig.VersionKey(t.Result);
                if (latest > WebHutConfig.VersionKey(K.AppVersion))
                {
                    _update.Text = "WebHut " + t.Result.TrimStart('v') + " is available.";
                    _update.ForeColor = Ui.AccentInk;
                    _update.Font = Ui.SmallBold;
                    _download.Visible = true;
                }
                else { _update.Text = "✓ You have the latest version."; _update.ForeColor = Ui.Green; }
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
}
