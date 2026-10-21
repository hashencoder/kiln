using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WebHut
{
    public class MainForm : Form
    {
        class Row
        {
            public Service Svc;
            public Label Detail, Pid, Port;
            public Pill Status;
            public IconButton Toggle;
        }

        readonly TableLayoutPanel _grid = new TableLayoutPanel();
        readonly RichTextBox _log = new RichTextBox();
        readonly List<Row> _rows = new List<Row>();
        readonly Timer _timer = new Timer { Interval = 2000 };
        readonly Timer _backupTimer = new Timer { Interval = 10 * 60 * 1000 };
        volatile bool _backingUp;
        readonly NotifyIcon _tray = new NotifyIcon();
        Label _sub, _summaryDot, _summary;
        FlowLayoutPanel _tiles;
        IconButton _allBtn;
        ToolStripMenuItem _trayAll;
        Switch _autoScroll;
        bool _exiting, _refreshing, _trayHintShown;
        bool _anyRunning, _sequenceRunning;

        WebHutConfig Cfg { get { return Services.Cfg; } }

        static int Px(float v) { return Ui.Px(v); }

        public MainForm()
        {
            Ui.Style(this);
            AutoScaleMode = AutoScaleMode.None; // every size below is already in physical pixels (Ui.Px)
            Text = "WebHut";
            ClientSize = new Size(Px(1340), Px(880));
            MinimumSize = new Size(Px(1200), Px(760));
            StartPosition = FormStartPosition.CenterScreen;
            DoubleBuffered = true;

            var main = new Panel { Dock = DockStyle.Fill, BackColor = Ui.Bg, Padding = new Padding(Px(30), Px(24), Px(30), Px(24)), AutoScroll = true };
            var stack = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = Ui.Bg, MinimumSize = new Size(0, Px(700)) };
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            stack.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            stack.Controls.Add(BuildHeader(), 0, 0);
            stack.Controls.Add(BuildServicesCard(), 0, 1);
            stack.Controls.Add(BuildQuickActions(), 0, 2);
            stack.Controls.Add(BuildActivity(), 0, 3);
            main.Controls.Add(stack);

            Controls.Add(main);
            Controls.Add(BuildSidebar());

            BuildTray();
            Log.Line += line => Ui.AppendDarkLog(_log, line, _autoScroll == null || _autoScroll.Checked);
            _timer.Tick += (s, e) => RefreshStatus();
            _backupTimer.Tick += (s, e) => ScheduledBackup();
            Load += OnLoad;
            FormClosing += OnClosing;
        }

        // ------------------------------------------------------------------ layout

        Control BuildSidebar()
        {
            var side = new Panel { Dock = DockStyle.Left, Width = Px(240), BackColor = Ui.Sidebar, Padding = new Padding(Px(14), Px(20), Px(14), Px(16)) };
            side.Paint += (s, e) => { using (var p = new Pen(Ui.Line)) e.Graphics.DrawLine(p, side.Width - 1, 0, side.Width - 1, side.Height); };
            int w = Px(240 - 28 - 1);

            var brand = new TableLayoutPanel { Dock = DockStyle.Top, Height = Px(68), ColumnCount = 2, RowCount = 2, BackColor = Ui.Sidebar, Padding = new Padding(Px(6), 0, 0, 0) };
            brand.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Px(60)));
            brand.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            brand.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
            brand.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
            var logo = new Logo(Px(46)) { Margin = new Padding(0, Px(4), 0, 0) };
            brand.Controls.Add(logo, 0, 0); brand.SetRowSpan(logo, 2);
            var name = new Label { Text = "WebHut", Font = Ui.Brand, ForeColor = Ui.Ink, AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Bottom, Margin = new Padding(0) };
            var ver = new Label { Text = "v" + K.AppVersion, Font = Ui.Base, ForeColor = Ui.Muted, AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Top, Margin = new Padding(Px(1), 0, 0, 0) };
            brand.Controls.Add(name, 1, 0);
            brand.Controls.Add(ver, 1, 1);

            var nav = new FlowLayoutPanel { Dock = DockStyle.Top, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, BackColor = Ui.Sidebar, Padding = new Padding(0, Px(18), 0, 0) };
            var dash = Nav(nav, w, "Dashboard", "home", null); dash.Active = true;
            Nav(nav, w, "Open localhost", "globe", () => Sys.OpenUrl(Services.Apache.Url));
            Nav(nav, w, "phpMyAdmin", "database", () => Sys.OpenUrl(Services.MariaDb.Url));
            Nav(nav, w, "www folder", "folder", () => Sys.OpenFolder(K.Www));
            Nav(nav, w, "Shell", "terminal", () => Services.OpenShell());
            Nav(nav, w, "Refresh sites", "refresh", RefreshSites);
            Nav(nav, w, "Node apps", "cube", ShowNodeApps);
            Nav(nav, w, "Database tools", "server", () => { using (var d = new DatabaseForm()) d.ShowDialog(this); });
            Nav(nav, w, "Log files", "logs", () => Sys.OpenFolder(K.Logs));

            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, BackColor = Ui.Sidebar };
            var sep = new Panel { Height = 1, Width = w, BackColor = Ui.Line, Margin = new Padding(0, 0, 0, Px(10)) };
            bottom.Controls.Add(sep);
            Nav(bottom, w, "Settings", "gear", ShowSettings);
            Nav(bottom, w, "About", "info", ShowAbout);
            Nav(bottom, w, "Quit WebHut", "power", ExitApp);

            side.Controls.Add(nav);
            side.Controls.Add(brand);
            side.Controls.Add(bottom);
            return side;
        }

        NavItem Nav(FlowLayoutPanel host, int width, string text, string icon, Action act)
        {
            var n = new NavItem(text, icon) { Width = width };
            if (act != null) n.Click += (s, e) => act();
            host.Controls.Add(n);
            return n;
        }

        Control BuildHeader()
        {
            var t = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, BackColor = Ui.Bg, Margin = new Padding(0, 0, 0, Px(18)) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var titles = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(0), BackColor = Ui.Bg };
            titles.Controls.Add(new Label { Text = "WebHut", Font = Ui.H1, ForeColor = Ui.Ink, AutoSize = true, Margin = new Padding(0) });
            titles.Controls.Add(new Label { Text = "Local development environment", Font = Ui.Nav, ForeColor = Ui.Muted, AutoSize = true, Margin = new Padding(Px(2), 0, 0, Px(4)) });
            _sub = new Label { Text = "", Font = Ui.Base, ForeColor = Ui.Muted, AutoSize = true, Margin = new Padding(Px(2), 0, 0, 0) };
            titles.Controls.Add(_sub);

            // One button that flips between "Start all" and "Stop all" with the servers' state.
            _allBtn = new IconButton("Start all", "play", ButtonKind.Primary) { Font = Ui.NavBold, Anchor = AnchorStyles.Right | AnchorStyles.Top, Margin = new Padding(0, Px(10), 0, 0) };
            _allBtn.SetMin(Px(170), Px(48));
            _allBtn.Click += (s, e) => ToggleAll();

            t.Controls.Add(titles, 0, 0);
            t.Controls.Add(_allBtn, 1, 0);
            return t;
        }

        /// <summary>Card heading: accent icon + title on the left, optional controls on the right.</summary>
        static TableLayoutPanel CardHeader(string icon, string title, Control right)
        {
            var h = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, BackColor = Ui.Card, Margin = new Padding(0, 0, 0, Px(14)) };
            h.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            h.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var left = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Ui.Card, Margin = new Padding(0) };
            left.Controls.Add(new IconBox(icon, Ui.Accent, Px(24)) { Margin = new Padding(0, Px(3), Px(12), 0) });
            left.Controls.Add(new Label { Text = title, Font = Ui.H2, ForeColor = Ui.Ink, AutoSize = true, Margin = new Padding(0) });
            h.Controls.Add(left, 0, 0);
            if (right != null) { right.Anchor = AnchorStyles.Right; h.Controls.Add(right, 1, 0); }
            return h;
        }

        Control BuildServicesCard()
        {
            var card = new Card { Dock = DockStyle.Top, AutoSize = true, Margin = new Padding(0, 0, 0, Px(16)) };

            var summary = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Ui.Card, Margin = new Padding(0) };
            _summaryDot = new Label { Text = "●", Font = Ui.Nav, ForeColor = Ui.Grey, AutoSize = true, Margin = new Padding(0, 0, Px(4), 0) };
            _summary = new Label { Text = "", Font = Ui.Base, ForeColor = Ui.Ink, AutoSize = true, Margin = new Padding(0, Px(2), 0, 0) };
            summary.Controls.Add(_summaryDot); summary.Controls.Add(_summary);

            // The table sits in a rounded frame; the frame paints the header band.
            var frame = new Card { Dock = DockStyle.Top, AutoSize = true, Radius = Px(10), BackColor = Ui.Card, Padding = new Padding(1) };
            frame.Paint += (s, e) =>
            {
                var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
                int head = _grid.GetRowHeights().Length > 0 ? _grid.GetRowHeights()[0] : Px(42);
                using (var path = Ui.Rounded(new RectangleF(0.5f, 0.5f, frame.Width - 1.5f, frame.Height - 1.5f), Px(10)))
                {
                    g.SetClip(path);
                    using (var b = new SolidBrush(Ui.HeadBg)) g.FillRectangle(b, 0, 0, frame.Width, head + 1);
                    g.ResetClip();
                    using (var pen = new Pen(Ui.Line)) g.DrawPath(pen, path);
                }
            };
            _grid.Dock = DockStyle.Top;
            _grid.AutoSize = true;
            _grid.BackColor = Color.Transparent;
            _grid.ColumnCount = 6;
            _grid.Padding = new Padding(Px(14), 0, Px(10), 0);
            _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Px(168)));  // service
            _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));       // details
            _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Px(70)));   // pid
            _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Px(70)));   // port
            _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Px(126)));  // status
            _grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));           // actions
            _grid.CellPaint += (s, e) =>
            {
                if (e.Row < _grid.RowCount - 1)
                    using (var p = new Pen(Ui.Line))
                        e.Graphics.DrawLine(p, e.CellBounds.Left - (e.Column == 0 ? Px(14) : 0), e.CellBounds.Bottom - 1,
                            e.CellBounds.Right + (e.Column == 5 ? Px(10) : 0), e.CellBounds.Bottom - 1);
            };
            frame.Controls.Add(_grid);

            card.Controls.Add(frame);
            card.Controls.Add(CardHeader("pulse", "Services", summary));
            return card;
        }

        Control BuildQuickActions()
        {
            var card = new Card { Dock = DockStyle.Top, AutoSize = true, Margin = new Padding(0, 0, 0, Px(16)), Padding = new Padding(Px(20), Px(16), Px(8), Px(6)) };
            _tiles = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, BackColor = Ui.Card, Margin = new Padding(0) };
            AddTile("Open localhost", "http://localhost", "globe", () => Sys.OpenUrl(Services.Apache.Url));
            AddTile("phpMyAdmin", "Open database UI", "database", () => Sys.OpenUrl(Services.MariaDb.Url));
            AddTile("www folder", "Open in Explorer", "folder", () => Sys.OpenFolder(K.Www));
            AddTile("Shell", "Open terminal", "terminal", () => Services.OpenShell());
            AddTile("Refresh sites", "Reload configuration", "refresh", RefreshSites);
            AddTile("Node apps", "Manage Node.js apps", "cube", ShowNodeApps);
            _tiles.Resize += (s, e) => LayoutTiles();
            card.Controls.Add(_tiles);
            card.Controls.Add(CardHeader("bolt", "Quick Actions", null));
            return card;
        }

        void AddTile(string title, string sub, string icon, Action act)
        {
            var t = new Tile(title, sub, icon);
            t.Click += (s, e) => act();
            _tiles.Controls.Add(t);
        }

        /// <summary>Six equal tiles per row; three per row when the window is narrow.</summary>
        void LayoutTiles()
        {
            int avail = _tiles.ClientSize.Width;
            if (avail <= 0) return;
            int gap = Px(12), n = _tiles.Controls.Count;
            int perRow = avail >= Px(128) * n + gap * n ? n : (n + 1) / 2;
            int w = (avail - gap * perRow) / perRow;
            foreach (Control c in _tiles.Controls) if (c.Width != w) c.Width = w;
        }

        Control BuildActivity()
        {
            var card = new Card { Dock = DockStyle.Fill, Margin = new Padding(0), MinimumSize = new Size(0, Px(200)) };

            var right = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Ui.Card, Margin = new Padding(0) };
            var clear = new IconButton("Clear", "trash", ButtonKind.Outline) { IconColor = Ui.NavInk, Margin = new Padding(0, 0, Px(16), 0) };
            clear.Click += (s, e) => _log.Clear();
            _autoScroll = new Switch { Checked = true };
            right.Controls.Add(clear);
            right.Controls.Add(_autoScroll);
            right.Controls.Add(new Label { Text = "Auto-scroll", Font = Ui.Base, ForeColor = Ui.Ink, AutoSize = true, Margin = new Padding(0, Px(7), 0, 0) });

            var dark = new Card { Dock = DockStyle.Fill, Fill = Ui.LogBg, Border = Color.Transparent, BackColor = Ui.Card, Radius = Px(10), Padding = new Padding(Px(16), Px(12), Px(8), Px(10)) };
            _log.ReadOnly = true;
            _log.BorderStyle = BorderStyle.None;
            _log.BackColor = Ui.LogBg;
            _log.ForeColor = Ui.LogInk;
            _log.Font = Ui.LogFont;
            _log.Dock = DockStyle.Fill;
            _log.DetectUrls = false;
            _log.WordWrap = true;
            _log.ScrollBars = RichTextBoxScrollBars.Vertical;
            dark.Controls.Add(_log);

            card.Controls.Add(dark);
            card.Controls.Add(CardHeader("file", "Activity", right));
            return card;
        }

        static string IconFor(Service svc)
        {
            if (svc == Services.Apache) return "server";
            if (svc == Services.MariaDb) return "database";
            return "cube";
        }

        void BuildRows()
        {
            _grid.SuspendLayout();
            _grid.Controls.Clear();
            _grid.RowStyles.Clear();
            _rows.Clear();
            _grid.RowCount = 0;

            AddHeaderRow();
            foreach (var svc in Services.All())
            {
                var r = new Row { Svc = svc };
                var row = r;
                var nameCell = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Color.Transparent, Margin = new Padding(0, Px(14), Px(8), Px(14)), Anchor = AnchorStyles.Left };
                nameCell.Controls.Add(new IconBox(IconFor(svc), Ui.Accent, Px(22)) { Margin = new Padding(0, Px(1), Px(12), 0) });
                nameCell.Controls.Add(new Label { Text = svc.Title, Font = Ui.NavBold, ForeColor = Ui.Ink, AutoSize = true, BackColor = Color.Transparent, Margin = new Padding(0, Px(1), 0, 0) });

                r.Detail = CellLabel(svc.Detail, Ui.Ink);
                r.Detail.AutoSize = false; r.Detail.AutoEllipsis = true; r.Detail.Dock = DockStyle.Fill; r.Detail.TextAlign = ContentAlignment.MiddleLeft;
                r.Pid = CellLabel("-", Ui.Muted);
                r.Port = CellLabel(svc.Port.ToString(), Ui.Ink);
                r.Status = new Pill { Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, Px(8), 0) };
                r.Status.Size = new Size(Px(124), Px(32));
                r.Status.Set(PillState.Stopped, "Stopped");

                r.Toggle = new IconButton("Start", "play", ButtonKind.Primary);
                r.Toggle.SetMin(Px(88), 0);
                r.Toggle.Click += (s, e) => Toggle(row);
                var open = new IconButton("Open", "external", ButtonKind.Outline) { IconColor = Ui.NavInk };
                open.Click += (s, e) => Sys.OpenUrl(row.Svc.Url);
                var logs = new IconButton("Logs", "file", ButtonKind.Outline) { IconColor = Ui.NavInk };
                logs.Click += (s, e) => Sys.OpenInEditor(row.Svc.LogFile);
                var conf = new IconButton("Config", "gear", ButtonKind.Outline) { IconColor = Ui.NavInk, Margin = new Padding(0) };
                conf.Click += (s, e) => Sys.OpenInEditor(row.Svc.ConfigFile);

                var actions = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, BackColor = Color.Transparent, Margin = new Padding(0, Px(10), 0, Px(10)), Anchor = AnchorStyles.Left };
                actions.Controls.AddRange(new Control[] { r.Toggle, open, logs, conf });

                int i = _grid.RowCount++;
                _grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                _grid.Controls.Add(nameCell, 0, i);
                _grid.Controls.Add(r.Detail, 1, i);
                _grid.Controls.Add(r.Pid, 2, i);
                _grid.Controls.Add(r.Port, 3, i);
                _grid.Controls.Add(r.Status, 4, i);
                _grid.Controls.Add(actions, 5, i);
                _rows.Add(r);
            }
            _grid.ResumeLayout();
            RefreshStatus();
        }

        static Label CellLabel(string text, Color color)
        {
            return new Label { Text = text, Font = Ui.Nav, ForeColor = color, AutoSize = true, BackColor = Color.Transparent, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, Px(8), 0) };
        }

        void AddHeaderRow()
        {
            int i = _grid.RowCount++;
            _grid.RowStyles.Add(new RowStyle(SizeType.Absolute, Px(42)));
            string[] heads = { "SERVICE", "DETAILS", "PID", "PORT", "STATUS", "ACTIONS" };
            for (int c = 0; c < heads.Length; c++)
                _grid.Controls.Add(new Label { Text = heads[c], Font = Ui.Caps, ForeColor = Ui.Muted, AutoSize = true, BackColor = Color.Transparent, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, Px(8), 0) }, c, i);
        }

        void ShowAbout()
        {
            var answer = MessageBox.Show(this,
                "WebHut " + K.AppVersion + "\n\nA portable local development stack for Windows: Apache, PHP, MariaDB, phpMyAdmin and Node.js in one folder.\n\n" +
                "Folder: " + K.Root + "\nFree and open source (MIT License).\n\nOpen the project page on GitHub?",
                "About WebHut", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (answer == DialogResult.Yes) Sys.OpenUrl("https://github.com/hashencoder/webhut");
        }

        // ------------------------------------------------------------------ behaviour

        void OnLoad(object sender, EventArgs e)
        {
            Log.Info("WebHut " + K.AppVersion + " · " + K.Root);
            try { new Installer(Cfg).RelocateIfMoved(); } catch (Exception ex) { Log.Error(ex.Message); }
            UpdateSubtitle();
            BuildRows();
            LayoutTiles();
            _timer.Start();
            _backupTimer.Start();
            if (K.RootWarning() != null) Log.Warn(K.RootWarning());
            if (!Installer.VcRedistOk())
                Log.Warn("Visual C++ Redistributable 2017-2026 (x64) looks missing or old. If Apache won't start, install it from Settings.");
            if (Cfg.StartOnLaunch) StartAll();
        }

        void UpdateSubtitle()
        {
            _sub.Text = K.Root + "   ·   PHP " + (string.IsNullOrEmpty(Cfg.Php) ? "-" : WebHutConfig.FullVersion(Cfg.PhpDir)) +
                        "   ·   Node " + (Cfg.NodeDir == null ? "-" : WebHutConfig.FullVersion(Cfg.NodeDir));
            if (Cfg.LanAccess)
            {
                var ips = Sys.LanAddresses();
                _sub.Text += "   ·   Network: " + (ips.Count == 0 ? "no network" : "http://" + ips[0] + (Cfg.HttpPort == 80 ? "" : ":" + Cfg.HttpPort));
            }
        }

        void RefreshStatus()
        {
            if (_refreshing || _rows.Count == 0) return;
            _refreshing = true;
            var rows = _rows.ToList();
            Task.Run(() => rows.Select(r => new { r, pid = SafePid(r.Svc) }).ToList())
                .ContinueWith(t =>
                {
                    _refreshing = false;
                    if (IsDisposed || t.IsFaulted) return;
                    foreach (var x in t.Result)
                    {
                        var r = x.r;
                        bool running = x.pid > 0;
                        if (r.Svc.Busy)
                        {
                            r.Status.Set(PillState.Busy, "Working…");
                            continue;
                        }
                        r.Status.Set(running ? PillState.Running : PillState.Stopped, running ? "Running" : "Stopped");
                        r.Pid.Text = running ? x.pid.ToString() : "-";
                        r.Pid.ForeColor = running ? Ui.Ink : Ui.Muted;
                        r.Port.Text = r.Svc.Port.ToString();
                        r.Detail.Text = r.Svc.Detail;
                        r.Toggle.Enabled = true;
                        r.Toggle.Set(running ? "Stop" : "Start", running ? "stop" : "play", ButtonKind.Primary);
                    }
                    int up = t.Result.Count(x => x.pid > 0), total = t.Result.Count;
                    _tray.Text = "WebHut - " + up + " of " + total + " running";
                    _summary.Text = up == total ? "All services running" : up == 0 ? "All services stopped" : up + " of " + total + " running";
                    _summaryDot.ForeColor = up == total ? Ui.Green : up == 0 ? Ui.Grey : Ui.Amber;
                    _anyRunning = up > 0;
                    UpdateAllButton();
                }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        static int SafePid(Service s) { try { return s.Pid(); } catch { return 0; } }

        void ScheduledBackup()
        {
            var cfg = Cfg;
            if (_backingUp || !cfg.AutoBackup || cfg.BackupEveryHours <= 0 || Services.MariaDb.Busy) return;
            _backingUp = true;
            Task.Run(() =>
            {
                try { if (Services.MariaDb.IsRunning) Backup.CreateIfDue(cfg, TimeSpan.FromHours(cfg.BackupEveryHours), "scheduled"); }
                finally { _backingUp = false; }
            });
        }

        /// <summary>After MariaDB fails to start, offer to rebuild it from the newest backup.</summary>
        void OfferRebuild()
        {
            var latest = Backup.List().FirstOrDefault();
            if (latest == null) return;
            var answer = MessageBox.Show(this,
                "MariaDB couldn't start. If this happened after a crash or power cut, its files may be damaged.\n\n" +
                "Rebuild from your latest backup (" + latest.Time.ToString("g") + ")?\n\n" +
                "The current data folder is kept as data\\mariadb-damaged-<date>, nothing is deleted. Changes made after the backup won't be in the rebuilt database.",
                "Rebuild database?", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (answer == DialogResult.Yes)
                Run(Services.MariaDb, () => Backup.Rebuild(Cfg, latest.Path), "Rebuilding…");
        }

        void Toggle(Row r)
        {
            bool running = r.Svc.IsRunning;
            Run(r.Svc, running ? (Action)r.Svc.Stop : r.Svc.Start, running ? "Stopping…" : "Starting…");
        }

        void Run(Service svc, Action act, string busyText)
        {
            if (svc.Busy) return;
            svc.Busy = true;
            var row = _rows.FirstOrDefault(x => x.Svc == svc);
            if (row != null) { row.Toggle.Enabled = false; row.Status.Set(PillState.Busy, busyText); }
            Task.Run(act).ContinueWith(t =>
            {
                svc.Busy = false;
                if (t.IsFaulted)
                {
                    var msg = t.Exception.GetBaseException().Message;
                    Log.Error(svc.Title + ": " + msg);
                    if (Visible) Ui.Error(this, svc.Title, msg);
                    if (svc == Services.MariaDb && busyText.StartsWith("Start") && Visible) OfferRebuild();
                }
                RefreshStatus();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        /// <summary>Start everything when nothing is running; otherwise stop everything.</summary>
        void ToggleAll()
        {
            if (_sequenceRunning) return;
            if (_anyRunning) StopAll(); else StartAll();
        }

        void UpdateAllButton()
        {
            if (_allBtn == null || _sequenceRunning) return;
            _allBtn.Enabled = true;
            if (_anyRunning) _allBtn.Set("Stop all", "stop", ButtonKind.Outline);
            else _allBtn.Set("Start all", "play", ButtonKind.Primary);
            _allBtn.Font = Ui.NavBold;
            if (_trayAll != null) _trayAll.Text = _allBtn.Text;
        }

        void StartAll()
        {
            var list = Services.All().Where(s => !(s is NodeService) || ((NodeService)s).App.AutoStart).ToList();
            RunSequence(list, s => s.Start(), true);
        }

        void StopAll()
        {
            var list = Services.All();
            list.Reverse();
            RunSequence(list, s => s.Stop(), false);
        }

        void RunSequence(List<Service> list, Action<Service> act, bool starting)
        {
            if (_sequenceRunning) return;
            _sequenceRunning = true;
            if (_allBtn != null) { _allBtn.Enabled = false; _allBtn.Text = starting ? "Starting…" : "Stopping…"; }
            if (_trayAll != null) _trayAll.Enabled = false;
            foreach (var s in list) s.Busy = true;
            foreach (var r in _rows.Where(r => list.Contains(r.Svc))) { r.Toggle.Enabled = false; r.Status.Set(PillState.Busy, starting ? "Starting…" : "Stopping…"); }
            bool dbFailed = false;
            Task.Run(() =>
            {
                foreach (var s in list)
                {
                    try { act(s); }
                    catch (Exception e)
                    {
                        Log.Error(s.Title + ": " + e.Message);
                        if (s == Services.MariaDb) dbFailed = true;
                    }
                    finally { s.Busy = false; }
                }
            }).ContinueWith(t =>
            {
                _sequenceRunning = false;
                if (_trayAll != null) _trayAll.Enabled = true;
                RefreshStatus();
                if (starting && dbFailed && Visible) OfferRebuild();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void RefreshSites()
        {
            try
            {
                Vhosts.Write(Cfg);
                var sites = Vhosts.Sites(NodeApp.LoadAll());
                Log.Info("Sites: " + (sites.Count == 0 ? "none (create a folder in www\\)" : string.Join(", ", sites.Select(s => s.Name + ".localhost"))));
                if (Services.Apache.IsRunning) Run(Services.Apache, Services.Apache.Restart, "Restarting…");
            }
            catch (Exception e) { Log.Error(e.Message); }
        }

        void ShowNodeApps()
        {
            using (var f = new NodeAppsForm())
            {
                f.ShowDialog(this);
                if (f.Changed)
                {
                    BuildRows();
                    try { Vhosts.Write(Cfg); } catch (Exception e) { Log.Error(e.Message); }
                    if (Services.Apache.IsRunning) Run(Services.Apache, Services.Apache.Restart, "Restarting…");
                }
            }
        }

        void ShowSettings()
        {
            using (var f = new SettingsForm())
            {
                f.ShowDialog(this);
                UpdateSubtitle();
                BuildRows();
                if (f.RestartApache && Services.Apache.IsRunning) Run(Services.Apache, Services.Apache.Restart, "Restarting…");
                if (f.RestartMariaDb && Services.MariaDb.IsRunning) Run(Services.MariaDb, Services.MariaDb.Restart, "Restarting…");
            }
        }

        // ------------------------------------------------------------------ tray / closing

        void BuildTray()
        {
            _tray.Icon = Ui.AppIcon;
            _tray.Text = "WebHut";
            _tray.Visible = true;
            var menu = new ContextMenuStrip();
            menu.Items.Add("Open WebHut", null, (s, e) => ShowFromTray());
            _trayAll = (ToolStripMenuItem)menu.Items.Add("Start all", null, (s, e) => ToggleAll());
            menu.Items.Add("Open localhost", null, (s, e) => Sys.OpenUrl(Services.Apache.Url));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Quit WebHut", null, (s, e) => ExitApp());
            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += (s, e) => ShowFromTray();
        }

        void ShowFromTray()
        {
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
        }

        void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (_exiting) return;
            if (e.CloseReason == CloseReason.WindowsShutDown || e.CloseReason == CloseReason.TaskManagerClosing)
            {
                // No dialogs during shutdown/logoff. Stop MariaDB cleanly so a shutdown never needs
                // crash recovery; Windows shows our reason while we wait (normally 1-2 seconds).
                _exiting = true;
                _tray.Visible = false;
                try
                {
                    if (Services.MariaDb.IsRunning)
                    {
                        Native.BlockShutdown(Handle, "WebHut is stopping the database safely...");
                        Services.MariaDb.StopForShutdown();
                    }
                }
                catch (Exception ex) { Log.Error("Shutdown: " + ex.Message); }
                finally { Native.UnblockShutdown(Handle); }
                return;
            }
            if (e.CloseReason == CloseReason.UserClosing && Cfg.MinimizeToTray)
            {
                e.Cancel = true;
                Hide();
                if (!_trayHintShown)
                {
                    _trayHintShown = true;
                    _tray.ShowBalloonTip(3000, "WebHut is still running", "Your servers keep running. To close WebHut completely, use Quit WebHut.", ToolTipIcon.Info);
                }
                return;
            }
            e.Cancel = true;
            ExitApp();
        }

        void ExitApp()
        {
            bool anyRunning = Services.All().Any(s => SafePid(s) > 0);
            if (anyRunning)
            {
                if (!Visible) ShowFromTray();
                var answer = MessageBox.Show(this, "Stop all servers before quitting?\n\nYes - stop them\nNo - leave them running in the background",
                    "Quit WebHut", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (answer == DialogResult.Cancel) return;
                if (answer == DialogResult.Yes)
                {
                    Cursor = Cursors.WaitCursor;
                    var list = Services.All(); list.Reverse();
                    foreach (var s in list) { try { s.Stop(); } catch (Exception e) { Log.Error(e.Message); } }
                }
            }
            _exiting = true;
            _tray.Visible = false;
            _tray.Dispose();
            Application.Exit();
        }
    }
}
