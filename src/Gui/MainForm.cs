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
            public IconButton Toggle, Open;
            public bool Failed;   // last start failed; shown until the next action
        }

        readonly TableLayoutPanel _grid = new TableLayoutPanel();
        readonly RichTextBox _log = new RichTextBox();
        readonly List<Row> _rows = new List<Row>();
        readonly Timer _timer = new Timer { Interval = 2000 };
        readonly Timer _backupTimer = new Timer { Interval = 10 * 60 * 1000 };
        volatile bool _backingUp;
        readonly NotifyIcon _tray = new NotifyIcon();
        Label _sub;
        Pill _health;
        Segmented _filter;
        readonly ToolTip _tips = new ToolTip { InitialDelay = 400 };
        readonly List<string> _lines = new List<string>();
        int _filterIndex;
        bool _logExpanded, _autoScrollOn = true;
        FlowLayoutPanel _tiles;
        IconButton _allBtn;
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
            // Design size, capped to the screen's work area (e.g. 1920x1080 at 125% is only ~1536x830 "design" pixels).
            var wa = Screen.PrimaryScreen.WorkingArea;
            ClientSize = new Size(Math.Min(Px(1320), wa.Width - Px(24)), Math.Min(Px(770), wa.Height - Px(48)));
            MinimumSize = new Size(Math.Min(Px(1100), wa.Width - Px(16)), Math.Min(Px(680), wa.Height - Px(16)));
            StartPosition = FormStartPosition.CenterScreen;
            DoubleBuffered = true;

            BuildUi();

            BuildTray();
            Log.Line += line => { if (InvokeRequired) { try { BeginInvoke(new Action(() => OnLogLine(line))); } catch { } } else OnLogLine(line); };
            _timer.Tick += (s, e) => RefreshStatus();
            _backupTimer.Tick += (s, e) => ScheduledBackup();
            Load += OnLoad;
            FormClosing += OnClosing;
            Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnWindowsThemeChanged;
            FormClosed += (s, e) => Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnWindowsThemeChanged;
        }

        void BuildUi()
        {
            _cards.Clear();
            BackColor = Ui.Bg;
            ForeColor = Ui.Ink;
            _pageNavs.Clear();
            var main = PageHost();
            main.Visible = true;
            var stack = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, RowCount = 4, BackColor = Ui.Bg };
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 4; i++) stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            stack.Controls.Add(BuildHeader(), 0, 0);
            stack.Controls.Add(BuildServicesCard(), 0, 1);
            stack.Controls.Add(BuildQuickActions(), 0, 2);
            stack.Controls.Add(BuildActivity(), 0, 3);
            main.Controls.Add(stack);
            main.Resize += (s, e) => Relayout();
            _pageDash = main;
            _pageSites = BuildSitesPage();
            _pageDb = BuildDbPage();
            _pageSites.Resize += (s, e) => FitCards();
            _pageDb.Resize += (s, e) => FitCards();

            Controls.Add(ScrollHost.Wrap(_pageDb));
            Controls.Add(ScrollHost.Wrap(_pageSites));
            Controls.Add(ScrollHost.Wrap(main));
            Controls.Add(BuildSidebar());
            ShowPage(_page);
        }

        /// <summary>Switch light/dark: rebuild the window with the new palette, keeping the log.</summary>
        void ApplyTheme(bool dark)
        {
            if (dark == Ui.IsDark) return;
            Ui.ApplyTheme(dark);
            SuspendLayout();
            var old = Controls.Cast<Control>().ToList();
            Controls.Clear();
            BuildUi();
            foreach (var c in old) if (c != _log && !c.Contains(_log)) c.Dispose();
            ResumeLayout(true);
            Ui.TitleBar(this);
            UpdateSubtitle();
            BuildRows();
            Relayout();
            _log.BackColor = Ui.LogBg;
            RenderLog();
        }

        void SetTheme(bool dark)
        {
            Cfg.Theme = dark ? "dark" : "light";
            try { Cfg.Save(); } catch (Exception e) { Log.Error(e.Message); }
            ApplyTheme(dark);
        }

        void OnWindowsThemeChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
        {
            if (e.Category != Microsoft.Win32.UserPreferenceCategory.General || Cfg.Theme != "system") return;
            if (IsDisposed) return;
            BeginInvoke(new Action(() => ApplyTheme(Ui.WantsDark("system"))));
        }

        void Relayout()
        {
            LayoutTiles();
            FitCards();
        }

        // ------------------------------------------------------------------ layout

        Control BuildSidebar()
        {
            const int SideW = 232;
            var side = new Panel { Dock = DockStyle.Left, Width = Px(SideW), BackColor = Ui.Sidebar, Padding = new Padding(Px(12), Px(18), Px(12), Px(12)) };
            side.Paint += (s, e) => { using (var p = new Pen(Ui.Line)) e.Graphics.DrawLine(p, side.Width - 1, 0, side.Width - 1, side.Height); };
            int w = Px(SideW - 24 - 1);

            var brand = new TableLayoutPanel { Dock = DockStyle.Top, Height = Px(60), ColumnCount = 2, RowCount = 2, BackColor = Ui.Sidebar, Padding = new Padding(Px(6), 0, 0, 0) };
            brand.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Px(54)));
            brand.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            brand.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
            brand.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
            var logo = new Logo(Px(42)) { Margin = new Padding(0, Px(4), 0, 0) };
            brand.Controls.Add(logo, 0, 0); brand.SetRowSpan(logo, 2);
            brand.Controls.Add(new Label { Text = "WebHut", Font = Ui.Brand, ForeColor = Ui.Ink, AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Bottom, Margin = new Padding(0) }, 1, 0);
            brand.Controls.Add(new Label { Text = "v" + K.AppVersion, Font = Ui.Small, ForeColor = Ui.Muted, AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Top, Margin = new Padding(Px(1), 0, 0, 0) }, 1, 1);

            // Places, not actions: the everyday actions live in Quick Actions on the dashboard.
            var nav = new FlowLayoutPanel { Dock = DockStyle.Top, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, BackColor = Ui.Sidebar, Padding = new Padding(0, Px(16), 0, 0) };
            // Pages on left-click; Sites and Databases also have a quick menu on right-click.
            _pageNavs.Add(PageNav(nav, w, "Dashboard", "home", 0, null));
            NavItem sites = null, dbs = null, logs = null;
            sites = PageNav(nav, w, "Sites", "globe", 1, () => ShowSitesMenu(sites));
            dbs = PageNav(nav, w, "Databases", "database", 2, () => ShowDatabaseMenu(dbs));
            _pageNavs.Add(sites); _pageNavs.Add(dbs);
            _tips.SetToolTip(sites, "Right-click for quick actions");
            _tips.SetToolTip(dbs, "Right-click for quick actions");
            Nav(nav, w, "Node apps", "cube", ShowNodeApps);
            logs = Nav(nav, w, "Logs", "logs", () => ShowLogsMenu(logs));
            Nav(nav, w, "Settings", "gear", ShowSettings);

            // Footer: theme switch, then the rarely used About / Quit, smaller.
            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, BackColor = Ui.Sidebar };
            bottom.Controls.Add(new Panel { Height = 1, Width = w, BackColor = Ui.Line, Margin = new Padding(0, 0, 0, Px(8)) });
            bottom.Controls.Add(ThemeRow(w));
            // Quit on the left, About as an icon on the right: both always fit the sidebar width, hover included.
            var minor = new TableLayoutPanel { Width = w, Height = Px(40), ColumnCount = 2, RowCount = 1, BackColor = Ui.Sidebar, Margin = new Padding(0, Px(4), 0, 0) };
            minor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            minor.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var quit = new IconButton("Quit WebHut", "power", ButtonKind.Ghost) { IconColor = Ui.Faint, Font = Ui.Small, Margin = new Padding(0), Anchor = AnchorStyles.Left };
            quit.Click += (s, e) => ExitApp();
            _tips.SetToolTip(quit, "Close WebHut completely (asks whether to stop running servers)");
            var about = new IconButton("", "info", ButtonKind.Ghost) { IconColor = Ui.Faint, Margin = new Padding(0), Anchor = AnchorStyles.Right };
            about.SetMin(Px(36), Px(34));
            about.Click += (s, e) => ShowAbout();
            _tips.SetToolTip(about, "About WebHut");
            minor.Controls.Add(quit, 0, 0); minor.Controls.Add(about, 1, 0);
            bottom.Controls.Add(minor);

            side.Controls.Add(nav);
            side.Controls.Add(brand);
            side.Controls.Add(bottom);
            return side;
        }

        /// <summary>Offers the other theme: "Dark mode" while light, "Light mode" while dark.</summary>
        Control ThemeRow(int width)
        {
            bool dark = Ui.IsDark;
            var n = new NavItem(dark ? "Light mode" : "Dark mode", dark ? "sun" : "moon") { Width = width };
            _tips.SetToolTip(n, dark ? "Switch to the light theme" : "Switch to the dark theme");
            n.Click += (s, e) => BeginInvoke(new Action(() => SetTheme(!Ui.IsDark)));
            return n;
        }

        NavItem PageNav(FlowLayoutPanel host, int width, string text, string icon, int page, Action quick)
        {
            var n = new NavItem(text, icon) { Width = width };
            n.MouseUp += (s, e) =>
            {
                if (e.Button == MouseButtons.Left) ShowPage(page);
                else if (e.Button == MouseButtons.Right && quick != null) quick();
            };
            host.Controls.Add(n);
            return n;
        }

        NavItem Nav(FlowLayoutPanel host, int width, string text, string icon, Action act)
        {
            var n = new NavItem(text, icon) { Width = width };
            if (act != null) n.Click += (s, e) => act();
            host.Controls.Add(n);
            return n;
        }

        // ------------------------------------------------------------------ quick menus (right-click a sidebar item)

        void ShowSitesMenu(Control anchor)
        {
            bool up = Services.Apache.IsRunning;
            PopupMenu.Show(anchor, PopupMenu.Side.Right, "Sites", new List<PopupItem>
            {
                new PopupItem { Title = "Open localhost", Description = up ? "http://localhost in your browser" : "Start Apache first", Icon = "globe", Enabled = up, Act = () => Sys.OpenUrl(Services.Apache.Url) },
                new PopupItem { Title = "www folder", Description = "Open in Explorer", Icon = "folder", Act = () => Sys.OpenFolder(K.Www) },
                PopupItem.Line(),
                new PopupItem { Title = "Refresh sites", Description = "Rescan www and reload Apache", Icon = "refresh", Act = RefreshSites }
            });
        }

        void ShowDatabaseMenu(Control anchor)
        {
            bool pma = Services.Apache.IsRunning && Services.MariaDb.IsRunning;
            PopupMenu.Show(anchor, PopupMenu.Side.Right, "Databases", new List<PopupItem>
            {
                new PopupItem { Title = "Open phpMyAdmin", Description = pma ? "Manage databases in your browser" : "Start Apache and MariaDB first", Icon = "database", Enabled = pma, Act = () => Sys.OpenUrl(Services.MariaDb.Url) },
                new PopupItem { Title = "Backup & restore", Description = "Back up, restore, or rebuild after a crash", Icon = "server", Act = () => { using (var d = new DatabaseForm()) d.ShowDialog(this); RefreshDbPage(); } },
                new PopupItem { Title = "Import from XAMPP", Description = "Copy XAMPP databases and projects", Icon = "copy", Act = () => { using (var f = new XamppImportForm()) f.ShowDialog(this); RefreshDbPage(); } },
                PopupItem.Line(),
                new PopupItem { Title = "MariaDB configuration", Description = "Performance, reliability, security, logs", Icon = "gear", Act = () => ShowServerConfig("mariadb") }
            });
        }

        void ShowLogsMenu(Control anchor)
        {
            PopupMenu.Show(anchor, PopupMenu.Side.Right, "Logs", new List<PopupItem>
            {
                new PopupItem { Title = "Apache error log", Icon = "file", Act = () => Sys.OpenInEditor(Services.Apache.LogFile) },
                new PopupItem { Title = "MariaDB log", Icon = "file", Act = () => Sys.OpenInEditor(Services.MariaDb.LogFile) },
                new PopupItem { Title = "WebHut log", Icon = "file", Act = () => Sys.OpenInEditor(Path.Combine(K.Logs, "webhut.log")) },
                PopupItem.Line(),
                new PopupItem { Title = "Open logs folder", Icon = "folder", Act = () => Sys.OpenFolder(K.Logs) }
            }, Px(250));
        }

        // ------------------------------------------------------------------ pages

        Panel _pageDash, _pageSites, _pageDb;
        int _page;
        readonly List<NavItem> _pageNavs = new List<NavItem>();
        TableLayoutPanel _sitesGrid, _dbGrid, _backupGrid;
        Label _dbSub, _dbNote;
        Pill _dbStatus;
        IconButton _pmaBtn, _backupBtn;
        bool _dbLoading;

        Panel PageHost()
        {
            return new Panel { Dock = DockStyle.Fill, BackColor = Ui.Bg, Padding = new Padding(Px(28), Px(22), Px(28), Px(20)), AutoScroll = true, Visible = false };
        }

        static TableLayoutPanel Stack()
        {
            var stack = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, BackColor = Ui.Bg };
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            return stack;
        }

        static void Push(TableLayoutPanel stack, Control c)
        {
            int i = stack.RowCount++;
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            stack.Controls.Add(c, 0, i);
        }

        /// <summary>Page title on the left, actions on the right.</summary>
        static Control PageHeader(string title, Label sub, params Control[] actions)
        {
            var t = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, BackColor = Ui.Bg, Margin = new Padding(0, 0, 0, Px(16)) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var titles = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(0), BackColor = Ui.Bg };
            titles.Controls.Add(new Label { Text = title, Font = Ui.H1, ForeColor = Ui.Ink, AutoSize = true, Margin = new Padding(0) });
            sub.Margin = new Padding(Px(2), Px(2), 0, 0);
            titles.Controls.Add(sub);
            var right = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Ui.Bg, Anchor = AnchorStyles.Right, Margin = new Padding(0, Px(8), 0, 0) };
            foreach (var a in actions) right.Controls.Add(a);
            t.Controls.Add(titles, 0, 0);
            t.Controls.Add(right, 1, 0);
            return t;
        }

        IconButton PageButton(string text, string icon, ButtonKind kind, Action act)
        {
            var b = new IconButton(text, icon, kind) { Margin = new Padding(Px(10), 0, 0, 0) };
            b.SetMin(0, Px(42));
            b.Click += (s, e) => act();
            return b;
        }

        /// <summary>A table inside a rounded frame with a header band, like the Services table.</summary>
        TableLayoutPanel TableIn(Card frame, params int[] widths)
        {
            var grid = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, BackColor = Color.Transparent, ColumnCount = widths.Length, Padding = new Padding(Px(16), 0, Px(12), 0) };
            foreach (var w in widths)
                grid.ColumnStyles.Add(w < 0 ? new ColumnStyle(SizeType.Percent, 100) : w == 0 ? new ColumnStyle(SizeType.AutoSize) : new ColumnStyle(SizeType.Absolute, Px(w)));
            grid.CellPaint += (s, e) =>
            {
                if (e.Row < grid.RowCount - 1)
                    using (var p = new Pen(Ui.Line))
                        e.Graphics.DrawLine(p, e.CellBounds.Left - (e.Column == 0 ? Px(16) : 0), e.CellBounds.Bottom - 1,
                            e.CellBounds.Right + (e.Column == grid.ColumnCount - 1 ? Px(12) : 0), e.CellBounds.Bottom - 1);
            };
            frame.Paint += (s, e) =>
            {
                var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
                var hs = grid.GetRowHeights();
                int head = hs.Length > 0 ? hs[0] : Px(38);
                using (var path = Ui.Rounded(new RectangleF(0.5f, 0.5f, frame.Width - 1.5f, frame.Height - 1.5f), Px(10)))
                {
                    g.SetClip(path);
                    using (var b = new SolidBrush(Ui.HeadBg)) g.FillRectangle(b, 0, 0, frame.Width, head + 1);
                    g.ResetClip();
                    using (var pen = new Pen(Ui.Line)) g.DrawPath(pen, path);
                }
            };
            frame.Controls.Add(grid);
            return grid;
        }

        static void HeadRow(TableLayoutPanel grid, params string[] heads)
        {
            int i = grid.RowCount++;
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, Px(38)));
            for (int c = 0; c < heads.Length; c++)
                grid.Controls.Add(new Label { Text = heads[c], Font = Ui.Caps, ForeColor = Ui.Muted, AutoSize = true, BackColor = Color.Transparent, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, Px(8), 0) }, c, i);
        }

        static void ClearGrid(TableLayoutPanel grid)
        {
            grid.SuspendLayout();
            foreach (Control c in grid.Controls.Cast<Control>().ToList()) c.Dispose();
            grid.Controls.Clear();
            grid.RowStyles.Clear();
            grid.RowCount = 0;
        }

        static Label Cell(string text, Font font, Color color)
        {
            return new Label { Text = text, Font = font, ForeColor = color, AutoSize = true, AutoEllipsis = true, BackColor = Color.Transparent, Anchor = AnchorStyles.Left, Margin = new Padding(0, Px(14), Px(8), Px(14)) };
        }

        Card TableCard(string icon, string title, Control right, out TableLayoutPanel grid, params int[] widths)
        {
            var card = SectionCard();
            var frame = new Card { Dock = DockStyle.Top, AutoSize = false, Radius = Px(10), BackColor = Ui.Card, Padding = new Padding(1) };
            var g = TableIn(frame, widths);
            grid = g;
            return Assemble(card, CardHeader(icon, title, right), frame, w => g.GetPreferredSize(new Size(w - 2, 0)).Height + 2);
        }

        Panel BuildSitesPage()
        {
            var page = PageHost();
            var stack = Stack();
            var sub = new Label { Text = "Every folder in www becomes a site at http://<folder>.localhost. Node apps appear here too.", Font = Ui.Base, ForeColor = Ui.Muted, AutoSize = true };
            Push(stack, PageHeader("Sites", sub,
                PageButton("www folder", "folder", ButtonKind.Outline, () => Sys.OpenFolder(K.Www)),
                PageButton("Refresh sites", "refresh", ButtonKind.Primary, () => { RefreshSites(); RefreshSitesPage(); })));
            TableLayoutPanel g;
            Push(stack, TableCard("globe", "Your sites", null, out g, 180, -1, 220, 110, 0));
            _sitesGrid = g;
            page.Controls.Add(stack);
            return page;
        }

        void RefreshSitesPage()
        {
            if (_sitesGrid == null) return;
            ClearGrid(_sitesGrid);
            HeadRow(_sitesGrid, "SITE", "ADDRESS", "FOLDER", "TYPE", "");
            bool up = Services.Apache.IsRunning;
            string port = Cfg.HttpPort == 80 ? "" : ":" + Cfg.HttpPort;
            var apps = NodeApp.LoadAll();
            List<Vhosts.Site> sites;
            try { sites = Vhosts.Sites(apps); } catch { sites = new List<Vhosts.Site>(); }
            Action<string, string, string, string> add = (name, url, folder, type) =>
            {
                int i = _sitesGrid.RowCount++;
                _sitesGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                _sitesGrid.Controls.Add(Cell(name, Ui.ServiceName, Ui.Ink), 0, i);
                _sitesGrid.Controls.Add(Cell(url, Ui.Mono, Ui.Muted), 1, i);
                _sitesGrid.Controls.Add(Cell(folder, Ui.Detail, Ui.Muted), 2, i);
                _sitesGrid.Controls.Add(Cell(type, Ui.Small, Ui.Muted), 3, i);
                var open = new IconButton("Open", "external", ButtonKind.Outline) { Font = Ui.BtnSmall, IconColor = Ui.NavInk, Enabled = up, Anchor = AnchorStyles.Left, Margin = new Padding(0, Px(8), 0, Px(8)) };
                open.Click += (s, e) => Sys.OpenUrl(url);
                _tips.SetToolTip(open, up ? "Open " + url : "Start Apache first");
                _sitesGrid.Controls.Add(open, 4, i);
            };
            add("localhost", "http://localhost" + port + "/", "www", "Dashboard");
            foreach (var s in sites) add(s.Name, "http://" + s.Name + ".localhost" + port + "/", "www\\" + s.Folder, s.DocRoot.EndsWith("/public") ? "PHP · public\\" : "PHP");
            foreach (var a in apps) add(a.Name, "http://" + a.Host + port + "/", a.Dir.Replace('/', '\\'), "Node app · :" + a.Port);
            _sitesGrid.ResumeLayout();
            FitCards();
        }

        Panel BuildDbPage()
        {
            var page = PageHost();
            var stack = Stack();
            _dbSub = new Label { Text = "", Font = Ui.Base, ForeColor = Ui.Muted, AutoSize = true };
            _pmaBtn = PageButton("Open phpMyAdmin", "database", ButtonKind.Primary, () => Sys.OpenUrl(Services.MariaDb.Url));
            _backupBtn = PageButton("Back up now", "copy", ButtonKind.Outline, BackupNow);
            IconButton more = null;
            more = PageButton("", "more", ButtonKind.Outline, () => PopupMenu.Show(more, PopupMenu.Side.BelowRight, null, new List<PopupItem>
            {
                new PopupItem { Title = "Restore or rebuild…", Description = "Pick a backup, or rebuild after a crash", Icon = "refresh", Act = () => { using (var d = new DatabaseForm()) d.ShowDialog(this); RefreshDbPage(); } },
                new PopupItem { Title = "Import from XAMPP", Description = "Copy XAMPP databases and projects", Icon = "copy", Act = () => { using (var f = new XamppImportForm()) f.ShowDialog(this); RefreshDbPage(); } },
                PopupItem.Line(),
                new PopupItem { Title = "MariaDB configuration", Description = "Performance, reliability, security, logs", Icon = "gear", Act = () => ShowServerConfig("mariadb") },
                new PopupItem { Title = "Backups folder", Description = "data\\backups", Icon = "folder", Act = () => Sys.OpenFolder(K.Backups) }
            }));
            _tips.SetToolTip(more, "More database tools");
            Push(stack, PageHeader("Databases", _dbSub, _backupBtn, more, _pmaBtn));

            _dbStatus = new Pill { Fit = true, Height = Px(30), Margin = new Padding(0, 0, Px(10), 0), Anchor = AnchorStyles.Left };
            _dbStatus.Set(PillState.Stopped, "Stopped");
            TableLayoutPanel g;
            Push(stack, TableCard("database", "Databases", _dbStatus, out g, -1, 90, 110, 0));
            _dbGrid = g;
            _dbNote = new Label { Text = "", Font = Ui.Small, ForeColor = Ui.Muted, AutoSize = true, BackColor = Ui.Card };
            Push(stack, TableCard("copy", "Recent backups", _dbNote, out g, -1, 120, 0));
            _backupGrid = g;
            page.Controls.Add(stack);
            return page;
        }

        void RefreshDbPage()
        {
            if (_dbGrid == null || _dbLoading) return;
            bool up = Services.MariaDb.IsRunning;
            _dbSub.Text = "MariaDB " + WebHutConfig.FullVersion(K.MariaDb) + " on 127.0.0.1:" + Cfg.MysqlPort + "  ·  only this PC can connect  ·  root " + (string.IsNullOrEmpty(Cfg.MysqlRootPassword) ? "has no password" : "password set");
            _dbStatus.Set(up ? PillState.Running : PillState.Stopped, up ? "MariaDB running" : "MariaDB stopped");
            _pmaBtn.Enabled = up && Services.Apache.IsRunning;
            _tips.SetToolTip(_pmaBtn, _pmaBtn.Enabled ? "Open http://localhost/phpmyadmin" : "Start Apache and MariaDB first");
            _backupBtn.Enabled = up;

            // backups (local files, instant)
            ClearGrid(_backupGrid);
            HeadRow(_backupGrid, "WHEN", "SIZE", "");
            var backups = Backup.List();
            _dbNote.Text = Cfg.AutoBackup ? "Automatic every " + Cfg.BackupEveryHours + " h · newest " + Cfg.BackupKeep + " kept" : "Automatic backups are off";
            foreach (var b in backups.Take(5))
            {
                var bk = b;
                int i = _backupGrid.RowCount++;
                _backupGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                _backupGrid.Controls.Add(Cell(b.Time.ToString("ddd d MMM yyyy, HH:mm"), Ui.Base, Ui.Ink), 0, i);
                _backupGrid.Controls.Add(Cell(b.Size < 1048576 ? (b.Size / 1024.0).ToString("0") + " KB" : (b.Size / 1048576.0).ToString("0.0") + " MB", Ui.Mono, Ui.Muted), 1, i);
                var restore = new IconButton("Restore…", "refresh", ButtonKind.Outline) { Font = Ui.BtnSmall, IconColor = Ui.NavInk, Enabled = up, Anchor = AnchorStyles.Left, Margin = new Padding(0, Px(8), 0, Px(8)) };
                restore.Click += (s, e) => RestoreBackup(bk);
                _tips.SetToolTip(restore, up ? "Load this backup into MariaDB" : "Start MariaDB first");
                _backupGrid.Controls.Add(restore, 2, i);
            }
            if (backups.Count == 0) { int i = _backupGrid.RowCount++; _backupGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize)); var l = Cell("No backups yet. Click Back up now, or wait for the automatic one.", Ui.Base, Ui.Muted); _backupGrid.Controls.Add(l, 0, i); _backupGrid.SetColumnSpan(l, 3); }
            _backupGrid.ResumeLayout();

            // databases (needs a query; runs in the background)
            ClearGrid(_dbGrid);
            HeadRow(_dbGrid, "DATABASE", "TABLES", "SIZE", "");
            if (!up)
            {
                int i = _dbGrid.RowCount++; _dbGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                var l = Cell("Start MariaDB to see your databases.", Ui.Base, Ui.Muted); _dbGrid.Controls.Add(l, 0, i); _dbGrid.SetColumnSpan(l, 4);
                _dbGrid.ResumeLayout(); FitCards(); return;
            }
            _dbGrid.ResumeLayout();
            _dbLoading = true;
            var cfg = Cfg;
            Task.Run(() => Backup.Stats(cfg)).ContinueWith(t =>
            {
                _dbLoading = false;
                if (IsDisposed || _dbGrid.IsDisposed) return;
                ClearGrid(_dbGrid);
                HeadRow(_dbGrid, "DATABASE", "TABLES", "SIZE", "");
                bool pma = Services.Apache.IsRunning;
                if (t.IsFaulted || t.Result.Count == 0)
                {
                    int i = _dbGrid.RowCount++; _dbGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                    var l = Cell(t.IsFaulted ? "Couldn't list databases: " + t.Exception.GetBaseException().Message : "No databases yet. Create one in phpMyAdmin, or import from XAMPP.", Ui.Base, Ui.Muted);
                    _dbGrid.Controls.Add(l, 0, i); _dbGrid.SetColumnSpan(l, 4);
                }
                else foreach (var d in t.Result)
                {
                    var db = d.Name;
                    int i = _dbGrid.RowCount++;
                    _dbGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                    _dbGrid.Controls.Add(Cell(d.Name, Ui.ServiceName, Ui.Ink), 0, i);
                    _dbGrid.Controls.Add(Cell(d.Tables.ToString(), Ui.Mono, Ui.Muted), 1, i);
                    _dbGrid.Controls.Add(Cell(d.Bytes < 1048576 ? (d.Bytes / 1024.0).ToString("0") + " KB" : (d.Bytes / 1048576.0).ToString("0.0") + " MB", Ui.Mono, Ui.Muted), 2, i);
                    var open = new IconButton("phpMyAdmin", "external", ButtonKind.Outline) { Font = Ui.BtnSmall, IconColor = Ui.NavInk, Enabled = pma, Anchor = AnchorStyles.Left, Margin = new Padding(0, Px(8), 0, Px(8)) };
                    open.Click += (s, e) => Sys.OpenUrl(Services.MariaDb.Url.TrimEnd('/') + "/index.php?route=/database/structure&db=" + Uri.EscapeDataString(db));
                    _tips.SetToolTip(open, pma ? "Open " + db + " in phpMyAdmin" : "Start Apache first");
                    _dbGrid.Controls.Add(open, 3, i);
                }
                _dbGrid.ResumeLayout();
                FitCards();
            }, TaskScheduler.FromCurrentSynchronizationContext());
            FitCards();
        }

        void BackupNow()
        {
            _backupBtn.Enabled = false;
            var cfg = Cfg;
            Task.Run(() => Backup.Create(cfg, "manual")).ContinueWith(t =>
            {
                if (IsDisposed) return;
                if (t.IsFaulted) { Log.Error("Backup failed: " + t.Exception.GetBaseException().Message); Toast.Show(this, "Backup failed. See Activity.", ToastKind.Error); }
                else Toast.Show(this, t.Result == null ? "Nothing to back up yet." : "Backup saved.", t.Result == null ? ToastKind.Info : ToastKind.Success);
                RefreshDbPage();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void RestoreBackup(Backup.Info b)
        {
            if (MessageBox.Show(this, "Restore the backup from " + b.Time.ToString("g") + "?\n\nTables in the backup replace the current tables with the same name. Other tables are not touched.",
                "Restore backup", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            var cfg = Cfg;
            Toast.Show(this, "Restoring backup…", ToastKind.Info);
            Task.Run(() => Backup.Restore(cfg, b.Path)).ContinueWith(t =>
            {
                if (IsDisposed) return;
                if (t.IsFaulted) { Log.Error("Restore failed: " + t.Exception.GetBaseException().Message); Ui.Error(this, "Restore", t.Exception.GetBaseException().Message); }
                else { Log.Info("Restored backup from " + b.Time.ToString("g") + "."); Toast.Show(this, "Backup restored."); }
                RefreshDbPage();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        /// <summary>The themed scroll host around a page (pages live inside one).</summary>
        static Control Host(Control page) { return page.Parent as ScrollHost ?? (Control)page; }

        void ShowPage(int page)
        {
            _page = page;
            SuspendLayout();
            Host(_pageDash).Visible = page == 0;
            Host(_pageSites).Visible = page == 1;
            Host(_pageDb).Visible = page == 2;
            for (int i = 0; i < _pageNavs.Count; i++) { _pageNavs[i].Active = i == page; _pageNavs[i].Invalidate(); }
            ResumeLayout(true);
            if (page == 1) RefreshSitesPage();
            if (page == 2) RefreshDbPage();
            Relayout();
        }

        Control BuildHeader()
        {
            var t = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, BackColor = Ui.Bg, Margin = new Padding(0, 0, 0, Px(16)) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var titles = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(0), BackColor = Ui.Bg };
            titles.Controls.Add(new Label { Text = "WebHut", Font = Ui.H1, ForeColor = Ui.Ink, AutoSize = true, Margin = new Padding(0) });
            _sub = new Label { Text = "", Font = Ui.Base, ForeColor = Ui.Muted, AutoSize = true, AutoEllipsis = true, Margin = new Padding(Px(2), Px(2), 0, 0) };
            titles.Controls.Add(_sub);

            // Health chip + the one Start all / Stop all button, side by side.
            var right = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Ui.Bg, Anchor = AnchorStyles.Right, Margin = new Padding(0, Px(4), 0, 0) };
            _health = new Pill { Fit = true, Height = Px(34), Anchor = AnchorStyles.Left, Margin = new Padding(0, Px(6), Px(12), 0) };
            _health.Set(PillState.Stopped, "All services stopped");
            _allBtn = new IconButton("Start all", "play", ButtonKind.Primary) { Margin = new Padding(0) };
            _allBtn.SetMin(Px(150), Px(46));
            _allBtn.Click += (s, e) => ToggleAll();
            right.Controls.Add(_health);
            right.Controls.Add(_allBtn);

            t.Controls.Add(titles, 0, 0);
            t.Controls.Add(right, 1, 0);
            return t;
        }

        /// <summary>Card heading: accent icon + title on the left, optional controls on the right.</summary>
        static TableLayoutPanel CardHeader(string icon, string title, Control right)
        {
            var h = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, BackColor = Ui.Card, Margin = new Padding(0, 0, 0, Px(12)) };
            h.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            h.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var left = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Ui.Card, Margin = new Padding(0), Anchor = AnchorStyles.Left };
            left.Controls.Add(new IconBox(icon, Ui.AccentInk, Px(22)) { Margin = new Padding(0, Px(4), Px(10), 0) });
            left.Controls.Add(new Label { Text = title, Font = Ui.H2, ForeColor = Ui.Ink, AutoSize = true, Margin = new Padding(0) });
            h.Controls.Add(left, 0, 0);
            if (right != null) { right.Anchor = AnchorStyles.Right; h.Controls.Add(right, 1, 0); }
            return h;
        }

        static Card SectionCard()
        {
            return new Card { Dock = DockStyle.Top, AutoSize = false, Margin = new Padding(0, 0, 0, Px(16)), Padding = new Padding(Px(20), Px(16), Px(20), Px(18)) };
        }

        /// <summary>Card = header, a fixed gap, then content; heights are set by FitCards (WinForms
        /// auto-size doesn't shrink panels with docked children reliably).</summary>
        class CardParts { public Card Card; public Control Header, Content; public Func<int, int> ContentHeight; }
        readonly List<CardParts> _cards = new List<CardParts>();

        Card Assemble(Card card, Control header, Control content, Func<int, int> contentHeight)
        {
            content.Dock = DockStyle.Top;
            header.Dock = DockStyle.Top;
            card.Controls.Add(content);
            card.Controls.Add(new Panel { Dock = DockStyle.Top, Height = Px(12), BackColor = Ui.Card });
            card.Controls.Add(header);
            _cards.Add(new CardParts { Card = card, Header = header, Content = content, ContentHeight = contentHeight });
            return card;
        }

        void FitCards()
        {
            foreach (var c in _cards)
            {
                if (c.Card.IsDisposed) continue;
                int inner = Math.Max(1, c.Card.ClientSize.Width - c.Card.Padding.Horizontal);
                int hh = c.Header.GetPreferredSize(new Size(inner, 0)).Height;
                if (c.Header.Height != hh) c.Header.Height = hh;
                int ch = c.ContentHeight(inner);
                if (c.Content.Height != ch) c.Content.Height = ch;
                int total = c.Card.Padding.Vertical + hh + Px(12) + ch;
                if (c.Card.Height != total) c.Card.Height = total;
            }
        }

        Control BuildServicesCard()
        {
            var card = SectionCard();

            // The table sits in a rounded frame; the frame paints the header band.
            var frame = new Card { Dock = DockStyle.Top, AutoSize = false, Radius = Px(10), BackColor = Ui.Card, Padding = new Padding(1) };
            frame.Paint += (s, e) =>
            {
                var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
                int head = _grid.GetRowHeights().Length > 0 ? _grid.GetRowHeights()[0] : Px(38);
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
            _grid.ColumnStyles.Clear();
            _grid.ColumnCount = 6;
            _grid.Padding = new Padding(Px(16), 0, Px(12), 0);
            _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Px(150)));  // service
            _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));       // details
            _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Px(64)));   // port
            _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Px(72)));   // pid
            _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Px(124)));  // status
            _grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));           // actions
            _grid.CellPaint -= GridCellPaint;
            _grid.CellPaint += GridCellPaint;
            frame.Controls.Add(_grid);
            return Assemble(card, CardHeader("pulse", "Services", null), frame,
                w => _grid.GetPreferredSize(new Size(w - 2, 0)).Height + 2);
        }

        void GridCellPaint(object sender, TableLayoutCellPaintEventArgs e)
        {
            if (e.Row < _grid.RowCount - 1)
                using (var p = new Pen(Ui.Line))
                    e.Graphics.DrawLine(p, e.CellBounds.Left - (e.Column == 0 ? Px(16) : 0), e.CellBounds.Bottom - 1,
                        e.CellBounds.Right + (e.Column == 5 ? Px(12) : 0), e.CellBounds.Bottom - 1);
        }

        Control BuildQuickActions()
        {
            var card = SectionCard();
            card.Padding = new Padding(Px(20), Px(16), Px(10), Px(8));
            _tiles = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, BackColor = Ui.Card, Margin = new Padding(0) };
            AddTile("Open localhost", "http://localhost", "globe", () => Sys.OpenUrl(Services.Apache.Url));
            AddTile("phpMyAdmin", "Database UI", "database", () => Sys.OpenUrl(Services.MariaDb.Url));
            AddTile("www folder", "Open in Explorer", "folder", () => Sys.OpenFolder(K.Www));
            AddTile("Shell", "Terminal + tools", "terminal", () => Services.OpenShell());
            AddTile("Refresh sites", "Rescan www", "refresh", RefreshSites);
            AddTile("Node apps", "Add or edit apps", "cube", ShowNodeApps);
            _tiles.AutoSize = false;
            card.Padding = new Padding(Px(20), Px(16), Px(10), Px(8));
            return Assemble(card, CardHeader("bolt", "Quick Actions", null), _tiles, w =>
            {
                int n = _tiles.Controls.Count, per = TilesPerRow(w);
                int rows = (n + per - 1) / per;
                int th = n > 0 ? _tiles.Controls[0].Height + _tiles.Controls[0].Margin.Vertical : 0;
                return rows * th;
            });
        }

        void AddTile(string title, string sub, string icon, Action act)
        {
            var t = new Tile(title, sub, icon);
            t.Click += (s, e) => act();
            _tiles.Controls.Add(t);
        }

        int TilesPerRow(int avail)
        {
            int n = Math.Max(1, _tiles.Controls.Count);
            return avail >= (Px(150) + Px(10)) * n ? n : (n + 1) / 2;
        }

        /// <summary>Six equal tiles per row; three per row when the window is narrow.</summary>
        void LayoutTiles()
        {
            if (_tiles == null) return;
            int avail = _tiles.ClientSize.Width;
            if (avail <= 0) return;
            int gap = Px(10), per = TilesPerRow(avail);
            int w = (avail - gap * per) / per;
            foreach (Control c in _tiles.Controls) if (c.Width != w) c.Width = w;
        }

        Control BuildActivity()
        {
            var card = SectionCard();
            card.Margin = new Padding(0);

            var right = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Ui.Card, Margin = new Padding(0) };
            _filter = new Segmented("All", "Apache", "MariaDB", "Errors") { SelectedIndex = _filterIndex, Anchor = AnchorStyles.Left };
            _filter.SelectedChanged += (s, e) => { _filterIndex = _filter.SelectedIndex; RenderLog(); };
            var copy = SmallButton("Copy", "copy", "Copy the lines shown to the clipboard");
            copy.Click += (s, e) => CopyLog();
            var clear = SmallButton("Clear", "trash", "Clear this view only - log files on disk are kept");
            clear.Click += (s, e) => { lock (_lines) _lines.Clear(); _log.Clear(); };
            var expand = SmallButton("", _logExpanded ? "collapse" : "expand", _logExpanded ? "Make the console smaller" : "Make the console taller");
            _autoScroll = new Switch { Checked = _autoScrollOn, Anchor = AnchorStyles.Left, Margin = new Padding(Px(8), 0, Px(6), 0) };
            _autoScroll.CheckedChanged += (s, e) => _autoScrollOn = _autoScroll.Checked;
            var auto = new Label { Text = "Auto-scroll", Font = Ui.Small, ForeColor = Ui.Muted, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0) };
            right.Controls.AddRange(new Control[] { _filter, copy, clear, expand, _autoScroll, auto });

            var dark = new Card { Dock = DockStyle.Top, AutoSize = false, Height = Px(_logExpanded ? 380 : 150), Fill = Ui.LogBg, Border = Color.Transparent, BackColor = Ui.Card, Radius = Px(10), Padding = new Padding(Px(14), Px(10), Px(6), Px(8)) };
            expand.Click += (s, e) =>
            {
                _logExpanded = !_logExpanded;
                FitCards();
                expand.IconName = _logExpanded ? "collapse" : "expand";
                _tips.SetToolTip(expand, _logExpanded ? "Make the console smaller" : "Make the console taller");
            };
            _log.ReadOnly = true;
            _log.BorderStyle = BorderStyle.None;
            _log.BackColor = Ui.LogBg;
            _log.ForeColor = Ui.LogInk;
            _log.Font = Ui.LogFont;
            _log.Dock = DockStyle.Fill;
            _log.DetectUrls = false;
            _log.WordWrap = true;
            _log.ScrollBars = RichTextBoxScrollBars.Vertical;
            Ui.ThemeScrollBars(_log, true);   // the activity console is dark in both themes
            dark.Controls.Add(_log);
            return Assemble(card, CardHeader("file", "Activity", right), dark, w => Px(_logExpanded ? 380 : 150));
        }

        IconButton SmallButton(string text, string icon, string tip)
        {
            var b = new IconButton(text, icon, ButtonKind.Outline) { IconColor = Ui.NavInk, Font = Ui.BtnSmall, Margin = new Padding(0, 0, Px(6), 0), Anchor = AnchorStyles.Left };
            b.SetMin(0, Px(32));
            if (tip != null) _tips.SetToolTip(b, tip);
            return b;
        }

        // ------------------------------------------------------------------ activity log

        bool PassesFilter(string line)
        {
            switch (_filterIndex)
            {
                case 1: return line.IndexOf("Apache", StringComparison.OrdinalIgnoreCase) >= 0 || line.IndexOf("httpd", StringComparison.OrdinalIgnoreCase) >= 0 || line.IndexOf("Sites:", StringComparison.Ordinal) >= 0;
                case 2: return line.IndexOf("MariaDB", StringComparison.OrdinalIgnoreCase) >= 0 || line.IndexOf("database", StringComparison.OrdinalIgnoreCase) >= 0 || line.IndexOf("backup", StringComparison.OrdinalIgnoreCase) >= 0;
                case 3: return line.Contains("ERROR:") || line.Contains("WARN:");
                default: return true;
            }
        }

        void OnLogLine(string line)
        {
            lock (_lines)
            {
                _lines.Add(line);
                if (_lines.Count > 3000) _lines.RemoveRange(0, 1000);
            }
            if (PassesFilter(line)) Ui.AppendDarkLog(_log, line, _autoScrollOn);
        }

        void RenderLog()
        {
            if (_log.IsDisposed) return;
            List<string> lines;
            lock (_lines) lines = _lines.Where(PassesFilter).ToList();
            _log.SuspendLayout();
            _log.Clear();
            foreach (var l in lines) Ui.AppendDarkLog(_log, l, false);
            _log.ResumeLayout();
            _log.SelectionStart = _log.TextLength;
            _log.ScrollToCaret();
        }

        void CopyLog()
        {
            List<string> lines;
            lock (_lines) lines = _lines.Where(PassesFilter).ToList();
            if (lines.Count == 0) return;
            try { Clipboard.SetText(string.Join(Environment.NewLine, lines)); Toast.Show(this, "Copied " + lines.Count + (lines.Count == 1 ? " line" : " lines") + " to the clipboard.", ToastKind.Info); }
            catch (Exception e) { Log.Warn("Couldn't copy: " + e.Message); }
        }

        // ------------------------------------------------------------------ services table

        static string IconFor(Service svc)
        {
            if (svc == Services.Apache) return "server";
            if (svc == Services.MariaDb) return "database";
            return "cube";
        }

        static string OpenTip(Service svc)
        {
            if (svc == Services.Apache) return "Open http://localhost";
            if (svc == Services.MariaDb) return "Open phpMyAdmin (needs Apache and MariaDB running)";
            return "Open " + svc.Url;
        }

        void BuildRows()
        {
            _grid.SuspendLayout();
            foreach (Control c in _grid.Controls.Cast<Control>().ToList()) c.Dispose();
            _grid.Controls.Clear();
            _grid.RowStyles.Clear();
            _rows.Clear();
            _grid.RowCount = 0;

            AddHeaderRow();
            foreach (var svc in Services.All())
            {
                var r = new Row { Svc = svc };
                var row = r;
                var nameCell = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Color.Transparent, Margin = new Padding(0, Px(12), Px(8), Px(12)), Anchor = AnchorStyles.Left };
                nameCell.Controls.Add(new IconBox(IconFor(svc), Ui.AccentInk, Px(20)) { Margin = new Padding(0, Px(2), Px(10), 0) });
                nameCell.Controls.Add(new Label { Text = svc.Title, Font = Ui.ServiceName, ForeColor = Ui.Ink, AutoSize = true, BackColor = Color.Transparent, Margin = new Padding(0) });

                r.Detail = CellLabel(svc.Detail, Ui.Muted, Ui.Detail);
                r.Detail.AutoSize = false; r.Detail.AutoEllipsis = true; r.Detail.Dock = DockStyle.Fill; r.Detail.TextAlign = ContentAlignment.MiddleLeft;
                r.Port = CellLabel(svc.Port.ToString(), Ui.Ink, Ui.Mono);
                r.Pid = CellLabel("—", Ui.Faint, Ui.Mono);
                r.Status = new Pill { Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, Px(8), 0) };
                r.Status.Size = new Size(Px(116), Px(30));
                r.Status.Set(PillState.Stopped, "Stopped");

                r.Toggle = new IconButton("Start", "play", ButtonKind.Primary) { Font = Ui.BtnSmall };
                r.Toggle.SetMin(Px(84), 0);
                r.Toggle.Click += (s, e) => { row.Failed = false; Toggle(row); };
                r.Open = new IconButton("Open", "external", ButtonKind.Outline) { IconColor = Ui.NavInk, Font = Ui.BtnSmall };
                r.Open.Click += (s, e) => Sys.OpenUrl(row.Svc.Url);
                _tips.SetToolTip(r.Open, OpenTip(svc));
                var logs = new IconButton("", "file", ButtonKind.Outline) { IconColor = Ui.NavInk };
                logs.Click += (s, e) => Sys.OpenInEditor(row.Svc.LogFile);
                _tips.SetToolTip(logs, "Open the " + svc.Title + " log");
                var conf = new IconButton("", "gear", ButtonKind.Outline) { IconColor = Ui.NavInk, Margin = new Padding(0) };
                conf.Click += (s, e) =>
                {
                    if (row.Svc is ApacheService) ShowServerConfig("apache");
                    else if (row.Svc is MariaDbService) ShowServerConfig("mariadb");
                    else Sys.OpenInEditor(row.Svc.ConfigFile);
                };
                _tips.SetToolTip(conf, svc is NodeService ? "Edit node-apps.json" : svc.Title + " configuration");

                var actions = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, BackColor = Color.Transparent, Margin = new Padding(0, Px(9), 0, Px(9)), Anchor = AnchorStyles.Left };
                actions.Controls.AddRange(new Control[] { r.Toggle, r.Open, logs, conf });

                int i = _grid.RowCount++;
                _grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                _grid.Controls.Add(nameCell, 0, i);
                _grid.Controls.Add(r.Detail, 1, i);
                _grid.Controls.Add(r.Port, 2, i);
                _grid.Controls.Add(r.Pid, 3, i);
                _grid.Controls.Add(r.Status, 4, i);
                _grid.Controls.Add(actions, 5, i);
                _rows.Add(r);
            }
            _grid.ResumeLayout();
            FitCards();
            RefreshStatus();
        }

        static Label CellLabel(string text, Color color, Font font)
        {
            return new Label { Text = text, Font = font, ForeColor = color, AutoSize = true, BackColor = Color.Transparent, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, Px(8), 0) };
        }

        void AddHeaderRow()
        {
            int i = _grid.RowCount++;
            _grid.RowStyles.Add(new RowStyle(SizeType.Absolute, Px(38)));
            string[] heads = { "SERVICE", "DETAILS", "PORT", "PID", "STATUS", "ACTIONS" };
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
            Relayout();
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
                            if (r.Status.State != PillState.Busy) r.Status.Set(PillState.Busy, "Working…");
                            r.Toggle.Enabled = false; r.Open.Enabled = false;
                            continue;
                        }
                        if (running) r.Failed = false;
                        if (!running && r.Failed) r.Status.Set(PillState.Error, "Failed");
                        else r.Status.Set(running ? PillState.Running : PillState.Stopped, running ? "Running" : "Stopped");
                        r.Pid.Text = running ? x.pid.ToString() : "—";
                        r.Pid.ForeColor = running ? Ui.Ink : Ui.Faint;
                        r.Port.Text = r.Svc.Port.ToString();
                        r.Detail.Text = r.Svc.Detail;
                        r.Toggle.Enabled = true;
                        r.Toggle.Set(running ? "Stop" : "Start", running ? "stop" : "play", ButtonKind.Primary);
                        r.Open.Enabled = running && (r.Svc == Services.Apache || Services.Apache.IsRunning);
                    }
                    int up = t.Result.Count(x => x.pid > 0), total = t.Result.Count;
                    _tray.Text = "WebHut - " + up + " of " + total + " running";
                    bool failed = rows.Any(r => r.Failed);
                    if (!_sequenceRunning)
                    {
                        if (failed) _health.Set(PillState.Error, "A service failed to start");
                        else if (up == total) _health.Set(PillState.Running, total == 1 ? "Running" : "All " + total + " services running");
                        else if (up == 0) _health.Set(PillState.Stopped, "All services stopped");
                        else _health.Set(PillState.Partial, up + " of " + total + " running");
                    }
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
            if (row != null) { row.Toggle.Enabled = false; row.Open.Enabled = false; row.Status.Set(PillState.Busy, busyText); }
            Task.Run(act).ContinueWith(t =>
            {
                svc.Busy = false;
                if (t.IsFaulted)
                {
                    var msg = t.Exception.GetBaseException().Message;
                    if (row != null && busyText.StartsWith("Start")) row.Failed = true;
                    Log.Error(svc.Title + ": " + msg);
                    if (Visible) Ui.Error(this, svc.Title, msg);
                    if (svc == Services.MariaDb && busyText.StartsWith("Start") && Visible) OfferRebuild();
                }
                else
                {
                    string what = busyText.StartsWith("Start") ? " started" : busyText.StartsWith("Stop") ? " stopped" : busyText.StartsWith("Restart") ? " restarted" : busyText.StartsWith("Rebuild") ? " rebuilt from backup" : null;
                    if (what != null) Toast.Show(this, svc.Title + what + ".");
                }
                RefreshStatus();
                if (_page == 2) RefreshDbPage();
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
            foreach (var s in list) s.Busy = true;
            foreach (var r in _rows.Where(r => list.Contains(r.Svc))) { r.Failed = false; r.Toggle.Enabled = false; r.Open.Enabled = false; r.Status.Set(PillState.Busy, starting ? "Starting…" : "Stopping…"); }
            if (_health != null) _health.Set(PillState.Busy, starting ? "Starting services…" : "Stopping services…");
            bool dbFailed = false;
            Task.Run(() =>
            {
                foreach (var s in list)
                {
                    try { act(s); }
                    catch (Exception e)
                    {
                        Log.Error(s.Title + ": " + e.Message);
                        if (starting) { var fr = _rows.FirstOrDefault(r => r.Svc == s); if (fr != null) fr.Failed = true; }
                        if (s == Services.MariaDb) dbFailed = true;
                    }
                    finally { s.Busy = false; }
                }
            }).ContinueWith(t =>
            {
                _sequenceRunning = false;
                bool anyFailed = _rows.Any(r => r.Failed);
                Toast.Show(this, anyFailed ? (starting ? "Some services failed to start. See Activity." : "Some services failed to stop. See Activity.")
                                          : starting ? "All services started." : "All services stopped.", anyFailed ? ToastKind.Warning : ToastKind.Success);
                RefreshStatus();
                if (_page == 1) RefreshSitesPage();
                if (_page == 2) RefreshDbPage();
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
                Toast.Show(this, sites.Count == 0 ? "No sites yet: create a folder in www." : "Sites refreshed: " + sites.Count + " found.", ToastKind.Info);
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

        /// <summary>The Apache / MariaDB configuration manager, opened on that service's section.</summary>
        void ShowServerConfig(string service)
        {
            using (var f = new ConfigForm(service, null))
            {
                f.ShowDialog(this);
                if (!f.Changed) return;
                UpdateSubtitle();
                BuildRows();
                RefreshStatus();
                if (_page == 2) RefreshDbPage();
            }
        }

        void ShowSettings()
        {
            using (var f = new SettingsForm())
            {
                f.ShowDialog(this);
                if (f.Saved) Toast.Show(this, "Settings saved." + (f.RestartApache || f.RestartMariaDb ? " Restarting affected servers…" : ""));
                if (Ui.WantsDark(Cfg.Theme) != Ui.IsDark) ApplyTheme(!Ui.IsDark);
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
            // WebHut's own menu (same look as the rest of the app) instead of the plain Windows one.
            _tray.MouseUp += (s, e) => { if (e.Button == MouseButtons.Right) ShowTrayMenu(); };
            _tray.DoubleClick += (s, e) => ShowFromTray();
        }

        void ShowTrayMenu()
        {
            var svcs = new List<Service> { Services.Apache, Services.MariaDb };
            int running = svcs.Count(x => x.IsRunning);
            bool busy = _sequenceRunning || svcs.Any(x => x.Busy);
            string state = busy ? "Working…" : running == 0 ? "All services stopped" : running == svcs.Count ? "All services running" : running + " of " + svcs.Count + " services running";
            bool pma = Services.Apache.IsRunning && Services.MariaDb.IsRunning;
            PopupMenu.ShowAt(Cursor.Position, "WebHut  ·  " + state, new List<PopupItem>
            {
                new PopupItem { Title = "Open WebHut", Description = "Show the control panel", Icon = "home", Act = ShowFromTray },
                new PopupItem { Title = _anyRunning ? "Stop all" : "Start all", Description = _anyRunning ? "Apache, MariaDB and Node apps" : "Apache, MariaDB and auto-start apps",
                                Icon = _anyRunning ? "stop" : "play", Enabled = !busy, Act = ToggleAll },
                PopupItem.Line(),
                new PopupItem { Title = "Open localhost", Description = Services.Apache.IsRunning ? Services.Apache.Url : "Start Apache first", Icon = "external", Enabled = Services.Apache.IsRunning, Act = () => Sys.OpenUrl(Services.Apache.Url) },
                new PopupItem { Title = "phpMyAdmin", Description = pma ? "Manage databases in your browser" : "Start Apache and MariaDB first", Icon = "database", Enabled = pma, Act = () => Sys.OpenUrl(Services.MariaDb.Url) },
                new PopupItem { Title = "www folder", Description = "Open your sites in Explorer", Icon = "folder", Act = () => Sys.OpenFolder(K.Www) },
                PopupItem.Line(),
                new PopupItem { Title = "Quit WebHut", Description = "Close WebHut completely", Icon = "power", Danger = true, Act = ExitApp }
            }, Px(290));
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
