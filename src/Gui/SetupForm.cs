using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WebHut
{
    /// <summary>
    /// First-run setup: looks up the latest official builds, lets the user untick optional ones or point at a zip
    /// they downloaded, then downloads, verifies and installs each one (with its own progress) and configures WebHut.
    /// </summary>
    public class SetupForm : Form
    {
        static readonly string[] Required = { "apache", "php" };
        static int Px(float v) { return Ui.Px(v); }

        readonly Panel _body;
        readonly Label _sub, _status, _vcText;
        readonly ProgressLine _bar;
        readonly IconButton _install, _cancel, _retry, _vcBtn, _logToggle;
        readonly NumberField _http, _mysql;
        readonly TextField _pw;
        readonly Callout _vc;
        readonly TextBox _log;
        readonly TableLayoutPanel _list;
        readonly List<Row> _rows = new List<Row>();
        readonly ToolTip _tips = new ToolTip();
        List<Component> _components = new List<Component>();
        CancellationTokenSource _cts;
        bool _working;
        Row _current;
        bool _filling;
        readonly HashSet<string> _unticked = new HashSet<string>();
        readonly HashSet<string> _ticked = new HashSet<string>();     // optional components the user chose
        readonly Action<string> _logHandler;

        public SetupForm()
        {
            Ui.Style(this);
            AutoScaleMode = AutoScaleMode.None;
            Text = "Set up WebHut";
            var wa = Screen.PrimaryScreen.WorkingArea;
            ClientSize = new Size(Math.Min(Px(860), wa.Width - Px(40)), Math.Min(Px(760), wa.Height - Px(60)));
            MinimumSize = new Size(Math.Min(Px(700), wa.Width), Math.Min(Px(520), wa.Height));
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Ui.Bg;

            // ---- header
            var head = new TableLayoutPanel { Dock = DockStyle.Top, Height = Px(92), ColumnCount = 2, BackColor = Ui.Bg, Padding = new Padding(Px(28), Px(22), Px(28), Px(8)) };
            head.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Px(62)));
            head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            head.Controls.Add(new Logo(Px(48)) { Margin = new Padding(0, Px(2), 0, 0) }, 0, 0);
            var titles = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, BackColor = Color.Transparent, Margin = new Padding(0) };
            titles.Controls.Add(new Label { Text = "Set up WebHut", Font = Ui.H2, ForeColor = Ui.Ink, AutoSize = true, Margin = new Padding(0) });
            _sub = new Label { Text = "Downloads the official Windows builds into " + K.Root + ". Nothing is installed system-wide.", Font = Ui.Base, ForeColor = Ui.Muted, AutoSize = true, Margin = new Padding(0, Px(4), 0, 0), UseMnemonic = false };
            titles.Controls.Add(_sub);
            head.Controls.Add(titles, 1, 0);
            head.SizeChanged += (s, e) => _sub.MaximumSize = new Size(Math.Max(Px(300), head.Width - Px(130)), 0);

            // ---- body
            _body = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Ui.Bg };
            var stack = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, BackColor = Ui.Bg, Padding = new Padding(Px(28), Px(6), Px(28), Px(16)) };
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _body.Controls.Add(stack);

            // components card
            var comps = Card(stack, "cube", "Components", "Apache and PHP are required. Untick anything else you don't need. MySQL, PostgreSQL, MongoDB and Adminer are optional; tick them here or add them later on the Databases page.");
            _retry = Small("Look up again", "refresh", ResolveAsync, "Find the latest versions again");
            AddTo(comps, Line(_retry));
            _list = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Dock = DockStyle.Top, BackColor = Color.Transparent, Margin = new Padding(0, Px(6), 0, 0) };
            _list.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            AddTo(comps, _list);
            AddTo(comps, new Label { Text = "Looking up the latest versions…", Font = Ui.Base, ForeColor = Ui.Muted, AutoSize = true, Name = "placeholder", Margin = new Padding(0, Px(8), 0, Px(8)) });

            // Visual C++ runtime
            _vc = new Callout(CalloutKind.Warning, "", Px(640)) { Margin = new Padding(0, 0, 0, Px(6)) };
            _vcText = new Label { Font = Ui.Bold, AutoSize = true, Margin = new Padding(0, Px(9), Px(10), 0) };
            _vcBtn = Small("Install it (needs admin)", "plus", InstallVc, "Runs Microsoft's installer; Windows asks for permission");
            var vcCard = Card(stack, "bolt", "Visual C++ runtime", "Apache and PHP for Windows need Microsoft's Visual C++ Redistributable 2017-2026 (x64).");
            AddTo(vcCard, _vc);
            AddTo(vcCard, Line(_vcText, _vcBtn));
            UpdateVc();

            // settings card
            var set = Card(stack, "gear", "Settings", "You can change these later in Settings.");
            _http = new NumberField(1, 65535, Px(120)) { Value = 80 };
            _mysql = new NumberField(1, 65535, Px(120)) { Value = 3306 };
            _pw = new TextField(Px(240), false);
            _pw.Box.UseSystemPasswordChar = true;
            AddRow(set, "HTTP port", "Apache · default 80", _http);
            AddRow(set, "MySQL port", "MariaDB · default 3306", _mysql);
            AddRow(set, "Database root password", "Leave empty for XAMPP-style passwordless root (fine for local-only use). Stored encrypted.", _pw);

            // details (log), collapsed
            _logToggle = Small("Show details", "file", ToggleLog, "Show what setup is doing");
            stack.Controls.Add(Line(_logToggle));
            _log = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, WordWrap = true, Font = Ui.LogFont, BackColor = Ui.LogBg, ForeColor = Ui.LogInk,
                                 BorderStyle = BorderStyle.None, Height = Px(160), Dock = DockStyle.Top, Visible = false, Tag = "keep-colors", Margin = new Padding(0, Px(8), 0, 0) };
            stack.Controls.Add(_log);
            Ui.ThemeScrollBars(_log, true);

            // ---- footer
            var footer = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = Px(76), ColumnCount = 3, BackColor = Ui.Card, Padding = new Padding(Px(24), Px(12), Px(24), Px(12)) };
            footer.Paint += (s, e) => { using (var pen = new Pen(Ui.Line)) e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0); };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var left = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, Dock = DockStyle.Fill, BackColor = Color.Transparent, Margin = new Padding(0) };
            _status = new Label { Text = "", Font = Ui.Small, ForeColor = Ui.Muted, AutoSize = true, Margin = new Padding(0, Px(2), 0, 0), UseMnemonic = false };
            _bar = new ProgressLine { Width = Px(280), Visible = false, Margin = new Padding(0, Px(8), 0, 0) };
            left.Controls.Add(_status); left.Controls.Add(_bar);
            left.SizeChanged += (s, e) => _status.MaximumSize = new Size(Math.Max(Px(200), left.Width - Px(10)), 0);
            _cancel = new IconButton("Cancel", null, ButtonKind.Outline) { Anchor = AnchorStyles.Right, Margin = new Padding(0, 0, Px(10), 0) };
            _cancel.SetMin(Px(100), Px(40));
            _cancel.Click += (s, e) => CancelOrClose();
            _install = new IconButton("Install", "play", ButtonKind.Primary) { Anchor = AnchorStyles.Right, Margin = new Padding(0), Enabled = false };
            _install.SetMin(Px(140), Px(40));
            _install.Click += (s, e) => InstallAsync();
            footer.Controls.Add(left, 0, 0);
            footer.Controls.Add(_cancel, 1, 0);
            footer.Controls.Add(_install, 2, 0);

            Controls.Add(ScrollHost.Wrap(_body));
            Controls.Add(head);
            Controls.Add(footer);
            AcceptButton = _install;

            _logHandler = line => { try { BeginInvoke(new Action(() => { if (!_log.IsDisposed) _log.AppendText(line + Environment.NewLine); })); } catch { } };
            Log.Line += _logHandler;
            FormClosed += (s, e) => Log.Line -= _logHandler;
            FormClosing += (s, e) => { if (_working && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; CancelOrClose(); } };
            Shown += (s, e) => ResolveAsync();
        }

        // ------------------------------------------------------------------ building blocks

        static TableLayoutPanel Card(TableLayoutPanel stack, string icon, string title, string desc)
        {
            var t = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, BackColor = Ui.Bg, Dock = DockStyle.Top,
                                           Padding = new Padding(Px(20), Px(16), Px(20), Px(12)), Margin = new Padding(0, 0, 0, Px(14)) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Px(220)));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            t.Paint += (s, e) =>
            {
                var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var p = Ui.Rounded(new RectangleF(0.5f, 0.5f, t.Width - 1.5f, t.Height - 1.5f), Px(12)))
                {
                    using (var b = new SolidBrush(Ui.Card)) g.FillPath(b, p);
                    using (var pen = new Pen(Ui.Line)) g.DrawPath(pen, p);
                }
            };
            var head = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, Px(2)) };
            head.Controls.Add(new IconBox(icon, Ui.AccentInk, Px(20)) { Margin = new Padding(0, Px(3), Px(10), 0) });
            head.Controls.Add(new Label { Text = title, Font = Ui.SectionTitle, ForeColor = Ui.Ink, AutoSize = true, BackColor = Color.Transparent, Margin = new Padding(0) });
            AddTo(t, head);
            AddTo(t, new Label { Text = desc, Font = Ui.Small, ForeColor = Ui.Muted, AutoSize = true, MaximumSize = new Size(Px(640), 0), BackColor = Color.Transparent, Margin = new Padding(Px(30), 0, 0, Px(8)) });
            stack.Controls.Add(t);
            return t;
        }

        static void AddTo(TableLayoutPanel t, Control c)
        {
            int r = t.RowCount++;
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.Controls.Add(c, 0, r);
            t.SetColumnSpan(c, 2);
        }

        static void AddRow(TableLayoutPanel t, string label, string hint, Control control)
        {
            int r = t.RowCount++;
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var l = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, BackColor = Color.Transparent, Margin = new Padding(0, Px(10), Px(12), Px(10)) };
            l.Controls.Add(new Label { Text = label, Font = Ui.Bold, ForeColor = Ui.Ink, AutoSize = true, BackColor = Color.Transparent, Margin = new Padding(0) });
            l.Controls.Add(new Label { Text = hint, Font = Ui.Small, ForeColor = Ui.Muted, AutoSize = true, MaximumSize = new Size(Px(200), 0), BackColor = Color.Transparent, Margin = new Padding(0, Px(2), 0, 0) });
            control.Margin = new Padding(0, Px(10), 0, Px(10));
            control.Anchor = AnchorStyles.Left | AnchorStyles.Top;
            t.Controls.Add(l, 0, r);
            t.Controls.Add(control, 1, r);
        }

        static FlowLayoutPanel Line(params Control[] items)
        {
            var f = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, BackColor = Color.Transparent, Margin = new Padding(0) };
            foreach (var c in items) f.Controls.Add(c);
            return f;
        }

        IconButton Small(string text, string icon, Action act, string tip)
        {
            var b = new IconButton(text, icon, ButtonKind.Outline) { Font = Ui.BtnSmall, IconColor = Ui.NavInk, Margin = new Padding(0, 0, Px(8), 0) };
            b.SetMin(0, Px(34));
            b.Click += (s, e) => act();
            if (tip != null) _tips.SetToolTip(b, tip);
            return b;
        }

        void ToggleLog()
        {
            _log.Visible = !_log.Visible;
            _logToggle.Text = _log.Visible ? "Hide details" : "Show details";
            _logToggle.Invalidate();
            if (_log.Visible) { _log.SelectionStart = _log.TextLength; _log.ScrollToCaret(); }
        }

        // ------------------------------------------------------------------ component rows

        /// <summary>One component: tick, name, version, status pill, detail line, progress, and fix-up links when it wasn't found.</summary>
        class Row : TableLayoutPanel
        {
            public readonly Component C;
            public readonly CheckMark Check = new CheckMark();
            public readonly Pill Pill = new Pill { Fit = true };
            public readonly Label Detail;
            public readonly ProgressLine Bar = new ProgressLine { Visible = false };
            public readonly LinkLabel Page, Zip;
            public bool Required;

            public Row(Component c, bool required, Action<Row> pickZip)
            {
                C = c; Required = required;
                AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; ColumnCount = 4; Dock = DockStyle.Top;
                BackColor = Color.Transparent; Margin = new Padding(0); Padding = new Padding(0, Px(10), 0, Px(10));
                ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Px(34)));
                ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                Check.Checked = true;
                Check.Enabled = !required;
                Check.Margin = new Padding(0, Px(2), 0, 0);
                Pill.Font = Ui.SmallBold; Pill.Height = Px(26); Pill.Margin = new Padding(Px(12), 0, 0, 0); Pill.Anchor = AnchorStyles.Right | AnchorStyles.Top;
                var name = new Label { Text = c.Name + (required ? "" : ""), Font = Ui.Bold, ForeColor = Ui.Ink, AutoSize = true, Margin = new Padding(0, Px(2), 0, 0), UseMnemonic = false };
                var ver = new Label { Text = c.Version ?? "", Font = Ui.Mono, ForeColor = Ui.Ink, AutoSize = true, Margin = new Padding(Px(8), Px(5), 0, 0), Anchor = AnchorStyles.Right | AnchorStyles.Top, UseMnemonic = false };
                Detail = new Label { Font = Ui.Small, ForeColor = Ui.Muted, AutoSize = true, Margin = new Padding(0, Px(4), 0, 0), UseMnemonic = false };
                Page = Link("Open download page");
                Page.LinkClicked += (s, e) => { if (C.Homepage != null) Sys.OpenUrl(C.Homepage); };
                Zip = Link("Use a zip I downloaded…");
                Zip.LinkClicked += (s, e) => pickZip(this);
                var links = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Color.Transparent, Margin = new Padding(0, Px(4), 0, 0) };
                links.Controls.Add(Page); links.Controls.Add(Zip);
                Bar.Margin = new Padding(0, Px(8), 0, 0);
                Bar.Width = Px(320);
                Controls.Add(Check, 0, 0);
                Controls.Add(name, 1, 0);
                Controls.Add(ver, 2, 0);
                Controls.Add(Pill, 3, 0);
                Controls.Add(Detail, 1, 1); SetColumnSpan(Detail, 3);
                Controls.Add(links, 1, 2); SetColumnSpan(links, 3);
                Controls.Add(Bar, 1, 3); SetColumnSpan(Bar, 3);
                Paint += (s, e) => { if (Parent != null && Parent.Controls.GetChildIndex(this) > 0) using (var pen = new Pen(Ui.Line)) e.Graphics.DrawLine(pen, 0, 0, Width, 0); };
                SizeChanged += (s, e) => Detail.MaximumSize = new Size(Math.Max(Px(200), Width - Px(60)), 0);
                Sync();
            }

            static LinkLabel Link(string text)
            {
                return new LinkLabel { Text = text, Font = Ui.Small, AutoSize = true, LinkColor = Ui.AccentInk, ActiveLinkColor = Ui.AccentHover, LinkBehavior = LinkBehavior.HoverUnderline, Margin = new Padding(0, 0, Px(14), 0) };
            }

            /// <summary>Show the looked-up state (before installing).</summary>
            public void Sync()
            {
                bool notFound = !C.Ready;
                Page.Visible = Zip.Visible = notFound || C.LocalFile != null;
                Page.Visible = notFound;
                if (C.LocalFile != null) { Pill.Set(PillState.Info, "Using your zip"); Detail.Text = Path.GetFileName(C.LocalFile); }
                else if (notFound) { Pill.Set(PillState.Error, "Not found"); Detail.Text = (C.Error ?? "No download found") + ". Download it yourself, then choose the zip."; Detail.ForeColor = Ui.Red; return; }
                else { Pill.Set(Check.Checked ? PillState.Running : PillState.Stopped, Check.Checked ? (C.Sha256 != null ? "Ready · verified" : "Ready") : Engines.IsOptional(C.Id) ? "Not added" : "Skipped"); Detail.Text = (Required ? "Required · " : Engines.IsOptional(C.Id) ? "Optional · " : "") + (C.FileName ?? ""); }
                Detail.ForeColor = Ui.Muted;
            }

            public void Stage(PillState state, string pill, string detail, int pct, bool bar)
            {
                Pill.Set(state, pill);
                if (detail != null) { Detail.Text = detail; Detail.ForeColor = state == PillState.Error ? Ui.Red : Ui.Muted; }
                Bar.Visible = bar;
                Bar.Value = pct;
            }
        }

        // ------------------------------------------------------------------ lookup

        void UpdateVc()
        {
            var v = Installer.VcRedistVersion();
            bool ok = Installer.VcRedistOk();
            _vc.Visible = !ok;
            if (!ok) _vc.Set(CalloutKind.Warning, "Visual C++ Redistributable 2017-2026 (x64) is " + (v == null ? "missing" : "too old (" + v.Major + "." + v.Minor + ")") + ". Apache and PHP won't start without it.");
            _vcText.Text = ok ? "✓  Installed (" + v.Major + "." + v.Minor + ")" : "";
            _vcText.ForeColor = Ui.Green;
            _vcText.Visible = ok;
            _vcBtn.Visible = !ok;
        }

        void ResolveAsync()
        {
            _install.Enabled = false;
            _retry.Enabled = false;
            SetStatus("Looking up the latest versions…", true);
            var cfg = Services.Cfg;
            var previous = _components.Where(c => c.Id != null && c.LocalFile != null).GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First().LocalFile);
            Task.Run(() => Setup.ResolveAll(cfg, true)).ContinueWith(t =>
            {
                if (IsDisposed) return;
                _retry.Enabled = true;
                _bar.Visible = false;
                if (t.IsFaulted) { Log.Error("Setup lookup failed: " + t.Exception.GetBaseException()); SetStatus("Lookup failed: " + t.Exception.GetBaseException().Message + " Check your internet connection and click Look up again.", false, Ui.Red); return; }
                _components = t.Result;
                foreach (var c in _components)
                {
                    string local;
                    if (c.Id != null && previous.TryGetValue(c.Id, out local) && local != null) { c.LocalFile = local; c.Error = null; }
                }
                FillList();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void FillList()
        {
            var ph = _list.Parent.Controls.Find("placeholder", false).FirstOrDefault();
            if (ph != null) ph.Visible = false;
            _filling = true;
            _list.SuspendLayout();
            foreach (var r in _rows) r.Dispose();
            _list.Controls.Clear(); _list.RowStyles.Clear(); _list.RowCount = 0;
            _rows.Clear();
            foreach (var c in _components)
            {
                var row = new Row(c, Required.Contains(c.Id), PickZip);
                // Keep what the user unticked across "Look up again"; components that weren't found start unticked.
                // Optional engines (MySQL, PostgreSQL, MongoDB, Adminer) start unticked unless the user picked them.
                string key = c.Id ?? c.Name;
                bool optional = Engines.IsOptional(c.Id);
                if (!row.Required && (!c.Ready || (optional ? !_ticked.Contains(key) : _unticked.Contains(key)))) row.Check.Checked = false;
                row.Check.CheckedChanged += (s, e) =>
                {
                    if (!_filling)
                    {
                        if (row.Check.Checked) { _unticked.Remove(key); _ticked.Add(key); } else { _unticked.Add(key); _ticked.Remove(key); }
                    }
                    row.Sync(); UpdateReady();
                };
                row.Sync();
                _rows.Add(row);
                int i = _list.RowCount++;
                _list.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                _list.Controls.Add(row, 0, i);
            }
            _list.ResumeLayout(true);
            _filling = false;
            Ui.ThemeTree(_list);
            UpdateReady();
        }

        void UpdateReady()
        {
            var chosen = _rows.Where(r => r.Check.Checked).ToList();
            bool requiredOk = _rows.Where(r => r.Required).All(r => r.C.Ready);
            int missing = chosen.Count(r => !r.C.Ready);
            _install.Enabled = requiredOk && missing == 0 && !_working;
            _install.Invalidate();
            if (_rows.Count == 0) return;
            if (!requiredOk) SetStatus("Apache and PHP are required. Download the missing one from its page, then choose the zip.", false, Ui.Red);
            else if (missing > 0) SetStatus(missing + " ticked component(s) weren't found. Choose a zip you downloaded, or untick them.", false, Ui.Amber);
            else SetStatus("Ready to install " + chosen.Count + " components. Each download is checked before it is installed.", false);
        }

        void PickZip(Row row)
        {
            var c = row.C;
            using (var d = new OpenFileDialog { Title = "Choose the " + c.Name + " download", Filter = c.Id == "composer" ? "PHAR|*.phar" : c.Id == "cacert" ? "PEM|*.pem" : "Zip archives|*.zip" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                c.LocalFile = d.FileName;
                c.Error = null;
                if (c.Version == null) c.Version = Path.GetFileNameWithoutExtension(d.FileName);
                if (c.Id == "php" && c.Branch == null)
                {
                    var m = System.Text.RegularExpressions.Regex.Match(Path.GetFileName(d.FileName), @"php-(\d+\.\d+)");
                    if (m.Success) c.Branch = m.Groups[1].Value;
                }
                row.Check.Checked = true;
                row.Sync();
                UpdateReady();
            }
        }

        void InstallVc()
        {
            _vcBtn.Enabled = false;
            var inst = new Installer(Services.Cfg);
            inst.Progress += (m, p) => { try { BeginInvoke(new Action(() => SetStatus(m, true, null, p))); } catch { } };
            Task.Run(() => inst.InstallVcRedist(CancellationToken.None)).ContinueWith(t =>
            {
                if (IsDisposed) return;
                _vcBtn.Enabled = true;
                _bar.Visible = false;
                if (t.IsFaulted) Ui.Error(this, "Visual C++ Redistributable", t.Exception.GetBaseException().Message);
                UpdateVc();
                UpdateReady();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void SetStatus(string text, bool busy, Color? color = null, int pct = -1)
        {
            _status.Text = text;
            _status.ForeColor = color ?? Ui.Muted;
            _bar.Visible = busy;
            _bar.Value = pct;
        }

        // ------------------------------------------------------------------ install

        void OnProgress(string msg, int pct)
        {
            if (InvokeRequired) { try { BeginInvoke(new Action(() => OnProgress(msg, pct))); } catch { } return; }
            var r = _current;
            if (r != null)
            {
                string pill = msg.StartsWith("Downloading") ? "Downloading" + (pct >= 0 ? " " + pct + "%" : "") : msg.StartsWith("Verifying") ? "Verifying" : msg.StartsWith("Unpacking") ? "Unpacking" : "Installing";
                r.Stage(PillState.Busy, pill, msg, pct, true);
            }
            SetStatus(msg, true, null, -1);
        }

        void OnStage(Component c, string stage)
        {
            if (InvokeRequired) { try { BeginInvoke(new Action(() => OnStage(c, stage))); } catch { } return; }
            if (c == null) { _current = null; SetStatus("Writing configuration and creating the database…", true); return; }
            var r = _rows.FirstOrDefault(x => x.C == c);
            if (r == null) return;
            switch (stage)
            {
                case "start": _current = r; r.Stage(PillState.Busy, "Starting", "Preparing…", -1, true); break;
                case "done": r.Stage(PillState.Running, "Installed", c.Name + " " + (c.Version ?? "") + " is in place.", 100, false); if (_current == r) _current = null; break;
                case "failed": r.Stage(PillState.Error, "Failed", null, -1, false); break;
            }
        }

        void InstallAsync()
        {
            var selected = _rows.Where(r => r.Check.Checked).Select(r => r.C).ToList();
            var notReady = selected.Where(c => !c.Ready).ToList();
            if (notReady.Count > 0)
            {
                Ui.Error(this, "WebHut", "These still need a zip or must be unticked:\n" + string.Join("\n", notReady.Select(c => "• " + c.Name)));
                return;
            }
            if (!_http.IsValid || !_mysql.IsValid) { Ui.Error(this, "WebHut", "Ports must be numbers from 1 to 65535."); return; }
            if (_http.Value == _mysql.Value) { Ui.Error(this, "WebHut", "HTTP and MySQL ports must differ."); return; }
            if (!Installer.VcRedistOk())
            {
                var a = MessageBox.Show(this, "The Visual C++ Redistributable is missing or old, so Apache probably won't start.\n\nContinue anyway? (You can install it later from Settings.)",
                    "WebHut", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (a != DialogResult.Yes) return;
            }

            var cfg = Services.Cfg;
            cfg.HttpPort = _http.Value;
            cfg.MysqlPort = _mysql.Value;
            cfg.MysqlRootPassword = _pw.Box.Text;

            foreach (var r in _rows) if (r.Check.Checked) r.Stage(PillState.Stopped, "Waiting", null, -1, false);
            SetWorking(true);
            _cts = new CancellationTokenSource();
            var inst = new Installer(cfg);
            inst.Progress += OnProgress;
            var ct = _cts.Token;
            Task.Run(() => Setup.Run(inst, selected, ct, OnStage)).ContinueWith(t =>
            {
                if (IsDisposed) return;
                SetWorking(false);
                _current = null;
                if (t.IsCanceled || (t.IsFaulted && t.Exception.GetBaseException() is OperationCanceledException))
                {
                    foreach (var r in _rows.Where(r => r.Pill.State == PillState.Busy || r.Pill.Text == "Waiting")) r.Stage(PillState.Stopped, "Cancelled", "Not installed.", -1, false);
                    SetStatus("Cancelled. Downloads finished so far are kept in tmp\\downloads and reused next time.", false, Ui.Amber);
                    return;
                }
                if (t.IsFaulted)
                {
                    var msg = t.Exception.GetBaseException().Message;
                    var failed = _rows.FirstOrDefault(r => r.Pill.State == PillState.Error || r.Pill.State == PillState.Busy);
                    if (failed != null) failed.Stage(PillState.Error, "Failed", msg, -1, false);
                    SetStatus("Setup failed: " + msg + "  Fix it and click Install again; finished downloads are reused.", false, Ui.Red);
                    Log.Error(msg);
                    if (!_log.Visible) ToggleLog();
                    return;
                }
                SetStatus("WebHut is ready. Click Start all in the next window, then open http://localhost.", false, Ui.Green);
                MessageBox.Show(this, "WebHut is ready.\n\nClick \"Start all\" in the next window, then open http://localhost.", "WebHut", MessageBoxButtons.OK, MessageBoxIcon.Information);
                DialogResult = DialogResult.OK;
                Close();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void SetWorking(bool on)
        {
            _working = on;
            _install.Enabled = !on;
            _install.Text = on ? "Installing…" : "Install";
            foreach (var r in _rows) { r.Check.Enabled = !on && !r.Required; r.Zip.Enabled = r.Page.Enabled = !on; }
            _retry.Enabled = _vcBtn.Enabled = !on;
            _http.Enabled = _mysql.Enabled = _pw.Enabled = !on;
            _cancel.Text = on ? "Stop" : "Cancel";
            _install.Invalidate(); _cancel.Invalidate();
        }

        void CancelOrClose()
        {
            if (_working) { if (_cts != null) _cts.Cancel(); SetStatus("Stopping after the current step…", true); return; }
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }
}
