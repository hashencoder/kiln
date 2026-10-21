using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WebHut
{
    public class SettingsForm : Form
    {
        public bool RestartApache, RestartMariaDb, Saved;

        static int Px(float v) { return Ui.Px(v); }
        static readonly string[] ThemeIds = { "system", "light", "dark" };

        Dropdown _php, _node, _theme;
        NumberField _http, _mysql;
        Switch _startOnLaunch, _tray, _lan;
        Label _status, _pwState, _vcState, _dirty, _lanDetails;
        Callout _pwNote, _lanNote;
        IconButton _addPhp, _addNode, _updates, _vc, _pwBtn, _save, _lanMore;
        bool _busy, _loading;
        readonly ToolTip _tips = new ToolTip();

        WebHutConfig Cfg { get { return Services.Cfg; } }

        const int LabelW = 210, Inner = 660, SideW = 210;
        readonly List<NavItem> _navs = new List<NavItem>();
        readonly List<Control> _pages = new List<Control>();
        Panel _scroll;

        public SettingsForm()
        {
            Ui.Style(this);
            AutoScaleMode = AutoScaleMode.None;   // sizes are physical pixels (Ui.Px)
            Text = "WebHut settings";
            var wa = Screen.PrimaryScreen.WorkingArea;
            ClientSize = new Size(Math.Min(Px(980), wa.Width - Px(40)), Math.Min(Px(700), wa.Height - Px(60)));
            MinimumSize = new Size(Math.Min(Px(900), wa.Width - Px(20)), Math.Min(Px(520), wa.Height - Px(20)));
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            BackColor = Ui.Bg;

            // ---- controls
            _php = new Dropdown(Px(200));
            _node = new Dropdown(Px(200));
            _theme = new Dropdown(Px(200));
            _theme.Items.AddRange(new[] { "Match Windows", "Light", "Dark" });
            _http = new NumberField(1, 65535, Px(130));
            _mysql = new NumberField(1, 65535, Px(130));
            _startOnLaunch = new Switch();
            _tray = new Switch();
            _lan = new Switch();
            _status = Text2("", Ui.Small, Ui.Muted, Px(Inner));
            _pwState = Text2("", Ui.Bold, Ui.Ink, Px(200));
            _vcState = Text2("", Ui.Bold, Ui.Ink, Px(240));
            _addPhp = Compact("Install version", "plus", "Download another PHP version (Thread Safe x64)");
            _addPhp.Click += (s, e) => AddPhp();
            _addNode = Compact("Install latest LTS", "plus", "Download the newest Node.js LTS");
            _addNode.Click += (s, e) => AddNode();
            _pwBtn = Compact("Change password", "key", "Set or remove the MariaDB root password");
            _pwBtn.Click += (s, e) => ChangePassword();
            _updates = Compact("Check for updates", "refresh", "Compare Apache, PHP, MariaDB, phpMyAdmin and Node.js with the latest versions");
            _updates.Click += (s, e) => CheckUpdates();
            _vc = Compact("Install / repair", "gear", "Run Microsoft's Visual C++ installer (asks for admin permission)");
            _vc.Click += (s, e) => InstallVc();

            // ---- pages (one per sidebar category)
            var general = Page("General", "How WebHut starts, closes and looks.");
            var startup = Section(general, "power", "Startup & behaviour", "What happens when WebHut opens and closes.");
            Toggle(startup, _startOnLaunch, "Start servers when WebHut opens", "Apache, MariaDB, and Node apps marked \"Start with Start all\".");
            Toggle(startup, _tray, "Keep running in the system tray", "Closing the window hides WebHut; servers keep running. Use Quit WebHut to close it.");
            Row(startup, "Appearance", "Match Windows follows its light/dark setting.", Line(_theme));

            var runtimePage = Page("Runtime", "Which PHP and Node.js versions your sites and apps use.");
            var runtime = Section(runtimePage, "cube", "Versions", "Several versions can be installed side by side.");
            Row(runtime, "PHP version", "Used by Apache and the Shell. Saving a change restarts Apache.", Line(_php, _addPhp));
            Row(runtime, "Node.js version", "Used by Node apps and the Shell. Restart running Node apps after switching.", Line(_node, _addNode));

            var servicesPage = Page("Services", "Ports and database access.");
            var server = Section(servicesPage, "server", "Ports", "Change these only if another program already uses them.");
            Row(server, "HTTP port", "Apache · default 80. Saving a change restarts Apache.", Line(_http));
            Row(server, "MySQL port", "MariaDB · default 3306. Saving a change restarts MariaDB; update your apps' database settings too.", Line(_mysql));
            var db = Section(servicesPage, "database", "Database", "MariaDB only accepts connections from this PC (127.0.0.1).");
            Row(db, "Root password", "User root, used by phpMyAdmin and your apps.", Line(_pwState, _pwBtn));
            _pwNote = new Callout(CalloutKind.Warning, "", Px(Inner - 40));
            Full(db, _pwNote);

            var securityPage = Page("Security", "Who can reach your sites.");
            var network = Section(securityPage, "globe", "Network access", "Off by default: only this PC can open your sites.");
            Toggle(network, _lan, "Allow access from other devices", "Phones and other PCs on your Wi-Fi can open your websites at this PC's IP address. Saving a change restarts Apache.");
            _lanMore = new IconButton("How it works", "info", ButtonKind.Ghost) { Font = Ui.Small, IconColor = Ui.Muted, Margin = new Padding(Px(46), 0, 0, Px(4)) };
            _lanDetails = Text2("Only private network addresses (home or office Wi-Fi/LAN) are allowed; requests from the internet are refused. " +
                "This opens your websites only: phpMyAdmin and the MariaDB server always stay reachable from this PC only. " +
                "Windows may ask to allow Apache through the firewall - choose Private networks. Turn this off on public Wi-Fi (cafés, airports).", Ui.Small, Ui.Muted, Px(Inner - 92));
            _lanDetails.Margin = new Padding(Px(52), 0, 0, Px(8));
            _lanDetails.Visible = false;
            _lanMore.Click += (s, e) => { _lanDetails.Visible = !_lanDetails.Visible; _lanMore.Text = _lanDetails.Visible ? "Hide details" : "How it works"; };
            Full(network, _lanMore);
            Full(network, _lanDetails);
            _lanNote = new Callout(CalloutKind.Warning, "", Px(Inner - 40));
            Full(network, _lanNote);
            var always = Section(securityPage, "key", "Always private", "These never change with the setting above.");
            Full(always, new Callout(CalloutKind.Info, "phpMyAdmin (http://localhost/phpmyadmin) and MariaDB (port " + Cfg.MysqlPort + ") only accept connections from this PC, even when network access is on.", Px(Inner - 40)));

            var maintPage = Page("Maintenance", "Keep components current and repair the runtime.");
            var maint = Section(maintPage, "refresh", "Components", "Previous versions are kept in tmp\\backup when you update.");
            Row(maint, "Component updates", "Apache, PHP, MariaDB, phpMyAdmin, Node.js.", Line(_updates));
            Row(maint, "Visual C++ runtime", "Required by Apache and PHP.", Line(_vcState, _vc));
            Full(maint, _status);

            _scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Ui.Bg };
            foreach (var pg in _pages) _scroll.Controls.Add(pg);

            // ---- sidebar
            var side = new Panel { Dock = DockStyle.Left, Width = Px(SideW), BackColor = Ui.Sidebar, Padding = new Padding(Px(12), Px(20), Px(12), Px(12)) };
            side.Paint += (s, e) => { using (var pen = new Pen(Ui.Line)) e.Graphics.DrawLine(pen, side.Width - 1, 0, side.Width - 1, side.Height); };
            var navList = new FlowLayoutPanel { Dock = DockStyle.Top, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, BackColor = Ui.Sidebar, Padding = new Padding(0, Px(12), 0, 0) };
            string[,] cats = { { "General", "gear" }, { "Runtime", "cube" }, { "Services", "server" }, { "Security", "globe" }, { "Maintenance", "refresh" } };
            for (int i = 0; i < cats.GetLength(0); i++)
            {
                int idx = i;
                var n = new NavItem(cats[i, 0], cats[i, 1]) { Width = Px(SideW - 24 - 1) };
                n.Click += (s, e) => ShowPage(idx);
                _navs.Add(n);
                navList.Controls.Add(n);
            }
            side.Controls.Add(navList);
            side.Controls.Add(new Label { Text = "Settings", Font = Ui.SectionTitle, ForeColor = Ui.Ink, Dock = DockStyle.Top, Height = Px(30), Padding = new Padding(Px(8), 0, 0, 0), BackColor = Ui.Sidebar });

            // ---- sticky footer
            var footer = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = Px(68), ColumnCount = 3, BackColor = Ui.Card, Padding = new Padding(Px(24), Px(14), Px(24), Px(14)) };
            footer.Paint += (s, e) => { using (var pen = new Pen(Ui.Line)) e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0); };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _dirty = new Label { Text = "", Font = Ui.Bold, ForeColor = Ui.Amber, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0) };
            var cancel = new IconButton("Cancel", null, ButtonKind.Outline) { Anchor = AnchorStyles.Right, Margin = new Padding(0, 0, Px(10), 0) };
            cancel.SetMin(Px(100), Px(40));
            cancel.Click += (s, e) => Close();
            _save = new IconButton("Save changes", null, ButtonKind.Primary) { Anchor = AnchorStyles.Right, Margin = new Padding(0) };
            _save.SetMin(Px(140), Px(40));
            _save.Click += (s, e) => SaveAndClose();
            footer.Controls.Add(_dirty, 0, 0);
            footer.Controls.Add(cancel, 1, 0);
            footer.Controls.Add(_save, 2, 0);

            Controls.Add(ScrollHost.Wrap(_scroll));
            Controls.Add(side);
            Controls.Add(footer);
            ShowPage(0);
            CancelButton = cancel;

            EventHandler changed = (s, e) => OnChanged();
            _php.SelectedIndexChanged += changed; _node.SelectedIndexChanged += changed; _theme.SelectedIndexChanged += changed;
            _http.ValueChanged += changed; _mysql.ValueChanged += changed;
            _startOnLaunch.CheckedChanged += changed; _tray.CheckedChanged += changed; _lan.CheckedChanged += changed;
            Load += (s, e) => Populate();
        }

        // ---------------------------------------------------------------- building blocks

        static Label Text2(string text, Font font, Color color, int maxWidth)
        {
            return new Label { Text = text, Font = font, ForeColor = color, AutoSize = true, MaximumSize = new Size(maxWidth, 0), BackColor = Color.Transparent, Margin = new Padding(0) };
        }

        IconButton Compact(string text, string icon, string tip)
        {
            var b = new IconButton(text, icon, ButtonKind.Outline) { Font = Ui.BtnSmall, IconColor = Ui.NavInk, Margin = new Padding(Px(10), 0, 0, 0), Anchor = AnchorStyles.Left };
            b.SetMin(0, Px(38));
            if (tip != null) _tips.SetToolTip(b, tip);
            return b;
        }

        static FlowLayoutPanel Line(params Control[] items)
        {
            var f = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, BackColor = Color.Transparent, Margin = new Padding(0) };
            foreach (var c in items) { if (c is Label) c.Anchor = AnchorStyles.Left; f.Controls.Add(c); }
            if (items.Length > 0 && items[0] is Label) items[0].Margin = new Padding(0, Px(10), Px(4), 0);
            return f;
        }

        TableLayoutPanel Page(string title, string desc)
        {
            var page = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, BackColor = Ui.Bg, Padding = new Padding(Px(28), Px(22), Px(28), Px(8)), Visible = false };
            page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            page.Controls.Add(new Label { Text = title, Font = Ui.H2, ForeColor = Ui.Ink, AutoSize = true, Margin = new Padding(0, 0, 0, Px(2)) });
            page.Controls.Add(new Label { Text = desc, Font = Ui.Base, ForeColor = Ui.Muted, AutoSize = true, Margin = new Padding(0, 0, 0, Px(16)) });
            _pages.Add(page);
            return page;
        }

        void ShowPage(int idx)
        {
            SuspendLayout();
            for (int i = 0; i < _pages.Count; i++) _pages[i].Visible = i == idx;
            for (int i = 0; i < _navs.Count; i++) { _navs[i].Active = i == idx; _navs[i].Invalidate(); }
            _scroll.AutoScrollPosition = new Point(0, 0);
            ResumeLayout(true);
        }

        /// <summary>A rounded card: icon + title + description, then a two-column grid of rows.</summary>
        static TableLayoutPanel Section(TableLayoutPanel page, string icon, string title, string desc)
        {
            var t = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, BackColor = Ui.Bg, Dock = DockStyle.Top,
                                           Padding = new Padding(Px(20), Px(18), Px(20), Px(10)), Margin = new Padding(0, 0, 0, Px(16)) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Px(LabelW)));
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
            Add(t, head, 0, 2);
            var d = new Label { Text = desc, Font = Ui.Small, ForeColor = Ui.Muted, AutoSize = true, BackColor = Color.Transparent, Margin = new Padding(Px(30), 0, 0, Px(12)) };
            Add(t, d, 0, 2);
            page.Controls.Add(t);
            return t;
        }

        static void Add(TableLayoutPanel t, Control c, int col, int span)
        {
            int r = t.RowCount++;
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.Controls.Add(c, col, r);
            if (span > 1) t.SetColumnSpan(c, span);
        }

        /// <summary>Label + hint on the left, control(s) on the right, separated by a hairline.</summary>
        static void Row(TableLayoutPanel t, string label, string hint, Control control)
        {
            int r = t.RowCount++;
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var left = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, BackColor = Color.Transparent, Margin = new Padding(0, Px(12), Px(12), Px(12)) };
            left.Controls.Add(new Label { Text = label, Font = Ui.Bold, ForeColor = Ui.Ink, AutoSize = true, BackColor = Color.Transparent, Margin = new Padding(0) });
            left.Controls.Add(new Label { Text = hint, Font = Ui.Small, ForeColor = Ui.Muted, AutoSize = true, MaximumSize = new Size(Px(LabelW - 16), 0), BackColor = Color.Transparent, Margin = new Padding(0, Px(2), 0, 0) });
            control.Margin = new Padding(0, Px(12), 0, Px(12));
            control.Anchor = AnchorStyles.Left | AnchorStyles.Top;
            t.Controls.Add(left, 0, r);
            t.Controls.Add(control, 1, r);
        }

        /// <summary>Switch + title + description across both columns.</summary>
        static void Toggle(TableLayoutPanel t, Switch sw, string title, string desc)
        {
            var row = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, BackColor = Color.Transparent, Margin = new Padding(0, Px(10), 0, Px(10)), Cursor = Cursors.Hand };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Px(52)));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            sw.Margin = new Padding(0, Px(2), 0, 0);
            sw.Anchor = AnchorStyles.Left | AnchorStyles.Top;
            var text = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, BackColor = Color.Transparent, Margin = new Padding(0) };
            var l1 = new Label { Text = title, Font = Ui.Bold, ForeColor = Ui.Ink, AutoSize = true, BackColor = Color.Transparent, Margin = new Padding(0) };
            var l2 = new Label { Text = desc, Font = Ui.Small, ForeColor = Ui.Muted, AutoSize = true, MaximumSize = new Size(Px(Inner - 100), 0), BackColor = Color.Transparent, Margin = new Padding(0, Px(2), 0, 0) };
            text.Controls.Add(l1); text.Controls.Add(l2);
            EventHandler flip = (s, e) => sw.Checked = !sw.Checked;
            l1.Click += flip; l2.Click += flip;
            row.Controls.Add(sw, 0, 0);
            row.Controls.Add(text, 1, 0);
            Add(t, row, 0, 2);
        }

        static void Full(TableLayoutPanel t, Control c) { Add(t, c, 0, 2); }

        // ---------------------------------------------------------------- state

        void Populate()
        {
            _loading = true;
            _php.Items.Clear();
            _php.Items.AddRange(WebHutConfig.InstalledPhp());
            _php.SelectedItem = Cfg.Php;
            if (_php.SelectedIndex < 0 && _php.Items.Count > 0) _php.SelectedIndex = 0;
            _node.Items.Clear();
            _node.Items.AddRange(WebHutConfig.InstalledNode());
            _node.SelectedItem = Cfg.Node;
            if (_node.SelectedIndex < 0 && _node.Items.Count > 0) _node.SelectedIndex = 0;
            _theme.SelectedIndex = Math.Max(0, Array.IndexOf(ThemeIds, WebHutConfig.NormTheme(Cfg.Theme)));
            _http.Value = Cfg.HttpPort;
            _mysql.Value = Cfg.MysqlPort;
            _startOnLaunch.Checked = Cfg.StartOnLaunch;
            _tray.Checked = Cfg.MinimizeToTray;
            _lan.Checked = Cfg.LanAccess;
            bool vc = Installer.VcRedistOk();
            _vcState.Text = vc ? "Installed" : "Missing or outdated";
            _vcState.ForeColor = vc ? Ui.Green : Ui.Amber;
            _loading = false;
            OnChanged();
        }

        bool NoPassword { get { return string.IsNullOrEmpty(Cfg.MysqlRootPassword); } }

        bool IsDirty()
        {
            return (_php.SelectedItem ?? Cfg.Php) != Cfg.Php || (_node.SelectedItem ?? Cfg.Node) != Cfg.Node
                || ThemeIds[Math.Max(0, _theme.SelectedIndex)] != WebHutConfig.NormTheme(Cfg.Theme)
                || _http.Value != Cfg.HttpPort || _mysql.Value != Cfg.MysqlPort
                || _startOnLaunch.Checked != Cfg.StartOnLaunch || _tray.Checked != Cfg.MinimizeToTray || _lan.Checked != Cfg.LanAccess;
        }

        void OnChanged()
        {
            if (_loading) return;
            bool dirty = IsDirty();
            var restarts = new List<string>();
            if ((_php.SelectedItem ?? Cfg.Php) != Cfg.Php || _http.Value != Cfg.HttpPort || _mysql.Value != Cfg.MysqlPort || _lan.Checked != Cfg.LanAccess) restarts.Add("Apache");
            if (_mysql.Value != Cfg.MysqlPort) restarts.Add("MariaDB");
            _dirty.Text = !dirty ? "" : "●  Unsaved changes" + (restarts.Count == 0 ? "" : "  ·  saving restarts " + string.Join(" and ", restarts));
            _save.Enabled = dirty && !_busy;

            _pwState.Text = NoPassword ? "No password" : "Password set";
            _pwState.ForeColor = NoPassword ? Ui.Amber : Ui.Green;
            if (NoPassword)
                _pwNote.Set(_lan.Checked ? CalloutKind.Danger : CalloutKind.Warning, _lan.Checked
                    ? "The root user has no password while network access is on. MariaDB itself only listens on this PC, but any site or script on this PC can read every database. Set a password if this PC is shared."
                    : "No root password is fine for a local-only setup, like XAMPP. Set one if other people use this PC or before turning on network access.");
            _pwNote.Visible = NoPassword;

            if (_lan.Checked)
            {
                var ips = Sys.LanAddresses();
                _lanNote.Set(CalloutKind.Warning, "Your sites will be visible to other devices on this network" +
                    (ips.Count > 0 ? " at http://" + ips[0] + (_http.Value == 80 ? "" : ":" + _http.Value) + "/" : "") +
                    ". Use this only on networks you trust. phpMyAdmin and the database stay on this PC.");
            }
            _lanNote.Visible = _lan.Checked;
        }

        void SaveAndClose()
        {
            if (_busy) return;
            if (!_http.IsValid || !_mysql.IsValid) { Ui.Error(this, "Settings", "Ports must be numbers between 1 and 65535."); return; }
            int http = _http.Value, mysql = _mysql.Value;
            if (http == mysql) { Ui.Error(this, "Settings", "HTTP and MySQL ports must differ."); return; }
            var clash = NodeApp.LoadAll().FirstOrDefault(a => a.Port == http || a.Port == mysql);
            if (clash != null) { Ui.Error(this, "Settings", "Port " + clash.Port + " is used by the Node app '" + clash.Name + "'."); return; }

            string php = _php.SelectedItem ?? Cfg.Php;
            string node = _node.SelectedItem ?? Cfg.Node;
            RestartApache = php != Cfg.Php || http != Cfg.HttpPort || mysql != Cfg.MysqlPort || _lan.Checked != Cfg.LanAccess;
            bool mysqlChanged = mysql != Cfg.MysqlPort;
            // MainForm restarts it in the background; Stop() uses the port it was started on.
            RestartMariaDb = mysqlChanged;
            bool restartNodeHint = node != Cfg.Node;

            Cfg.Php = php;
            Cfg.Node = node;
            Cfg.HttpPort = http;
            Cfg.MysqlPort = mysql;
            Cfg.StartOnLaunch = _startOnLaunch.Checked;
            Cfg.MinimizeToTray = _tray.Checked;
            Cfg.LanAccess = _lan.Checked;
            Cfg.Theme = ThemeIds[Math.Max(0, _theme.SelectedIndex)];
            Cfg.Save();
            if (mysqlChanged)
            {
                try { new Installer(Cfg).UpdatePmaConfig(); } catch (Exception e) { Log.Error(e.Message); }
            }
            if (restartNodeHint) Log.Info("Node.js " + node + " selected. Restart running Node apps to use it.");
            Log.Info("Settings saved.");
            Saved = true;
            Close();
        }

        void SetBusy(bool on, string msg)
        {
            _busy = on;
            _addPhp.Enabled = _addNode.Enabled = _updates.Enabled = _vc.Enabled = _pwBtn.Enabled = !on;
            _save.Enabled = !on && IsDirty();
            _status.Text = msg ?? "";
            Cursor = on ? Cursors.AppStarting : Cursors.Default;
        }

        void RunBg(string msg, Action act, Action done)
        {
            SetBusy(true, msg);
            Task.Run(act).ContinueWith(t =>
            {
                if (IsDisposed)
                {
                    if (t.IsFaulted) Log.Error(t.Exception.GetBaseException().Message);
                    return;
                }
                SetBusy(false, t.IsFaulted ? "Failed: " + t.Exception.GetBaseException().Message : "Done.");
                if (t.IsFaulted) Ui.Error(this, "WebHut", t.Exception.GetBaseException().Message);
                RefreshLists();
                if (!t.IsFaulted && done != null) done();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        /// <summary>Reload installed versions and status without discarding unsaved edits.</summary>
        void RefreshLists()
        {
            string php = _php.SelectedItem, node = _node.SelectedItem;
            _loading = true;
            _php.Items.Clear(); _php.Items.AddRange(WebHutConfig.InstalledPhp());
            _php.SelectedItem = php ?? Cfg.Php;
            if (_php.SelectedIndex < 0 && _php.Items.Count > 0) _php.SelectedIndex = 0;
            _node.Items.Clear(); _node.Items.AddRange(WebHutConfig.InstalledNode());
            _node.SelectedItem = node ?? Cfg.Node;
            if (_node.SelectedIndex < 0 && _node.Items.Count > 0) _node.SelectedIndex = 0;
            bool vc = Installer.VcRedistOk();
            _vcState.Text = vc ? "Installed" : "Missing or outdated";
            _vcState.ForeColor = vc ? Ui.Green : Ui.Amber;
            _loading = false;
            OnChanged();
            _php.Invalidate(); _node.Invalidate();
        }

        Installer NewInstaller()
        {
            var inst = new Installer(Cfg);
            inst.Progress += (m, p) => { try { BeginInvoke(new Action(() => _status.Text = m)); } catch { } };
            return inst;
        }

        void AddPhp()
        {
            SetBusy(true, "Fetching available PHP versions…");
            Task.Run(() => Resolver.PhpBranches()).ContinueWith(t =>
            {
                if (IsDisposed) return;
                SetBusy(false, "");
                if (t.IsFaulted) { Ui.Error(this, "WebHut", "Couldn't reach windows.php.net: " + t.Exception.GetBaseException().Message); return; }
                var installed = WebHutConfig.InstalledPhp();
                var choices = t.Result.Where(b => !installed.Contains(b)).ToList();
                if (choices.Count == 0) { MessageBox.Show(this, "Every supported PHP version is already installed.", "WebHut"); return; }
                string branch = Choose("Install PHP", "PHP version to install:", choices);
                if (branch == null) return;
                RunBg("Installing PHP " + branch + "…", () =>
                {
                    var c = Resolver.Php(Cfg, branch);
                    NewInstaller().Install(c, CancellationToken.None);
                }, () => _php.SelectedItem = branch);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void AddNode()
        {
            RunBg("Installing the latest Node.js LTS…", () =>
            {
                var c = Resolver.Node(Cfg, null);
                if (WebHutConfig.InstalledNode().Contains(c.Branch) && WebHutConfig.FullVersion(K.NodeDir(c.Branch)) == c.Version)
                {
                    Log.Info("Node.js " + c.Version + " is already installed.");
                    return;
                }
                NewInstaller().Install(c, CancellationToken.None);
            }, null);
        }

        void InstallVc()
        {
            RunBg("Installing Visual C++ Redistributable…", () => NewInstaller().InstallVcRedist(CancellationToken.None), null);
        }

        void ChangePassword()
        {
            var pw = Ui.Prompt(this, "Database root password", "New root password (leave empty for none):", true);
            if (pw == null) return;
            string old = Cfg.MysqlRootPassword;
            RunBg("Changing root password…", () =>
            {
                bool wasRunning = Services.MariaDb.IsRunning;
                if (!wasRunning) Services.MariaDb.Start();
                Services.MariaDb.ChangeRootPassword(old, pw);
                Cfg.MysqlRootPassword = pw;
                Cfg.Save();
                new Installer(Cfg).UpdatePmaConfig();
                Log.Info("Root password changed.");
            }, null);
        }

        void CheckUpdates()
        {
            using (var f = new UpdatesForm()) f.ShowDialog(this);
            RefreshLists();
        }

        string Choose(string title, string label, List<string> items)
        {
            using (var f = new Form { Text = title, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(360, 130), ShowInTaskbar = false })
            {
                Ui.Style(f);
                var l = Ui.Lbl(label); l.Location = new Point(14, 14);
                var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(16, 40), Width = 288 };
                foreach (var i in items) c.Items.Add(i);
                c.SelectedIndex = 0;
                var ok = Ui.Btn("Install", null, true, 90); ok.Location = new Point(116, 78); ok.DialogResult = DialogResult.OK;
                var cancel = Ui.Btn("Cancel", null, false, 90); cancel.Location = new Point(214, 78); cancel.DialogResult = DialogResult.Cancel;
                f.Controls.AddRange(new Control[] { l, c, ok, cancel });
                f.AcceptButton = ok; f.CancelButton = cancel;
                return f.ShowDialog(this) == DialogResult.OK ? c.SelectedItem as string : null;
            }
        }
    }

}
