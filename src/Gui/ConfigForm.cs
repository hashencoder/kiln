using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WebHut
{
    /// <summary>
    /// Server configuration manager for Apache and MariaDB. Shows each setting's owner and effective value,
    /// collects edits, and hands them to <see cref="ConfigManager"/>, which validates, backs up, applies,
    /// restarts only what's affected, verifies and rolls back.
    /// </summary>
    public class ConfigForm : Form
    {
        static int Px(float v) { return Ui.Px(v); }
        const int LabelW = 230, SideW = 220;
        static int CalloutW { get { return Px(600); } }

        public static readonly string[] ApachePages = { "General", "PHP Integration", "Performance", "Modules", "Security", "Logging", "Virtual Hosts", "Advanced" };
        static readonly string[] ApacheIcons = { "gear", "cube", "bolt", "server", "key", "logs", "globe", "terminal" };
        public static readonly string[] MariaPages = { "General", "Performance", "InnoDB", "Reliability & Recovery", "Security", "Logging & Diagnostics", "Advanced" };
        static readonly string[] MariaIcons = { "gear", "bolt", "database", "refresh", "key", "pulse", "terminal" };

        /// <summary>True when a change was applied (the main window refreshes).</summary>
        public bool Changed;

        WebHutConfig Cfg { get { return Services.Cfg; } }
        ApacheConfFile _hc;
        ApacheInfo _ai;
        MariaInfo _mi;
        OptionFile _ini;
        List<VhostFile> _vhosts = new List<VhostFile>();
        Dictionary<string, string> _overrides = new Dictionary<string, string>();
        ConfigEdits _e = new ConfigEdits();
        bool _loading = true, _busy;

        readonly Segmented _svc;
        readonly FlowLayoutPanel _navList;
        readonly Panel _scroll;
        readonly Label _status, _loadingText;
        readonly LinkLabel _details;
        readonly ProgressLine _bar;
        readonly IconButton _save, _cancel;
        readonly ToolTip _tips = new ToolTip();
        string _service;
        int _page;
        string _lastDetails = "";
        readonly Dictionary<string, Control> _built = new Dictionary<string, Control>();
        readonly List<Action> _refresh = new List<Action>();
        readonly List<Timer> _timers = new List<Timer>();

        public ConfigForm(string service, string page)
        {
            Ui.Style(this);
            AutoScaleMode = AutoScaleMode.None;
            Text = "Server configuration";
            var wa = Screen.PrimaryScreen.WorkingArea;
            ClientSize = new Size(Math.Min(Px(1040), wa.Width - Px(40)), Math.Min(Px(720), wa.Height - Px(60)));
            MinimumSize = new Size(Math.Min(Px(900), wa.Width - Px(20)), Math.Min(Px(560), wa.Height - Px(20)));
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            BackColor = Ui.Bg;
            _service = service == "mariadb" ? "mariadb" : "apache";
            var pages = _service == "apache" ? ApachePages : MariaPages;
            _page = Math.Max(0, Array.FindIndex(pages, x => string.Equals(x, page, StringComparison.OrdinalIgnoreCase)));

            _scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Ui.Bg };
            _loadingText = new Label { UseMnemonic = false, Text = "Reading the Apache and MariaDB configuration…", Font = Ui.Base, ForeColor = Ui.Muted, AutoSize = true, Location = new Point(Px(28), Px(28)) };
            _scroll.Controls.Add(_loadingText);

            // ---- sidebar: service selector + pages of that service
            var side = new Panel { Dock = DockStyle.Left, Width = Px(SideW), BackColor = Ui.Sidebar, Padding = new Padding(Px(12), Px(18), Px(12), Px(12)) };
            side.Paint += (s, e) => { using (var pen = new Pen(Ui.Line)) e.Graphics.DrawLine(pen, side.Width - 1, 0, side.Width - 1, side.Height); };
            _navList = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Ui.Sidebar, Padding = new Padding(0, Px(10), 0, 0) };
            _svc = new Segmented("Apache", "MariaDB") { Dock = DockStyle.Top, Margin = new Padding(0) };
            _svc.SelectedIndex = _service == "apache" ? 0 : 1;
            _svc.SelectedChanged += (s, e) => { _service = _svc.SelectedIndex == 0 ? "apache" : "mariadb"; _page = 0; BuildNav(); if (!_loading) ShowPage(); };
            var svcHost = new Panel { Dock = DockStyle.Top, Height = Px(40), BackColor = Ui.Sidebar };
            svcHost.Controls.Add(_svc);
            _svc.Width = Px(SideW - 24);
            side.Controls.Add(_navList);
            side.Controls.Add(svcHost);
            side.Controls.Add(new Label { UseMnemonic = false, Text = "Server configuration", Font = Ui.SectionTitle, ForeColor = Ui.Ink, Dock = DockStyle.Top, Height = Px(36), Padding = new Padding(Px(6), 0, 0, 0), BackColor = Ui.Sidebar });

            // ---- sticky footer
            var footer = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = Px(72), ColumnCount = 3, BackColor = Ui.Card, Padding = new Padding(Px(24), Px(10), Px(24), Px(10)) };
            footer.Paint += (s, e) => { using (var pen = new Pen(Ui.Line)) e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0); };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var left = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, Dock = DockStyle.Fill, BackColor = Color.Transparent, Margin = new Padding(0) };
            _status = new Label { UseMnemonic = false, Text = "", Font = Ui.SmallBold, ForeColor = Ui.Muted, AutoSize = true, Margin = new Padding(0, Px(4), 0, 0) };
            _details = new LinkLabel { Text = "Show details", Font = Ui.Small, AutoSize = true, Visible = false, LinkColor = Ui.AccentInk, ActiveLinkColor = Ui.AccentHover, LinkBehavior = LinkBehavior.HoverUnderline, Margin = new Padding(0, Px(2), 0, 0) };
            _details.LinkClicked += (s, e) => TextDialog.Show(this, "Technical details", _lastDetails);
            _bar = new ProgressLine { Width = Px(240), Visible = false, Margin = new Padding(0, Px(6), 0, 0) };
            left.Controls.Add(_status); left.Controls.Add(_details); left.Controls.Add(_bar);
            left.SizeChanged += (s, e) => _status.MaximumSize = new Size(Math.Max(Px(200), left.Width), 0);
            _cancel = new IconButton("Close", null, ButtonKind.Outline) { Anchor = AnchorStyles.Right, Margin = new Padding(0, 0, Px(10), 0) };
            _cancel.SetMin(Px(100), Px(40));
            _cancel.Click += (s, e) => { if (_e.IsEmpty) Close(); else Discard(); };
            _save = new IconButton("Save changes", null, ButtonKind.Primary) { Anchor = AnchorStyles.Right, Margin = new Padding(0), Enabled = false };
            _save.SetMin(Px(140), Px(40));
            _save.Click += (s, e) => Save();
            footer.Controls.Add(left, 0, 0);
            footer.Controls.Add(_cancel, 1, 0);
            footer.Controls.Add(_save, 2, 0);

            Controls.Add(_scroll);
            Controls.Add(side);
            Controls.Add(footer);
            BuildNav();

            FormClosing += (s, e) =>
            {
                if (_busy) { e.Cancel = true; Toast.Show(this, "Please wait until the change has been applied.", ToastKind.Warning); return; }
                if (!_e.IsEmpty && e.CloseReason == CloseReason.UserClosing &&
                    MessageBox.Show(this, "Discard your unsaved changes?", "Server configuration", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    e.Cancel = true;
            };
            FormClosed += (s, e) => { foreach (var t in _timers) t.Dispose(); };
            Shown += (s, e) => Reload(null);
        }

        // ================================================================== loading

        void Reload(Action done)
        {
            _loading = true;
            SetBusy(true, "Reading the current configuration…");
            var cfg = Cfg;
            Task.Run(() =>
            {
                var hc = File.Exists(K.HttpdConf) ? ApacheConfFile.Load(K.HttpdConf) : ApacheConfFile.Parse(Installer.Template("config/apache/httpd.conf"));
                var ini = File.Exists(K.MyIni) ? OptionFile.Load(K.MyIni) : OptionFile.Parse(Installer.Template("config/mariadb/my.ini"));
                var ai = ApacheInfo.Probe(cfg);
                var mi = MariaInfo.Probe(cfg);
                var vh = VhostAudit.Scan(cfg.LanAccess);
                var ov = VhostAudit.Overrides("KeepAlive", "KeepAliveTimeout", "MaxKeepAliveRequests", "LogLevel", "ServerName");
                return new object[] { hc, ini, ai, mi, vh, ov };
            }).ContinueWith(t =>
            {
                if (IsDisposed) return;
                SetBusy(false, "");
                if (t.IsFaulted)
                {
                    _loadingText.Text = "Couldn't read the configuration: " + t.Exception.GetBaseException().Message;
                    _loadingText.ForeColor = Ui.Red;
                    return;
                }
                var r = t.Result;
                _hc = (ApacheConfFile)r[0]; _ini = (OptionFile)r[1]; _ai = (ApacheInfo)r[2]; _mi = (MariaInfo)r[3];
                _vhosts = (List<VhostFile>)r[4]; _overrides = (Dictionary<string, string>)r[5];
                _e = new ConfigEdits();
                foreach (var c in _built.Values) { _scroll.Controls.Remove(c); c.Dispose(); }
                _built.Clear();
                _refresh.Clear();
                _loadingText.Visible = false;
                _loading = false;
                ShowPage();
                OnChanged();
                if (done != null) done();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void BuildNav()
        {
            _navList.SuspendLayout();
            foreach (Control c in _navList.Controls.Cast<Control>().ToList()) c.Dispose();
            _navList.Controls.Clear();
            var pages = _service == "apache" ? ApachePages : MariaPages;
            var icons = _service == "apache" ? ApacheIcons : MariaIcons;
            for (int i = 0; i < pages.Length; i++)
            {
                int idx = i;
                var n = new NavItem(pages[i], icons[i]) { Width = Px(SideW - 26), Height = Px(38), Active = i == _page, Margin = new Padding(0, 0, 0, Px(2)) };
                n.Click += (s, e) => { _page = idx; foreach (NavItem x in _navList.Controls) { x.Active = x == n; x.Invalidate(); } if (!_loading) ShowPage(); };
                _navList.Controls.Add(n);
            }
            _navList.ResumeLayout(true);
        }

        void ShowPage()
        {
            string key = _service + ":" + _page;
            Control page;
            if (!_built.TryGetValue(key, out page))
            {
                page = BuildPage(_service, _page);
                page.Visible = false;
                _built[key] = page;
                _scroll.Controls.Add(page);
                Ui.ThemeTree(page);
                bool was = _loading; _loading = false;
                foreach (var r in _refresh) r();
                _loading = was;
            }
            _scroll.SuspendLayout();
            foreach (var p in _built) p.Value.Visible = p.Key == key;
            _scroll.AutoScrollPosition = new Point(0, 0);
            _scroll.ResumeLayout(true);
            foreach (NavItem x in _navList.Controls) { x.Active = _navList.Controls.IndexOf(x) == _page; x.Invalidate(); }
        }

        /// <summary>Jump to a page (used by "Change…" links between sections).</summary>
        void Go(string service, string page)
        {
            if (service != _service) { _service = service; _svc.SelectedIndex = service == "apache" ? 0 : 1; }
            var pages = service == "apache" ? ApachePages : MariaPages;
            _page = Array.IndexOf(pages, page);
            BuildNav();
            ShowPage();
        }

        Control BuildPage(string svc, int i)
        {
            if (svc == "apache")
                switch (i)
                {
                    case 0: return ApGeneral();
                    case 1: return ApPhp();
                    case 2: return ApPerformance();
                    case 3: return ApModules();
                    case 4: return ApSecurity();
                    case 5: return ApLogging();
                    case 6: return ApVhosts();
                    default: return ApAdvanced();
                }
            switch (i)
            {
                case 0: return MyGeneral();
                case 1: return MyPerformance();
                case 2: return MyInnoDb();
                case 3: return MyReliability();
                case 4: return MySecurity();
                case 5: return MyLogging();
                default: return MyAdvanced();
            }
        }

        // ================================================================== building blocks

        TableLayoutPanel Page(string title, string desc)
        {
            var page = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, BackColor = Ui.Bg, Padding = new Padding(Px(28), Px(22), Px(28), Px(16)) };
            page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            page.Controls.Add(new Label { UseMnemonic = false, Text = title, Font = Ui.H2, ForeColor = Ui.Ink, AutoSize = true, Margin = new Padding(0, 0, 0, Px(2)) });
            page.Controls.Add(new Label { UseMnemonic = false, Text = desc, Font = Ui.Base, ForeColor = Ui.Muted, AutoSize = true, MaximumSize = new Size(Px(760), 0), Margin = new Padding(0, 0, 0, Px(16)) });
            return page;
        }

        static TableLayoutPanel Section(TableLayoutPanel page, string icon, string title, string desc)
        {
            var t = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, BackColor = Ui.Bg, Dock = DockStyle.Top,
                                           Padding = new Padding(Px(20), Px(16), Px(20), Px(8)), Margin = new Padding(0, 0, 0, Px(14)) };
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
            head.Controls.Add(new IconBox(icon, Ui.NavInk, Px(20)) { Margin = new Padding(0, Px(3), Px(10), 0) });
            head.Controls.Add(new Label { UseMnemonic = false, Text = title, Font = Ui.SectionTitle, ForeColor = Ui.Ink, AutoSize = true, BackColor = Color.Transparent, Margin = new Padding(0) });
            Add(t, head);
            if (!string.IsNullOrEmpty(desc))
                Add(t, new Label { UseMnemonic = false, Text = desc, Font = Ui.Small, ForeColor = Ui.Muted, AutoSize = true, MaximumSize = new Size(Px(680), 0), BackColor = Color.Transparent, Margin = new Padding(Px(30), 0, 0, Px(8)) });
            page.Controls.Add(t);
            return t;
        }

        static void Add(TableLayoutPanel t, Control c)
        {
            int r = t.RowCount++;
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.Controls.Add(c, 0, r);
            t.SetColumnSpan(c, 2);
        }

        /// <summary>Label, hint and owner on the left; the control and its effective value on the right.</summary>
        static void Row(TableLayoutPanel t, string label, string hint, string owner, Control control, Control under = null)
        {
            int r = t.RowCount++;
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var leftCol = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, BackColor = Color.Transparent, Margin = new Padding(0, Px(10), Px(12), Px(10)) };
            leftCol.Controls.Add(new Label { UseMnemonic = false, Text = label, Font = Ui.Bold, ForeColor = Ui.Ink, AutoSize = true, BackColor = Color.Transparent, Margin = new Padding(0) });
            if (!string.IsNullOrEmpty(hint))
                leftCol.Controls.Add(new Label { UseMnemonic = false, Text = hint, Font = Ui.Small, ForeColor = Ui.Muted, AutoSize = true, MaximumSize = new Size(Px(LabelW - 16), 0), BackColor = Color.Transparent, Margin = new Padding(0, Px(2), 0, 0) });
            if (owner != null) leftCol.Controls.Add(new Tag(owner) { Margin = new Padding(0, Px(6), 0, 0) });
            var right = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, BackColor = Color.Transparent, Margin = new Padding(0, Px(10), 0, Px(10)) };
            control.Margin = new Padding(0);
            right.Controls.Add(control);
            if (under != null) { under.Margin = new Padding(0, Px(6), 0, 0); right.Controls.Add(under); }
            t.Controls.Add(leftCol, 0, r);
            t.Controls.Add(right, 1, r);
        }

        static void Toggle(TableLayoutPanel t, Switch sw, string title, string desc, string owner, Control under = null)
        {
            var row = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, BackColor = Color.Transparent, Margin = new Padding(0, Px(8), 0, Px(8)) };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Px(52)));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            sw.Margin = new Padding(0, Px(2), 0, 0);
            sw.Anchor = AnchorStyles.Left | AnchorStyles.Top;
            var text = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, BackColor = Color.Transparent, Margin = new Padding(0) };
            var l1 = new Label { UseMnemonic = false, Text = title, Font = Ui.Bold, ForeColor = Ui.Ink, AutoSize = true, BackColor = Color.Transparent, Margin = new Padding(0), Cursor = Cursors.Hand };
            var l2 = new Label { UseMnemonic = false, Text = desc, Font = Ui.Small, ForeColor = Ui.Muted, AutoSize = true, MaximumSize = new Size(Px(640), 0), BackColor = Color.Transparent, Margin = new Padding(0, Px(2), 0, 0) };
            text.Controls.Add(l1); text.Controls.Add(l2);
            if (owner != null) text.Controls.Add(new Tag(owner) { Margin = new Padding(0, Px(6), 0, 0) });
            if (under != null) { under.Margin = new Padding(0, Px(6), 0, 0); text.Controls.Add(under); }
            l1.Click += (s, e) => { if (sw.Enabled) sw.Checked = !sw.Checked; };
            row.Controls.Add(sw, 0, 0);
            row.Controls.Add(text, 1, 0);
            Add(t, row);
        }

        static FlowLayoutPanel Line(params Control[] items)
        {
            var f = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, BackColor = Color.Transparent, Margin = new Padding(0) };
            foreach (var c in items)
            {
                if (c is Label) { c.Anchor = AnchorStyles.Left; c.Margin = new Padding(0, Px(10), Px(10), 0); }
                else c.Margin = new Padding(0, 0, Px(8), 0);
                f.Controls.Add(c);
            }
            return f;
        }

        static Label Mono(string text, Color? color = null)
        {
            return new Label { UseMnemonic = false, Text = text, Font = Ui.Mono, ForeColor = color ?? Ui.Ink, AutoSize = true, MaximumSize = new Size(Px(560), 0), BackColor = Color.Transparent };
        }

        static Label Note(string text, Color? color = null)
        {
            return new Label { UseMnemonic = false, Text = text, Font = Ui.Small, ForeColor = color ?? Ui.Muted, AutoSize = true, MaximumSize = new Size(Px(560), 0), BackColor = Color.Transparent };
        }

        IconButton Btn(string text, string icon, Action act, string tip = null)
        {
            var b = new IconButton(text, icon, ButtonKind.Outline) { Font = Ui.BtnSmall, IconColor = Ui.NavInk };
            b.SetMin(0, Px(36));
            b.Click += (s, e) => act();
            if (tip != null) _tips.SetToolTip(b, tip);
            return b;
        }

        static Callout Call(CalloutKind k, string text) { return new Callout(k, text, CalloutW) { Margin = new Padding(0, Px(6), 0, Px(8)) }; }

        /// <summary>Small neutral tag naming who owns a setting.</summary>
        class Tag : Painted
        {
            public Tag(string text)
            {
                Text = text; Font = Ui.Caps;
                var sz = TextRenderer.MeasureText(text, Font);
                Size = new Size(sz.Width + S(14), S(20));
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics; Smooth(g);
                using (var p = Ui.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), S(6)))
                using (var b = new SolidBrush(Ui.Surface)) g.FillPath(b, p);
                TextRenderer.DrawText(g, Text, Font, new Rectangle(0, 0, Width, Height), Ui.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
        }

        // ================================================================== edits and status

        void EditApache(string key, string value, string original)
        {
            if (_loading) return;
            if (string.Equals(value, original, StringComparison.OrdinalIgnoreCase)) _e.Apache.Remove(key); else _e.Apache[key] = value;
            OnChanged();
        }

        void EditModule(string name, bool on)
        {
            if (_loading) return;
            if (on == ModuleActiveNow(name)) _e.Modules.Remove(name); else _e.Modules[name] = on;
            OnChanged();
        }

        void EditMaria(string id, string value)
        {
            if (_loading) return;
            var s = MariaCatalog.Get(id);
            string orig = _ini.Get(s.Section, s.Key) ?? (s.Section == "mysqld" && _mi.NextStart != null && _mi.NextStart.Vars.ContainsKey(s.Key) ? _mi.NextStart.Vars[s.Key] : null);
            if (orig != null && MariaCatalog.Norm(s.Kind, orig) == MariaCatalog.Norm(s.Kind, value)) _e.Maria.Remove(id); else _e.Maria[id] = value;
            OnChanged();
        }

        bool ModuleActiveNow(string name)
        {
            var line = _hc.Modules().Where(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (line.Count > 0) return line.Any(m => m.Active);
            return _ai.Loaded.Contains(name);
        }

        bool ModuleActivePending(string name)
        {
            bool v;
            return _e.Modules.TryGetValue(name, out v) ? v : ModuleActiveNow(name);
        }

        void OnChanged()
        {
            if (_loading) return;
            foreach (var r in _refresh) r();
            if (_busy) return;
            if (_e.IsEmpty)
            {
                if (_status.ForeColor == Ui.Amber || _status.Text == "") { _status.Text = ""; }
                _save.Enabled = false;
                _cancel.Text = "Close";
                _cancel.Invalidate(); _save.Invalidate();
                return;
            }
            _details.Visible = false;
            _status.ForeColor = Ui.Amber;
            _status.Text = "●  Unsaved changes  ·  " + Effects();
            _save.Enabled = true;
            _cancel.Text = "Discard";
            _cancel.Invalidate(); _save.Invalidate();
        }

        /// <summary>What saving will do, in words.</summary>
        string Effects()
        {
            var list = new List<string>();
            bool apache = _e.Apache.Count > 0 || _e.Modules.Count > 0 || _e.ResetHttpd || _e.Php != null || _e.HttpPort != null || _e.Lan != null || _e.MysqlPort != null;
            if (apache) list.Add(Services.Apache.IsRunning ? "restarts Apache" : "used when Apache starts");
            if (_e.Maria.Count > 0 || _e.MysqlPort != null || _e.CommentOut.Count > 0)
            {
                bool running = Services.MariaDb.IsRunning;
                bool restart = _e.MysqlPort != null || _e.Maria.Keys.Any(id => { var s = MariaCatalog.Get(id); return s.Section == "mysqld" && _mi.Dynamic(s.Key) != true; });
                bool live = _e.Maria.Keys.Any(id => { var s = MariaCatalog.Get(id); return s.Section == "mysqld" && _mi.Dynamic(s.Key) == true; });
                if (!running) list.Add("used when MariaDB starts");
                else if (restart) list.Add("restarts MariaDB");
                else if (live) list.Add("applies to MariaDB immediately");
                else list.Add("saved to my.ini");
            }
            return string.Join(", ", list);
        }

        void SetBusy(bool on, string msg)
        {
            _busy = on;
            _bar.Visible = on;
            _bar.Value = -1;
            if (msg != null) { _status.Text = msg; _status.ForeColor = Ui.Muted; }
            _save.Enabled = !on && !_e.IsEmpty;
            _cancel.Enabled = !on;
            _svc.Enabled = !on;
            Cursor = on ? Cursors.AppStarting : Cursors.Default;
            _save.Invalidate(); _cancel.Invalidate();
        }

        void Discard()
        {
            if (MessageBox.Show(this, "Discard your unsaved changes?", "Server configuration", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            _status.Text = "";
            Reload(null);
        }

        // ================================================================== save

        void Save()
        {
            if (_busy || _e.IsEmpty) return;
            ConfigPlan plan;
            try { plan = ConfigManager.Plan(_e, Cfg, _mi, _ai); }
            catch (ConfigException ex)
            {
                _status.ForeColor = Ui.Red;
                _status.Text = "Fix " + (ex.Problems.Count == 1 ? "this" : "these") + " first: " + string.Join("  ·  ", ex.Problems);
                return;
            }
            if (plan.MariaRestart && Services.MariaDb.IsRunning && plan.MyIniText != null &&
                _e.Maria.Keys.Any(k => k.Contains("innodb") || k.Contains("aria")) &&
                MessageBox.Show(this, "These InnoDB/Aria changes restart MariaDB. WebHut backs up your databases before stopping MariaDB (if automatic backups are on and the last one is older than 15 minutes), and puts the previous configuration back if MariaDB doesn't start.\n\nContinue?",
                    "Restart MariaDB", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;

            SetBusy(true, "Validating…");
            _details.Visible = false;
            var cfg = Cfg; var mi = _mi;
            Task.Run(() => ConfigManager.Apply(plan, cfg, mi, m => { try { BeginInvoke(new Action(() => { _status.Text = m; })); } catch { } }))
                .ContinueWith(t =>
                {
                    if (IsDisposed) return;
                    SetBusy(false, null);
                    ApplyResult r = t.IsFaulted ? new ApplyResult { Outcome = ApplyOutcome.Failed, Message = "Failed: " + t.Exception.GetBaseException().Message, Details = t.Exception.ToString() } : t.Result;
                    _lastDetails = (r.Details ?? "") + (r.Notes.Count > 0 ? (r.Details.Length > 0 ? "\n\n" : "") + "Notes:\n" + string.Join("\n", r.Notes) : "") +
                                   (r.BackupDir != null ? "\n\nBackup of the previous files: " + r.BackupDir : "");
                    if (r.Ok)
                    {
                        Changed = true;
                        Toast.Show(this, r.Outcome == ApplyOutcome.AppliedLive ? "Applied immediately." : r.Outcome == ApplyOutcome.Applied ? "Applied successfully." : "Saved.", ToastKind.Success);
                        Reload(() => ShowResult(r));
                    }
                    else ShowResult(r);
                }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void ShowResult(ApplyResult r)
        {
            _status.ForeColor = r.Ok ? Ui.Green : Ui.Red;
            _status.Text = (r.Ok ? "✓  " : "✕  ") + r.Message + (r.Notes.Count > 0 && r.Ok ? "  " + r.Notes[0] : "");
            _details.Visible = _lastDetails.Trim().Length > 0;
            _details.Text = r.Ok ? "Details" : "Show technical details";
            if (!r.Ok) _save.Enabled = !_e.IsEmpty;
        }

        // ================================================================== shared helpers

        string ListenText(bool lan, int port)
        {
            return lan ? "0.0.0.0:" + port + "  (all addresses; sites accept private-network clients only)" : "127.0.0.1:" + port + "  (this PC only)";
        }

        Control PortField(int current, int min, Action<int> changed, Func<int, string> problem, Label warn)
        {
            var f = new NumberField(min, 65535, Px(120)) { Value = current };
            f.ValueChanged += (s, e) =>
            {
                if (!f.IsValid) { warn.Text = "Enter a number from " + min + " to 65535."; warn.ForeColor = Ui.Red; return; }
                string p = problem(f.Value);
                warn.Text = p ?? "";
                warn.ForeColor = p == null ? Ui.Muted : Ui.Red;
                changed(f.Value);
            };
            return f;
        }

        string PortProblem(int port, int current, bool apache)
        {
            if (port == current) return null;
            if (port == (apache ? (_e.MysqlPort ?? Cfg.MysqlPort) : (_e.HttpPort ?? Cfg.HttpPort))) return "Already used by " + (apache ? "MariaDB" : "Apache") + ".";
            var app = NodeApp.LoadAll().FirstOrDefault(a => a.Port == port);
            if (app != null) return "Used by the Node app '" + app.Name + "'.";
            if (Sys.IsPortListening(port)) return "In use by " + Sys.PortOwner(port) + ".";
            return null;
        }

        static string Shorten(string path) { return path.StartsWith(K.Root) ? "<WebHut>" + path.Substring(K.Root.Length) : path; }

        // ================================================================== Apache pages

        Control ApGeneral()
        {
            var page = Page("Apache · General", "Apache serves your sites. Settings here are stored in WebHut's settings or in config\\apache\\httpd.conf.");
            var s = Section(page, "globe", "Listening", null);
            var portNote = Note("");
            Action portText = () => { if (portNote.ForeColor != Ui.Red) portNote.Text = "Effective: " + ListenText(_e.Lan ?? Cfg.LanAccess, _e.HttpPort ?? Cfg.HttpPort) + (Services.Apache.IsRunning ? " · running" : " · stopped"); };
            Row(s, "HTTP port", "Default 80. The same setting as Settings › Services.", "WebHut setting",
                PortField(Cfg.HttpPort, 1, v => { _e.HttpPort = v == Cfg.HttpPort ? (int?)null : v; OnChanged(); }, v => PortProblem(v, Cfg.HttpPort, true), portNote), portNote);
            _refresh.Add(portText); portText();

            var host = new TextField(Px(220), true) { Value = _hc.ServerHost ?? "localhost" };
            host.Validate = v => System.Text.RegularExpressions.Regex.IsMatch(v, @"^[A-Za-z0-9]([A-Za-z0-9.-]*[A-Za-z0-9])?$");
            host.ValueChanged += (x, e) => { if (host.IsValid) EditApache("ServerHost", host.Value, _hc.ServerHost ?? "localhost"); };
            string ov;
            Row(s, "Server name", "Apache's own name, used in redirects and error pages. The port part is kept.", "httpd.conf",
                host, _overrides.TryGetValue("ServerName", out ov) ? (Control)Note("Overridden by " + ov, Ui.Amber) : null);

            var s2 = Section(page, "folder", "Content", null);
            Row(s2, "Document root", "Fixed: generated sites, the dashboard and phpMyAdmin rely on it. Put sites in folders inside it.", "httpd.conf · read-only",
                Line(Mono("${WEBHUT_ROOT}/www"), Btn("Open folder", "folder", () => Sys.OpenFolder(K.Www))), Mono(K.Www, Ui.Muted));
            var listing = new Switch { Checked = _hc.Indexes ?? true };
            listing.Enabled = _hc.Indexes != null;
            listing.CheckedChanged += (x, e) => EditApache("Indexes", listing.Checked ? "On" : "Off", (_hc.Indexes ?? true) ? "On" : "Off");
            Toggle(s2, listing, "Directory listing", "Show a file list for folders without an index file. Applies to www\\ and every generated site." +
                   (_hc.Indexes == null ? " (The Options line for www wasn't found in httpd.conf, so this can't be changed here.)" : ""), "httpd.conf");
            var mode = new Label { UseMnemonic = false, Text = _hc.Fcgid ? "mod_fcgid (FastCGI)" : "mod_php (inside Apache)", Font = Ui.Base, ForeColor = Ui.Ink, AutoSize = true };
            var change = new LinkLabel { Text = "Change in PHP Integration", Font = Ui.Small, AutoSize = true, LinkColor = Ui.AccentInk, LinkBehavior = LinkBehavior.HoverUnderline };
            change.LinkClicked += (x, e) => Go("apache", "PHP Integration");
            Row(s2, "PHP execution mode", null, "httpd.conf", mode, change);
            _refresh.Add(() => { string f; mode.Text = (_e.Apache.TryGetValue("Fcgid", out f) ? f == "On" : _hc.Fcgid) ? "mod_fcgid (FastCGI)" : "mod_php (inside Apache)"; });

            var s3 = Section(page, "file", "Configuration file", "WebHut's httpd.conf contains no absolute paths, so the WebHut folder can be moved.");
            Row(s3, "httpd.conf", null, null, Line(Btn("Open", "file", () => Sys.OpenInEditor(K.HttpdConf)), Btn("Open folder", "folder", () => Sys.OpenFolder(K.ApacheConf))), Mono(K.HttpdConf, Ui.Muted));
            return page;
        }

        Control ApPhp()
        {
            var page = Page("Apache · PHP Integration", "Which PHP Apache runs, and how.");
            var s = Section(page, "cube", "PHP version", null);
            var php = new Dropdown(Px(200));
            php.Items.AddRange(WebHutConfig.InstalledPhp());
            php.SelectedItem = Cfg.Php;
            php.SelectedIndexChanged += (x, e) => { _e.Php = php.SelectedItem == Cfg.Php ? null : php.SelectedItem; OnChanged(); };
            var dirLabel = Mono(Cfg.PhpDir, Ui.Muted);
            _refresh.Add(() => dirLabel.Text = K.PhpDir(_e.Php ?? Cfg.Php));
            Row(s, "Active PHP version", "Apache and the Shell use it. The same setting as Settings › Runtime.", "WebHut runtime manager",
                Line(php, Btn("Install another…", "plus", () => { using (var f = new SettingsForm()) f.ShowDialog(this); php.Items.Clear(); php.Items.AddRange(WebHutConfig.InstalledPhp()); php.SelectedItem = _e.Php ?? Cfg.Php; })), dirLabel);
            Row(s, "PHP folder", null, null, Line(Btn("Open folder", "folder", () => Sys.OpenFolder(K.PhpDir(_e.Php ?? Cfg.Php))), Btn("Edit php.ini", "file", () => Sys.OpenInEditor(Path.Combine(K.PhpDir(_e.Php ?? Cfg.Php), "php.ini")))));
            var info = Btn("Open phpinfo()", "external", () => Sys.OpenUrl(Services.Apache.Url + "?phpinfo=1"));
            info.Enabled = Services.Apache.IsRunning;
            Row(s, "PHP information", Services.Apache.IsRunning ? "phpinfo() as Apache runs it, from the dashboard." : "Start Apache to see phpinfo() as Apache runs it.", null, info);

            var s2 = Section(page, "bolt", "Execution mode", null);
            var mode = new Dropdown(Px(330));
            mode.Items.Add("mod_php — PHP inside Apache (default)");
            mode.Items.Add("mod_fcgid — FastCGI, per-site PHP versions");
            mode.SelectedIndex = _hc.Fcgid ? 1 : 0;
            bool fcgidOk = _ai.FcgidModule && _ai.PhpCgi;
            mode.Enabled = fcgidOk || _hc.Fcgid;
            mode.SelectedIndexChanged += (x, e) => EditApache("Fcgid", mode.SelectedIndex == 1 ? "On" : "Off", _hc.Fcgid ? "On" : "Off");
            Row(s2, "PHP mode", "mod_php is fastest for one PHP version. mod_fcgid lets custom vhosts pick their own PHP (docs/05-php-versions.md).", "httpd.conf (WEBHUT_PHP_FCGID)", mode,
                fcgidOk ? null : (Control)Note("mod_fcgid isn't available: " + (!_ai.FcgidModule ? "apache\\modules\\mod_fcgid.so is missing (get it from Apache Lounge). " : "") + (!_ai.PhpCgi ? "php-cgi.exe is missing in the PHP folder." : ""), Ui.Amber));

            var s3 = Section(page, "terminal", "Runtime definitions", "WebHut passes these to Apache at every start; httpd.conf and your vhosts use them instead of absolute paths.");
            var defs = new StringBuilder();
            defs.AppendLine("WEBHUT_ROOT       = " + K.RootFwd);
            defs.AppendLine("PHP_DIR           = " + K.Fwd(Cfg.PhpDir));
            defs.AppendLine("WEBHUT_HTTP_PORT  = " + Cfg.HttpPort);
            defs.Append("WEBHUT_MYSQL_PORT = " + Cfg.MysqlPort);
            if (Cfg.LanAccess) defs.Append("\nWEBHUT_LAN        = 1");
            Add(s3, Mono(defs.ToString()));
            Add(s3, Line(Btn("Copy startup command", "copy", () => CopyText("httpd.exe " + Services.Apache.Args()))));
            return page;
        }

        Control ApPerformance()
        {
            var page = Page("Apache · Performance", "Connection handling. The defaults suit a development PC.");
            bool winnt = _ai.MpmIsWinnt || !_ai.Installed || _ai.Mpm == "";
            var s = Section(page, "bolt", "Worker threads", "Apache on Windows uses one process with many threads (MPM " + (_ai.Mpm == "" ? "winnt" : _ai.Mpm) + ").");
            var tpc = new NumberField(1, 1920, Px(120)) { Value = Int(_hc.Mpm("ThreadsPerChild"), 150), Enabled = winnt };
            tpc.ValueChanged += (x, e) => { if (tpc.IsValid) EditApache("ThreadsPerChild", tpc.Value.ToString(), _hc.Mpm("ThreadsPerChild") ?? "150"); };
            Row(s, "ThreadsPerChild", "Requests Apache can serve at once. 150 is plenty for development.", "httpd.conf", tpc,
                winnt ? null : Note("Not used by this Apache build's MPM (" + _ai.Mpm + ").", Ui.Amber));
            var mcpc = new NumberField(0, 10000000, Px(120)) { Value = Int(_hc.Mpm("MaxConnectionsPerChild"), 0), Enabled = winnt };
            mcpc.ValueChanged += (x, e) => { if (mcpc.IsValid) EditApache("MaxConnectionsPerChild", mcpc.Value.ToString(), _hc.Mpm("MaxConnectionsPerChild") ?? "0"); };
            Row(s, "MaxConnectionsPerChild", "Recycle the worker after this many connections (0 = never). Helps with memory leaks in extensions.", "httpd.conf", mcpc);

            var s2 = Section(page, "pulse", "Keep-alive", "Lets browsers reuse a connection for several requests.");
            string ov;
            bool kaLocked = _overrides.ContainsKey("KeepAlive");
            var ka = new Switch { Checked = !string.Equals(_hc.TopArgs("KeepAlive") ?? "On", "Off", StringComparison.OrdinalIgnoreCase), Enabled = !kaLocked };
            ka.CheckedChanged += (x, e) => EditApache("KeepAlive", ka.Checked ? "On" : "Off", _hc.TopArgs("KeepAlive") ?? "On");
            Toggle(s2, ka, "KeepAlive", _hc.TopArgs("KeepAlive") == null ? "Not set in httpd.conf: Apache's default (On) applies." : "Set in httpd.conf.", "httpd.conf",
                kaLocked ? Note("Overridden by " + _overrides["KeepAlive"] + ". Edit that file instead.", Ui.Amber) : null);
            var kat = new NumberField(1, 300, Px(120)) { Value = Int(_hc.TopArgs("KeepAliveTimeout"), 5), Enabled = !_overrides.ContainsKey("KeepAliveTimeout") };
            kat.ValueChanged += (x, e) => { if (kat.IsValid) EditApache("KeepAliveTimeout", kat.Value.ToString(), _hc.TopArgs("KeepAliveTimeout") ?? "5"); };
            Row(s2, "KeepAliveTimeout", "Seconds an idle connection stays open (Apache default 5).", "httpd.conf", kat,
                _overrides.TryGetValue("KeepAliveTimeout", out ov) ? Note("Overridden by " + ov, Ui.Amber) : null);
            var mkr = new NumberField(0, 100000, Px(120)) { Value = Int(_hc.TopArgs("MaxKeepAliveRequests"), 100), Enabled = !_overrides.ContainsKey("MaxKeepAliveRequests") };
            mkr.ValueChanged += (x, e) => { if (mkr.IsValid) EditApache("MaxKeepAliveRequests", mkr.Value.ToString(), _hc.TopArgs("MaxKeepAliveRequests") ?? "100"); };
            Row(s2, "MaxKeepAliveRequests", "Requests per connection (0 = unlimited; Apache default 100).", "httpd.conf", mkr,
                _overrides.TryGetValue("MaxKeepAliveRequests", out ov) ? Note("Overridden by " + ov, Ui.Amber) : null);

            var s3 = Section(page, "cube", "Compression", null);
            bool deflateAvailable = _ai.Available.Count == 0 || _ai.Available.Contains("deflate_module");
            var gz = new Switch { Checked = ModuleActivePending("deflate_module"), Enabled = deflateAvailable };
            gz.CheckedChanged += (x, e) => EditModule("deflate_module", gz.Checked);
            _refresh.Add(() => { bool want = ModuleActivePending("deflate_module"); if (gz.Checked != want) gz.Checked = want; });
            Toggle(s3, gz, "Compress responses (gzip)", "HTML, CSS, JavaScript, JSON and SVG are compressed with mod_deflate. The same switch as deflate_module in Modules.", "httpd.conf (LoadModule)");
            return page;
        }

        static int Int(string s, int dflt) { int v; return int.TryParse((s ?? "").Trim(), out v) ? v : dflt; }

        // ---------------------------------------------------------------- modules

        static readonly Dictionary<string, string> ModuleInfo = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            {"alias_module","URL aliases (phpMyAdmin's /phpmyadmin)"}, {"rewrite_module","URL rewriting for .htaccess (WordPress, Laravel)"},
            {"headers_module","Set or change HTTP headers"}, {"deflate_module","Gzip compression"}, {"expires_module","Cache-Control / Expires headers"},
            {"proxy_module","Reverse proxy core"}, {"proxy_http_module","Proxy to HTTP backends (Node apps)"}, {"proxy_wstunnel_module","WebSocket proxying (older style)"},
            {"proxy_fcgi_module","Proxy to FastCGI (PHP-FPM)"}, {"ssl_module","HTTPS"}, {"socache_shmcb_module","Shared cache (needed by HTTPS)"},
            {"http2_module","HTTP/2"}, {"fcgid_module","PHP through FastCGI"}, {"autoindex_module","Directory listings"}, {"dir_module","Index files (index.php)"},
            {"mime_module","Content types by extension"}, {"env_module","SetEnv for PHP"}, {"setenvif_module","Environment variables from request data"},
            {"log_config_module","Access log format"}, {"authz_core_module","Access control core"}, {"authz_host_module","Require local / Require ip"},
            {"authz_user_module","Require user"}, {"authn_core_module","Authentication core"}, {"authn_file_module","Password files (.htpasswd)"},
            {"auth_basic_module","Basic authentication"}, {"auth_digest_module","Digest authentication"}, {"filter_module","Output filter chains"},
            {"cache_module","HTTP caching"}, {"cache_disk_module","Disk cache"}, {"include_module","Server-side includes"}, {"info_module","/server-info page"},
            {"status_module","/server-status page"}, {"cgi_module","CGI scripts"}, {"actions_module","Run scripts by handler"}, {"vhost_alias_module","Mass virtual hosting"},
            {"speling_module","Fix mistyped URLs"}, {"unique_id_module","Unique request IDs"}, {"remoteip_module","Client IP behind proxies"},
            {"ratelimit_module","Bandwidth limiting"}, {"reqtimeout_module","Request timeouts"}, {"substitute_module","Find and replace in responses"},
            {"userdir_module","~user folders"}, {"version_module","<IfVersion>"}, {"watchdog_module","Background tasks for other modules"},
            {"slotmem_shm_module","Shared memory slots"}, {"brotli_module","Brotli compression"}, {"md_module","Let's Encrypt certificates"},
            {"lua_module","Lua scripting"}, {"dav_module","WebDAV"}, {"dav_fs_module","WebDAV file storage"}, {"negotiation_module","Content negotiation"},
            {"session_module","Sessions"}, {"xml2enc_module","Character sets for filters"}, {"proxy_html_module","Rewrite links in proxied HTML"},
            {"proxy_balancer_module","Load balancing"}, {"lbmethod_byrequests_module","Balancer: by requests"}, {"logio_module","Bytes in/out in logs"},
            {"allowmethods_module","Restrict HTTP methods"}, {"authz_groupfile_module","Group files"}, {"request_module","Request body helpers"},
            {"buffer_module","Response buffering"}, {"data_module","data: URLs"}, {"dumpio_module","Debug: dump I/O"}, {"macro_module","<Macro> blocks"},
            {"sed_module","sed on responses"}, {"mime_magic_module","Content types by file contents"}, {"usertrack_module","User tracking cookies"},
            {"cern_meta_module","CERN metafiles"}, {"asis_module","Send files as-is"}, {"imagemap_module","Server-side image maps"},
            {"php_module","PHP (loaded by php-mod.conf)"},
        };

        Control ApModules()
        {
            var page = Page("Apache · Modules", "Modules in apache\\modules and whether httpd.conf loads them. \"Loaded\" is what the running configuration actually has (httpd -M).");
            var s = Section(page, "server", "Modules", null);
            if (_ai.LoadedError != null)
                Add(s, Call(CalloutKind.Warning, "Couldn't list the loaded modules (httpd -M failed), so states come from httpd.conf only:\n" + _ai.LoadedError.Split('\n')[0]));
            var search = new TextField(Px(260), false);
            var filter = new Segmented("All", "Loaded", "Not loaded");
            var list = new ModuleList(this);
            Add(s, Line(new Label { UseMnemonic = false, Text = "Search", Font = Ui.Bold, ForeColor = Ui.Ink, AutoSize = true }, search, filter));
            var counts = Note("");
            Add(s, counts);
            Add(s, list);
            Action apply = () =>
            {
                string q = search.Value.ToLowerInvariant();
                list.SetRows(AllModules().Where(m => (q.Length == 0 || m.Name.Contains(q) || (m.Desc ?? "").ToLowerInvariant().Contains(q)) &&
                                                      (filter.SelectedIndex == 0 || (filter.SelectedIndex == 1) == m.On)).ToList());
                int loaded = AllModules().Count(m => m.On);
                counts.Text = loaded + " loaded · " + (AllModules().Count - loaded) + " available but not loaded · required modules are locked";
            };
            search.ValueChanged += (x, e) => apply();
            filter.SelectedChanged += (x, e) => apply();
            _refresh.Add(() => list.Invalidate());
            apply();
            return page;
        }

        class ModRow { public string Name, Desc, State; public bool On, Locked; }

        List<ModRow> AllModules()
        {
            var names = new SortedSet<string>(_ai.Available, StringComparer.OrdinalIgnoreCase);
            foreach (var m in _hc.Modules()) names.Add(m.Name);
            foreach (var m in _ai.Loaded) names.Add(m);
            var required = ConfigManager.RequiredNow();
            var inConf = _hc.Modules();
            var rows = new List<ModRow>();
            foreach (var n in names)
            {
                bool static_ = _ai.Loaded.Contains(n) && !_ai.Available.Contains(n) && !inConf.Any(m => m.Name == n);
                if (n.StartsWith("mpm_") || n == "core_module" || n == "so_module" || n == "http_module" || n == "win32_module") continue;
                var r = new ModRow { Name = n, On = ModuleActivePending(n) };
                string desc; ModuleInfo.TryGetValue(n, out desc);
                string[] deps;
                if (ConfigManager.ModuleDeps.TryGetValue(n, out deps)) desc = (desc == null ? "" : desc + " · ") + "needs " + string.Join(", ", deps);
                r.Desc = desc;
                bool lineHere = inConf.Any(m => m.Name.Equals(n, StringComparison.OrdinalIgnoreCase));
                bool loadedNow = _ai.Loaded.Contains(n);
                if (required.Contains(n)) { r.Locked = true; r.State = "Required"; }
                else if (static_ || (loadedNow && !lineHere)) { r.Locked = true; r.State = n == "php_module" ? "Loaded by php-mod.conf" : static_ ? "Built in" : "Loaded by another .conf"; }
                else
                {
                    bool pending = _e.Modules.ContainsKey(n);
                    r.State = pending ? (r.On ? "Loads after saving" : "Unloads after saving") : loadedNow ? "Loaded" : lineHere && inConf.First(m => m.Name == n).Active ? "In httpd.conf" : "Not loaded";
                }
                rows.Add(r);
            }
            return rows;
        }

        /// <summary>Owner-drawn module list: a switch, name, description and state per row. Fast with a hundred modules.</summary>
        class ModuleList : Control
        {
            readonly ConfigForm _f;
            List<ModRow> _rows = new List<ModRow>();
            int _hot = -1;
            static int RowH { get { return Px(48); } }
            public ModuleList(ConfigForm f)
            {
                _f = f;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
                Width = Px(680);
                Margin = new Padding(0, Px(6), 0, Px(8));
                Cursor = Cursors.Hand;
            }
            public void SetRows(List<ModRow> rows) { _rows = rows; Height = Math.Max(Px(48), rows.Count * RowH); Invalidate(); }
            protected override void OnParentChanged(EventArgs e)
            {
                base.OnParentChanged(e);
                if (Parent != null) Parent.SizeChanged += (s, a) => { Width = Math.Max(Px(480), Parent.ClientSize.Width - Parent.Padding.Horizontal - Px(4)); };
            }
            protected override void OnMouseMove(MouseEventArgs e) { int h = e.Y / RowH; if (h != _hot) { _hot = h; Invalidate(); } base.OnMouseMove(e); }
            protected override void OnMouseLeave(EventArgs e) { _hot = -1; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnMouseClick(MouseEventArgs e)
            {
                int i = e.Y / RowH;
                if (i < 0 || i >= _rows.Count || _rows[i].Locked) return;
                var r = _rows[i];
                _f.EditModule(r.Name, !_f.ModuleActivePending(r.Name));
                var fresh = _f.AllModules().FirstOrDefault(x => x.Name == r.Name);
                if (fresh != null) _rows[i] = fresh;
                Invalidate();
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Ui.Card);
                if (_rows.Count == 0)
                {
                    TextRenderer.DrawText(g, "No modules match.", Ui.Small, new Rectangle(0, 0, Width, RowH), Ui.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
                    return;
                }
                for (int i = 0; i < _rows.Count; i++)
                {
                    var r = _rows[i];
                    int y = i * RowH;
                    if (y > e.ClipRectangle.Bottom || y + RowH < e.ClipRectangle.Top) continue;
                    if (i == _hot && !r.Locked) using (var b = new SolidBrush(Ui.Hover)) g.FillRectangle(b, 0, y, Width, RowH);
                    if (i > 0) using (var pen = new Pen(Ui.Line)) g.DrawLine(pen, 0, y, Width, y);
                    // switch
                    float sw = Px(36), sh = Px(20), sx = Px(4), sy = y + (RowH - sh) / 2f;
                    using (var p = Ui.Rounded(new RectangleF(sx, sy, sw, sh), sh / 2))
                    using (var b = new SolidBrush(r.Locked ? Ui.Mix(r.On ? Ui.Accent : Ui.Grey, Ui.Card, 0.45f) : r.On ? Ui.Accent : Ui.Grey)) g.FillPath(b, p);
                    float d = sh - 5;
                    using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, r.On ? sx + sw - 2.5f - d : sx + 2.5f, sy + 2.5f, d, d);
                    int tx = Px(56);
                    TextRenderer.DrawText(g, r.Name, Ui.Mono, new Point(tx, y + Px(7)), Ui.Ink, TextFormatFlags.NoPrefix);
                    if (!string.IsNullOrEmpty(r.Desc))
                        TextRenderer.DrawText(g, r.Desc, Ui.Small, new Rectangle(tx, y + Px(26), Width - tx - Px(190), Px(18)), Ui.Muted, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                    Color c = r.State.EndsWith("after saving") ? Ui.Amber : r.State == "Loaded" || r.State == "Required" || r.State.StartsWith("Loaded by") || r.State == "Built in" ? Ui.Green : Ui.Muted;
                    TextRenderer.DrawText(g, r.State, Ui.SmallBold, new Rectangle(Width - Px(180), y, Px(176), RowH), c, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                }
            }
        }

        Control ApSecurity()
        {
            var page = Page("Apache · Security", "Who can reach your sites, and which files are never served.");
            var s = Section(page, "globe", "Network access", null);
            var lan = new Switch { Checked = Cfg.LanAccess };
            var addr = Mono("");
            var warn = Call(CalloutKind.Warning, "Your sites become visible to other devices on this network (private addresses only). Use this only on networks you trust.");
            Action upd = () =>
            {
                bool on = _e.Lan ?? Cfg.LanAccess;
                int port = _e.HttpPort ?? Cfg.HttpPort;
                addr.Text = "Listening on " + ListenText(on, port);
                warn.Visible = on;
            };
            lan.CheckedChanged += (x, e) => { _e.Lan = lan.Checked == Cfg.LanAccess ? (bool?)null : lan.Checked; OnChanged(); };
            Toggle(s, lan, "Allow access from other devices", "Phones and PCs on your Wi-Fi can open your sites at this PC's address. The same setting as Settings › Security.", "WebHut setting", addr);
            Add(s, warn);
            Add(s, Call(CalloutKind.Info, "phpMyAdmin and MariaDB always stay private to this PC: turning this on never exposes them."));
            _refresh.Add(upd); upd();

            var s2 = Section(page, "folder", "Folders and files", null);
            var listing = Note("");
            _refresh.Add(() => { string v; bool on = _e.Apache.TryGetValue("Indexes", out v) ? v == "On" : _hc.Indexes ?? true; listing.Text = "Directory listing is " + (on ? "on" : "off") + " — change it in General."; });
            Row(s2, "Directory listing", null, "httpd.conf", listing);
            var ht = new Switch { Checked = _hc.HtaccessAllowed ?? true, Enabled = _hc.HtaccessAllowed != null };
            ht.CheckedChanged += (x, e) => EditApache("Htaccess", ht.Checked ? "On" : "Off", (_hc.HtaccessAllowed ?? true) ? "On" : "Off");
            Toggle(s2, ht, "Allow .htaccess overrides", "AllowOverride All for www\\ and generated sites. WordPress, Laravel and most PHP frameworks need it.", "httpd.conf");
            Row(s2, "Access rule", null, "httpd.conf", Mono(Cfg.LanAccess ? "Require ip 127.0.0.0/8 ::1 10.0.0.0/8 172.16.0.0/12 192.168.0.0/16 …" : "Require local"));
            Row(s2, ".ht* files", ".htaccess and .htpasswd are never served.", "httpd.conf", Note(_hc.HtFilesDenied ? "Blocked ✓" : "Not blocked — the <Files \".ht*\"> rule is missing from httpd.conf", _hc.HtFilesDenied ? Ui.Green : Ui.Red));
            var dot = new Switch { Checked = _hc.ProtectDotfiles };
            dot.CheckedChanged += (x, e) => EditApache("ProtectDotfiles", dot.Checked ? "On" : "Off", _hc.ProtectDotfiles ? "On" : "Off");
            Toggle(s2, dot, "Block .git, .env and other dotfiles", "Refuse requests for version-control folders and secrets files in any site. Recommended, especially with network access on.", "httpd.conf (WebHut block)");

            var s3 = Section(page, "key", "Virtual host check", "Access rules in generated and custom virtual hosts.");
            var findings = _vhosts.SelectMany(v => v.Findings).ToList();
            if (findings.Count == 0) Add(s3, Note("No problems found in " + _vhosts.Count + " file(s).", Ui.Green));
            foreach (var f in findings) Add(s3, Call(f.Warning ? CalloutKind.Warning : CalloutKind.Info, f.File + " — " + f.Text));
            return page;
        }

        Control ApLogging()
        {
            var page = Page("Apache · Logging", "Where Apache writes its logs, and how much.");
            var s = Section(page, "logs", "Log files", "Paths are fixed in httpd.conf under ${WEBHUT_ROOT}/logs.");
            string err = Path.Combine(K.Logs, "apache-error.log"), acc = Path.Combine(K.Logs, "apache-access.log");
            Row(s, "Error log", null, "httpd.conf · read-only", Line(Btn("Open", "file", () => Sys.OpenInEditor(err)), Btn("Open folder", "folder", () => Sys.OpenFolder(K.Logs))), Mono(err, Ui.Muted));
            Row(s, "Access log", null, "httpd.conf · read-only", Line(Btn("Open", "file", () => Sys.OpenInEditor(acc))), Mono(acc, Ui.Muted));
            var lvl = new Dropdown(Px(160));
            lvl.Items.AddRange(ConfigManager.LogLevels);
            string cur = (_hc.TopArgs("LogLevel") ?? "warn").Split(' ')[0].ToLowerInvariant();
            lvl.SelectedItem = cur;
            lvl.Enabled = !_overrides.ContainsKey("LogLevel");
            lvl.SelectedIndexChanged += (x, e) => EditApache("LogLevel", lvl.SelectedItem, cur);
            string ov;
            Row(s, "Log level", "warn is a good default; info and debug are noisy.", "httpd.conf", lvl, _overrides.TryGetValue("LogLevel", out ov) ? Note("Overridden by " + ov, Ui.Amber) : null);
            var s2 = Section(page, "pulse", "Live view", "The newest lines; refreshed every 2 seconds while Auto is on.");
            Add(s2, new LogView(this, new[] { "Error log", "Access log" }, i => i == 0 ? err : acc, true));
            return page;
        }

        Control ApVhosts()
        {
            var page = Page("Apache · Virtual Hosts", "Sites Apache answers for. Generated files belong to WebHut; files in custom\\ belong to you and are never overwritten.");
            var s = Section(page, "globe", "Virtual host files", null);
            if (_vhosts.Count == 0) Add(s, Note("No virtual host files yet. Start Apache once to generate them."));
            foreach (var v in _vhosts)
            {
                var head = Line(new Label { UseMnemonic = false, Text = v.Name, Font = Ui.Bold, ForeColor = Ui.Ink, AutoSize = true }, new Tag(v.Generated ? "Generated · WebHut" : "Custom · yours"));
                var vv = v;
                var btn = Btn(v.Generated ? "View" : "Edit", "file", () => Sys.OpenInEditor(vv.Path), v.Generated ? "Generated files are rewritten on every start; copy a block into custom\\ to change it." : null);
                Row(s, v.Generated ? "Managed" : "Yours", v.Generated ? "Regenerated from www\\ and node-apps.json; don't edit." : "Included after the generated hosts.", null,
                    Line(head, btn), Mono(v.Hosts.Count == 0 ? "(no ServerName)" : string.Join("  ", v.Hosts.Take(12)) + (v.Hosts.Count > 12 ? "  …" : ""), Ui.Muted));
                foreach (var f in v.Findings) Add(s, Call(f.Warning ? CalloutKind.Warning : CalloutKind.Info, f.Text));
            }
            var s2 = Section(page, "plus", "Add your own", "Creating custom\\<name>.conf for a folder in www\\ also stops WebHut generating that folder's host.");
            Add(s2, Line(Btn("New custom vhost…", "plus", NewVhost), Btn("Open custom folder", "folder", () => { Directory.CreateDirectory(Path.Combine(K.ApacheConf, "custom")); Sys.OpenFolder(Path.Combine(K.ApacheConf, "custom")); })));
            return page;
        }

        void NewVhost()
        {
            var name = Ui.Prompt(this, "New custom virtual host", "Name (letters, digits, hyphens), e.g. shop → shop.localhost:");
            if (name == null) return;
            name = name.Trim().ToLowerInvariant();
            if (!System.Text.RegularExpressions.Regex.IsMatch(name, "^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?$")) { Ui.Error(this, "Virtual host", "Use letters, digits and hyphens only."); return; }
            string file = Path.Combine(K.ApacheConf, "custom", name + ".conf");
            if (File.Exists(file)) { Sys.OpenInEditor(file); return; }
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            string doc = Directory.Exists(Path.Combine(K.Www, name)) ? "${WEBHUT_ROOT}/www/" + name : "${WEBHUT_ROOT}/www";
            File.WriteAllText(file, ("# Custom virtual host - yours; WebHut never overwrites this file.\n<VirtualHost *:${WEBHUT_HTTP_PORT}>\n    ServerName " + name + ".localhost\n    DocumentRoot \"" + doc + "\"\n" +
                "    <Directory \"" + doc + "\">\n        Options FollowSymLinks\n        AllowOverride All\n        <IfDefine WEBHUT_LAN>\n            Require ip 127.0.0.0/8 ::1 10.0.0.0/8 172.16.0.0/12 192.168.0.0/16\n        </IfDefine>\n" +
                "        <IfDefine !WEBHUT_LAN>\n            Require local\n        </IfDefine>\n    </Directory>\n</VirtualHost>\n").Replace("\n", "\r\n"));
            Log.Info("Created " + file + ". Restart Apache to use it.");
            Sys.OpenInEditor(file);
            Toast.Show(this, "Created custom\\" + name + ".conf. Restart Apache to use it.", ToastKind.Info);
            Reload(null);
        }

        Control ApAdvanced()
        {
            var page = Page("Apache · Advanced", "Direct access to the configuration, validation and recovery.");
            var s = Section(page, "terminal", "Files", null);
            Add(s, Line(Btn("Open httpd.conf", "file", () => Sys.OpenInEditor(K.HttpdConf)), Btn("Configuration folder", "folder", () => Sys.OpenFolder(K.ApacheConf)),
                        Btn("Custom files", "folder", () => { Directory.CreateDirectory(Path.Combine(K.ApacheConf, "custom")); Sys.OpenFolder(Path.Combine(K.ApacheConf, "custom")); })));
            var s2 = Section(page, "check", "Validate", "Runs the same httpd.exe, ServerRoot, httpd.conf, definitions and environment as a normal start, with -t.");
            var result = Note("");
            Add(s2, Line(Btn("Run syntax test", "play", () =>
            {
                result.Text = "Testing…"; result.ForeColor = Ui.Muted;
                Task.Run(() => File.Exists(K.Httpd) ? Sys.Run(K.Httpd, Services.Apache.Args() + " -t", Services.Env(), 30000, Path.Combine(K.Apache, "bin")) : null)
                    .ContinueWith(t =>
                    {
                        if (IsDisposed) return;
                        if (t.IsFaulted || t.Result == null) { result.Text = t.IsFaulted ? t.Exception.GetBaseException().Message : "Apache is not installed."; result.ForeColor = Ui.Red; return; }
                        result.Text = (t.Result.ExitCode == 0 ? "✓ " : "✕ ") + t.Result.Output.Trim();
                        result.ForeColor = t.Result.ExitCode == 0 ? Ui.Green : Ui.Red;
                    }, TaskScheduler.FromCurrentSynchronizationContext());
            }), Btn("Preview unsaved changes", "file", PreviewApache)));
            Add(s2, result);
            Add(s2, Note("Startup command:"));
            Add(s2, Mono("httpd.exe " + Services.Apache.Args()));
            var s3 = Section(page, "refresh", "Restore", "Restoring goes through Save, with the same validation and rollback. Custom vhosts are never touched.");
            Add(s3, Line(Btn("Restore managed defaults", "refresh", RestoreApacheDefaults, "Threads, keep-alive, log level, listing, .htaccess, PHP mode and WebHut's modules back to WebHut's values"),
                         Btn("Replace httpd.conf with template…", "warning", () =>
                         {
                             if (MessageBox.Show(this, "Replace httpd.conf with WebHut's template? Any hand edits in httpd.conf are lost (a backup is kept in config\\backups). Files in custom\\ are not affected.",
                                 "Replace httpd.conf", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
                             _e.ResetHttpd = true; OnChanged();
                             _status.Text = "●  httpd.conf will be replaced with the template when you save  ·  " + Effects();
                         }),
                         Btn("Backups folder", "folder", () => { Directory.CreateDirectory(ConfigManager.BackupRoot); Sys.OpenFolder(ConfigManager.BackupRoot); })));
            return page;
        }

        void RestoreApacheDefaults()
        {
            var t = ApacheConfFile.Parse(Installer.Template("config/apache/httpd.conf"));
            EditApacheQuiet("ThreadsPerChild", t.Mpm("ThreadsPerChild"), _hc.Mpm("ThreadsPerChild"));
            EditApacheQuiet("MaxConnectionsPerChild", t.Mpm("MaxConnectionsPerChild"), _hc.Mpm("MaxConnectionsPerChild"));
            EditApacheQuiet("KeepAlive", "On", _hc.TopArgs("KeepAlive") ?? "On");
            EditApacheQuiet("KeepAliveTimeout", "5", _hc.TopArgs("KeepAliveTimeout") ?? "5");
            EditApacheQuiet("MaxKeepAliveRequests", "100", _hc.TopArgs("MaxKeepAliveRequests") ?? "100");
            EditApacheQuiet("LogLevel", "warn", (_hc.TopArgs("LogLevel") ?? "warn").ToLowerInvariant());
            EditApacheQuiet("Indexes", "On", (_hc.Indexes ?? true) ? "On" : "Off");
            EditApacheQuiet("Htaccess", "On", (_hc.HtaccessAllowed ?? true) ? "On" : "Off");
            EditApacheQuiet("Fcgid", "Off", _hc.Fcgid ? "On" : "Off");
            foreach (var m in t.Modules().Where(m => m.Active)) if (!ModuleActiveNow(m.Name)) _e.Modules[m.Name] = true;
            RebuildKeepingEdits();
        }

        void EditApacheQuiet(string key, string value, string original)
        {
            if (string.Equals(value, original, StringComparison.OrdinalIgnoreCase)) _e.Apache.Remove(key); else _e.Apache[key] = value;
        }

        /// <summary>Rebuild the pages so controls show pending edits made programmatically (restore defaults).</summary>
        void RebuildKeepingEdits()
        {
            var keep = _e;
            foreach (var c in _built.Values) { _scroll.Controls.Remove(c); c.Dispose(); }
            _built.Clear(); _refresh.Clear();
            _loading = true;
            ShowPage();
            _loading = false;
            _e = keep;
            ApplyEditsToControls();
            OnChanged();
            Toast.Show(this, "Defaults filled in. Review, then Save.", ToastKind.Info);
        }

        /// <summary>After a rebuild, controls show the saved values; pending edits are kept in _e and shown in the footer and pages via refreshers.</summary>
        void ApplyEditsToControls() { foreach (var r in _refresh) r(); }

        void PreviewApache()
        {
            try
            {
                if (_e.Apache.Count == 0 && _e.Modules.Count == 0 && !_e.ResetHttpd) { TextDialog.Show(this, "Preview", "No unsaved httpd.conf changes."); return; }
                var plan = ConfigManager.Plan(_e, Cfg, _mi, _ai);
                TextDialog.Show(this, "Pending httpd.conf changes", Diff(File.Exists(K.HttpdConf) ? File.ReadAllText(K.HttpdConf) : "", plan.HttpdText ?? File.ReadAllText(K.HttpdConf)));
            }
            catch (ConfigException ex) { Ui.Error(this, "Preview", string.Join("\n", ex.Problems)); }
        }

        /// <summary>A minimal line diff for previews ("- old", "+ new", with line numbers).</summary>
        static string Diff(string a, string b)
        {
            var x = a.Replace("\r\n", "\n").Split('\n');
            var y = b.Replace("\r\n", "\n").Split('\n');
            int n = x.Length, m = y.Length;
            var lcs = new int[n + 1, m + 1];
            for (int i = n - 1; i >= 0; i--)
                for (int j = m - 1; j >= 0; j--)
                    lcs[i, j] = x[i] == y[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            var sb = new StringBuilder();
            int p = 0, q = 0;
            while (p < n || q < m)
            {
                if (p < n && q < m && x[p] == y[q]) { p++; q++; continue; }
                if (q < m && (p == n || lcs[p, q + 1] >= lcs[p + 1, q])) { sb.AppendLine("+ " + (q + 1).ToString().PadLeft(4) + "  " + y[q]); q++; }
                else { sb.AppendLine("- " + (p + 1).ToString().PadLeft(4) + "  " + x[p]); p++; }
            }
            return sb.Length == 0 ? "No differences." : sb.ToString();
        }

        void CopyText(string text)
        {
            try { Clipboard.SetText(text); Toast.Show(this, "Copied.", ToastKind.Info); } catch { }
        }

        // ================================================================== MariaDB pages

        /// <summary>"my.ini 150 · running 150 · next start 150 · applies immediately", amber when they disagree.</summary>
        Label MStatus(MariaSetting s)
        {
            var l = Note("");
            Action upd = () =>
            {
                var parts = new List<string>();
                string file = _ini.Get(s.Section, s.Key);
                string next = null, live = null;
                if (s.Section == "mysqld")
                {
                    if (_mi.NextStart != null) _mi.NextStart.Vars.TryGetValue(s.Key, out next);
                    live = _mi.LiveValue(s.Key);
                }
                Func<string, string> show = v => v == null ? "—" : s.Kind == "size" && MariaCatalog.Bytes(v) != null ? MariaCatalog.Human(MariaCatalog.Bytes(v).Value) : v;
                parts.Add("my.ini: " + (file == null ? "not set (MariaDB default)" : show(file)));
                if (s.Section != "mysqld") { l.Text = parts[0] + " · read by clients that use this option file"; l.ForeColor = Ui.Muted; return; }
                if (_mi.Running && live != null) parts.Add("running: " + show(live));
                if (next != null && (file == null || MariaCatalog.Norm(s.Kind, next) != MariaCatalog.Norm(s.Kind, file))) parts.Add("next start: " + show(next));
                bool? sup = _mi.Supports(s.Key);
                if (sup == false) parts.Add("not supported by MariaDB " + _mi.Version);
                else if (_mi.Running) parts.Add(_mi.Dynamic(s.Key) == true ? "applies immediately" : "restart required");
                else parts.Add("used at next start");
                l.Text = string.Join("  ·  ", parts);
                bool differs = _mi.Running && live != null && file != null && MariaCatalog.Norm(s.Kind, live) != MariaCatalog.Norm(s.Kind, file);
                if (differs) l.Text += "\nThe running value differs from my.ini (changed at runtime, or the server hasn't restarted since my.ini changed).";
                l.ForeColor = sup == false || differs ? Ui.Amber : Ui.Muted;
            };
            upd();
            return l;
        }

        string Cur(MariaSetting s)
        {
            string v;
            if (_e.Maria.TryGetValue(s.Id, out v)) return v;
            return _ini.Get(s.Section, s.Key) ?? (s.Section == "mysqld" && _mi.NextStart != null && _mi.NextStart.Vars.TryGetValue(s.Key, out v) ? v : s.Default);
        }

        bool Supported(MariaSetting s) { return s.Section != "mysqld" || _mi.Supports(s.Key) != false; }

        Control MyGeneral()
        {
            var page = Page("MariaDB · General", "Settings come from three places: WebHut's startup arguments (they win), my.ini, and changes made while the server runs.");
            if (!_mi.Installed) page.Controls.Add(Call(CalloutKind.Warning, "MariaDB is not installed. Run setup first."));
            var s = Section(page, "server", "Server", null);
            var portNote = Note("");
            Action pt = () =>
            {
                if (portNote.ForeColor == Ui.Red) return;
                portNote.Text = "Effective: --port=" + (_e.MysqlPort ?? Cfg.MysqlPort) + " (startup argument)" + (Services.MariaDb.IsRunning ? " · running on " + Services.MariaDb.ActivePort : " · stopped") +
                                (_e.MysqlPort != null ? " · saving restarts MariaDB and Apache (phpMyAdmin follows)" : "");
            };
            Row(s, "Port", "Default 3306. Update your apps' database settings after changing it.", "WebHut setting",
                PortField(Cfg.MysqlPort, 1, v => { _e.MysqlPort = v == Cfg.MysqlPort ? (int?)null : v; OnChanged(); }, v => PortProblem(v, Cfg.MysqlPort, false), portNote), portNote);
            _refresh.Add(pt); pt();
            Row(s, "Bind address", "MariaDB only accepts connections from this PC. Apache's network-access setting never changes this.", "WebHut security · read-only", Mono("127.0.0.1  (--bind-address startup argument)"));
            Row(s, "Data directory", "Moving it isn't supported here; your databases are never moved or deleted.", "WebHut runtime · read-only",
                Line(Mono(Shorten(K.MariaDbData)), Btn("Open folder", "folder", () => Sys.OpenFolder(K.MariaDbData))));
            Row(s, "Backup & restore", "Back up before risky changes. Backups are logical (mariadb-dump), not copies of live files.", null,
                Line(Btn("Backup & restore…", "copy", () => { using (var d = new DatabaseForm()) d.ShowDialog(this); }), Btn("Back up now", "copy", BackupNow)));

            var s2 = Section(page, "file", "Character set", "Changing the server default affects new databases and tables only; existing ones keep their character set and collation.");
            var cs = CharsetDropdown(MariaCatalog.Get("mysqld.character_set_server"));
            var coll = new Dropdown(Px(260));
            Action fillColl = () =>
            {
                string charset = Cur(MariaCatalog.Get("mysqld.character_set_server")).ToLowerInvariant();
                List<string> list;
                coll.Items.Clear();
                if (_mi.Collations.TryGetValue(charset, out list)) coll.Items.AddRange(list);
                else coll.Items.AddRange(new[] { charset + "_general_ci", charset + "_bin", charset + "_unicode_ci" });
                string want = Cur(MariaCatalog.Get("mysqld.collation_server")).ToLowerInvariant();
                if (!coll.Items.Contains(want))
                {
                    string def;
                    want = _mi.DefaultCollation.TryGetValue(charset, out def) ? def : charset == "utf8mb4" ? "utf8mb4_unicode_ci" : coll.Items.FirstOrDefault();
                    if (want != null && !_loading) EditMaria("mysqld.collation_server", want);
                }
                coll.SelectedItem = want;
                coll.Invalidate();
            };
            cs.SelectedIndexChanged += (x, e) => { EditMaria("mysqld.character_set_server", cs.SelectedItem); fillColl(); };
            coll.SelectedIndexChanged += (x, e) => { if (coll.SelectedItem != null) EditMaria("mysqld.collation_server", coll.SelectedItem); };
            fillColl();
            Row(s2, "Server character set", "utf8mb4 stores every character, including emoji.", "my.ini [mysqld]", cs, MStatus(MariaCatalog.Get("mysqld.character_set_server")));
            Row(s2, "Server collation", "How text is compared and sorted. Only collations of the selected character set are listed.", "my.ini [mysqld]", coll, MStatus(MariaCatalog.Get("mysqld.collation_server")));
            var client = CharsetDropdown(MariaCatalog.Get("client.default_character_set"));
            client.SelectedIndexChanged += (x, e) => EditMaria("client.default_character_set", client.SelectedItem);
            Row(s2, "Client character set", "For WebHut's mariadb and mariadb-dump tools ([client] in my.ini). Your apps set their own connection character set.", "my.ini [client]", client, MStatus(MariaCatalog.Get("client.default_character_set")));
            return page;
        }

        Dropdown CharsetDropdown(MariaSetting s)
        {
            var d = new Dropdown(Px(200));
            var sets = _mi.Collations.Count > 0 ? _mi.Collations.Keys.ToList() : new List<string> { "utf8mb4", "utf8mb3", "latin1", "ascii", "binary" };
            string cur = Cur(s).ToLowerInvariant();
            if (!sets.Contains(cur)) sets.Insert(0, cur);
            d.Items.AddRange(sets);
            d.SelectedItem = cur;
            return d;
        }

        void BackupNow()
        {
            if (!Services.MariaDb.IsRunning) { Toast.Show(this, "Start MariaDB first.", ToastKind.Warning); return; }
            var cfg = Cfg;
            Toast.Show(this, "Backing up…", ToastKind.Info);
            Task.Run(() => Backup.Create(cfg, "from server configuration")).ContinueWith(t =>
            {
                if (IsDisposed) return;
                if (t.IsFaulted) Toast.Show(this, "Backup failed: " + t.Exception.GetBaseException().Message, ToastKind.Error);
                else Toast.Show(this, t.Result == null ? "No databases to back up yet." : "Backup saved.", ToastKind.Success);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        Control SizeRow(TableLayoutPanel t, string id, string label, string hint, int minMb, int maxMb, int[] presets)
        {
            var s = MariaCatalog.Get(id);
            long bytes = MariaCatalog.Bytes(Cur(s)) ?? MariaCatalog.Bytes(s.Default).Value;
            int mb = (int)Math.Max(1, bytes / 1048576);
            var num = new NumberField(minMb, maxMb, Px(110)) { Value = mb, Enabled = Supported(s) };
            Control ctl = Line(num, new Label { UseMnemonic = false, Text = "MB", Font = Ui.Base, ForeColor = Ui.Muted, AutoSize = true });
            if (presets != null)
            {
                var dd = new Dropdown(Px(140));
                foreach (var p in presets) dd.Items.Add(p >= 1024 ? (p / 1024) + " GB" : p + " MB");
                dd.Items.Add("Custom");
                int idx = Array.IndexOf(presets, mb);
                dd.SelectedIndex = idx >= 0 ? idx : presets.Length;
                dd.Enabled = Supported(s);
                bool sync = false;
                dd.SelectedIndexChanged += (x, e) => { if (sync || dd.SelectedIndex >= presets.Length) return; sync = true; num.Value = presets[dd.SelectedIndex]; sync = false; };
                num.ValueChanged += (x, e) => { if (!num.IsValid) return; if (!sync) { sync = true; int i = Array.IndexOf(presets, num.Value); dd.SelectedIndex = i >= 0 ? i : presets.Length; sync = false; } EditMaria(id, num.Value + "M"); };
                ctl = Line(dd, num, new Label { UseMnemonic = false, Text = "MB", Font = Ui.Base, ForeColor = Ui.Muted, AutoSize = true });
            }
            else num.ValueChanged += (x, e) => { if (num.IsValid) EditMaria(id, num.Value + "M"); };
            Row(t, label, hint, "my.ini [mysqld]", ctl, MStatus(s));
            return ctl;
        }

        Control MyPerformance()
        {
            var page = Page("MariaDB · Performance", "Connections and packet size.");
            var s = Section(page, "bolt", "Limits", null);
            var mcS = MariaCatalog.Get("mysqld.max_connections");
            SysVar sv; _mi.Live.TryGetValue("max_connections", out sv);
            int lo = sv != null ? Int(sv.Min, 10) : 10, hi = Math.Min(100000, sv != null ? Int(sv.Max, 100000) : 100000);
            var mc = new NumberField(lo, hi, Px(120)) { Value = Int(Cur(mcS), 100) };
            mc.ValueChanged += (x, e) => { if (mc.IsValid) EditMaria(mcS.Id, mc.Value.ToString()); };
            Row(s, "Maximum connections", "Simultaneous client connections (" + lo + "–" + hi + "). Each connection uses some memory, so higher limits use more RAM.", "my.ini [mysqld]", mc, MStatus(mcS));
            SizeRow(s, "mysqld.max_allowed_packet", "Maximum allowed packet", "Largest single query or row MariaDB accepts — raise it for big imports (1–1024 MB).", 1, 1024, null);
            return page;
        }

        Control MyInnoDb()
        {
            var page = Page("MariaDB · InnoDB", "Memory and redo log for InnoDB, the default storage engine.");
            var s = Section(page, "database", "Memory", null);
            SizeRow(s, "mysqld.innodb_buffer_pool_size", "Buffer pool size", "Cache for table data and indexes. 256 MB suits most development work.", 5, 1048576, new[] { 128, 256, 512, 1024, 2048 });
            long ram = TotalRam();
            if (ram > 0)
                Add(s, Call(CalloutKind.Info, "This PC has " + MariaCatalog.Human(ram - ram % (1L << 30)) + " of RAM. For development, 256 MB–1 GB is plenty; the usual \"70% of RAM\" advice is for dedicated database servers, not a laptop running a browser and an IDE. WebHut never changes this automatically."));
            var s2 = Section(page, "file", "Redo log", null);
            var lf = MariaCatalog.Get("mysqld.innodb_log_file_size");
            SizeRow(s2, lf.Id, "Log file size", "Larger logs mean fewer checkpoints during big imports, but longer crash recovery.", 4, 524288, null);
            string verNote = _mi.Supports("innodb_log_file_size") == false ? "This MariaDB version doesn't have innodb_log_file_size."
                : _mi.Dynamic("innodb_log_file_size") == true ? "MariaDB " + _mi.Version + " resizes the redo log while running (10.11 and later); no restart needed."
                : _mi.Running ? "MariaDB " + _mi.Version + " needs a restart for this; it recreates the redo log automatically after a clean shutdown (10.5 and later)."
                : "Whether it can change without a restart depends on the version; WebHut asks the server when it is running.";
            Add(s2, Note(verNote));
            return page;
        }

        static long TotalRam()
        {
            try
            {
                if (Sys.IsWindows)
                {
                    var m = new MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(MEMORYSTATUSEX)) };
                    if (GlobalMemoryStatusEx(ref m)) return (long)m.ullTotalPhys;
                }
                else if (File.Exists("/proc/meminfo"))
                {
                    var l = File.ReadAllLines("/proc/meminfo").FirstOrDefault(x => x.StartsWith("MemTotal:"));
                    if (l != null) return long.Parse(System.Text.RegularExpressions.Regex.Match(l, @"\d+").Value) * 1024;
                }
            }
            catch { }
            return 0;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct MEMORYSTATUSEX { public uint dwLength, dwMemoryLoad; public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual; }
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);

        Control MyReliability()
        {
            var page = Page("MariaDB · Reliability & Recovery", "How MariaDB protects committed data when the PC crashes or loses power. No setting guarantees zero loss on every hardware failure.");
            page.Controls.Add(Call(CalloutKind.Info, "Back up before changing these settings (Backup & restore in General). Changing them never converts or moves existing data."));
            var s = Section(page, "refresh", "Durability", null);
            var fl = MariaCatalog.Get("mysqld.innodb_flush_log_at_trx_commit");
            var dd = new Dropdown(Px(380));
            dd.Items.Add("1 — Maximum durability (flush at every commit)");
            dd.Items.Add("2 — Faster (write at commit, flush about once a second)");
            dd.Items.Add("0 — Reduced durability (write and flush about once a second)");
            string cur = MariaCatalog.Norm("enum", Cur(fl));
            dd.SelectedIndex = cur == "2" ? 1 : cur == "0" ? 2 : 0;
            var flWarn = Call(CalloutKind.Warning, "");
            Action fw = () =>
            {
                flWarn.Visible = dd.SelectedIndex > 0;
                flWarn.Set(CalloutKind.Warning, dd.SelectedIndex == 1 ? "With 2, about the last second of committed transactions can be lost if Windows crashes or the power fails (a MariaDB crash alone loses nothing)."
                                                                      : "With 0, about the last second of committed transactions can be lost even if only MariaDB crashes.");
            };
            dd.SelectedIndexChanged += (x, e) => { EditMaria(fl.Id, dd.SelectedIndex == 1 ? "2" : dd.SelectedIndex == 2 ? "0" : "1"); fw(); };
            Row(s, "Flush log at commit", "innodb_flush_log_at_trx_commit. WebHut's default is 1. With 1, commits are on disk before MariaDB answers, as far as the disk and Windows guarantee it.", "my.ini [mysqld]", dd, MStatus(fl));
            Add(s, flWarn); fw();

            var dw = MariaCatalog.Get("mysqld.innodb_doublewrite");
            SysVar dv; _mi.Live.TryGetValue("innodb_doublewrite", out dv);
            var dwSw = new Switch { Checked = MariaCatalog.Norm("bool", Cur(dw)) != "0", Enabled = Supported(dw) };
            var dwWarn = Call(CalloutKind.Warning, "Without doublewrite, a page half-written during a power cut can't be repaired and the table may need restoring from a backup.");
            dwSw.CheckedChanged += (x, e) => { EditMaria(dw.Id, dwSw.Checked ? "1" : "0"); dwWarn.Visible = !dwSw.Checked; };
            Toggle(s, dwSw, "Doublewrite buffer", "innodb_doublewrite. Pages are written twice so a partially written page can be recovered after a crash. Default on." +
                   (dv != null && dv.Enum.Length > 2 ? " (This version also supports: " + string.Join(", ", dv.Enum) + " — edit my.ini for those.)" : ""), "my.ini [mysqld]", MStatus(dw));
            Add(s, dwWarn); dwWarn.Visible = !dwSw.Checked;

            var fpt = MariaCatalog.Get("mysqld.innodb_file_per_table");
            var fptSw = new Switch { Checked = MariaCatalog.Norm("bool", Cur(fpt)) != "0", Enabled = Supported(fpt) };
            fptSw.CheckedChanged += (x, e) => EditMaria(fpt.Id, fptSw.Checked ? "1" : "0");
            Toggle(s, fptSw, "File per table", "innodb_file_per_table. New InnoDB tables get their own .ibd file (space is returned when you drop or rebuild them). Existing tables aren't moved.", "my.ini [mysqld]", MStatus(fpt));

            var s2 = Section(page, "server", "Aria recovery", "Aria holds MariaDB's own system tables. These options decide what happens when an Aria table is found damaged at startup.");
            var ar = MariaCatalog.Get("mysqld.aria_recover_options");
            var presets = new[] { "BACKUP,QUICK", "BACKUP", "BACKUP,FORCE,QUICK", "NORMAL", "OFF" };
            var labels = new[] { "BACKUP,QUICK — repair, keep a backup (WebHut default)", "BACKUP — repair fully, keep a backup", "BACKUP,FORCE,QUICK — repair even if rows are lost", "NORMAL — repair", "OFF — don't repair automatically" };
            var add = new Dropdown(Px(420));
            add.Items.AddRange(labels);
            string arCur = MariaCatalog.Norm("set", Cur(ar));
            int ai = Array.FindIndex(presets, p => MariaCatalog.Norm("set", p) == arCur);
            if (ai < 0) { add.Items.Add(Cur(ar) + " — current value (custom)"); ai = add.Items.Count - 1; }
            add.SelectedIndex = ai;
            add.SelectedIndexChanged += (x, e) => { if (add.SelectedIndex < presets.Length) EditMaria(ar.Id, presets[add.SelectedIndex]); };
            Row(s2, "Recovery options", "BACKUP keeps a copy of anything a repair changes. QUICK skips rows that aren't damaged. FORCE continues even if rows would be lost.", "my.ini [mysqld]", add, MStatus(ar));
            return page;
        }

        Control MySecurity()
        {
            var page = Page("MariaDB · Security", "The root account, network exposure and other accounts.");
            var s = Section(page, "key", "Root password", "The password is stored encrypted for your Windows account (DPAPI), never in my.ini, webhut.json or logs.");
            bool none = string.IsNullOrEmpty(Cfg.MysqlRootPassword);
            var state = new Label { UseMnemonic = false, Text = Cfg.PasswordUnreadable ? "Saved password unreadable" : none ? "No password" : "Password set", Font = Ui.Bold, ForeColor = none || Cfg.PasswordUnreadable ? Ui.Amber : Ui.Green, AutoSize = true };
            var test = Note("");
            Row(s, "Root account", "Used by phpMyAdmin, WebHut's backups and your apps.", "MariaDB accounts",
                Line(state, Btn(none ? "Set password…" : "Change password…", "key", ChangePassword), Btn("Test connection", "pulse", () => TestConnection(test))), test);
            Row(s, "Saved password out of date?", "If the password was changed outside WebHut (or webhut.json came from another PC), enter the current one. WebHut checks it with MariaDB before saving it.", null,
                Btn("Enter current password…", "key", EnterSavedPassword));
            if (none) Add(s, Call(CalloutKind.Warning, "No root password is fine for a local-only setup, like XAMPP. Set one if other people use this PC."));

            var s2 = Section(page, "globe", "Network", null);
            Row(s2, "Bind address", "WebHut starts MariaDB with --bind-address=127.0.0.1: only programs on this PC can connect. Turning on Apache's network access never changes this.", "WebHut security · read-only", Mono("127.0.0.1"));
            var bind = _ini.Effective("bind_address");
            if (bind != null) Add(s2, Call(CalloutKind.Info, "my.ini sets bind-address = " + bind.Value + " (line " + (bind.Line + 1) + "). It has no effect: the startup argument wins. You can comment it out in Advanced."));
            Add(s2, Note("Remote database access isn't offered here: it needs firewall rules, a non-root user with limited privileges, and strong authentication. Connect over SSH or use a tunnel instead."));

            var s3 = Section(page, "server", "Accounts", null);
            if (!_mi.Running) Add(s3, Note("Start MariaDB to inspect its accounts."));
            else if (!_mi.Connected) Add(s3, Call(CalloutKind.Warning, "Couldn't connect as root: " + _mi.ConnectError));
            else
            {
                if (_mi.Anonymous.Count == 0) Add(s3, Note("No anonymous accounts ✓", Ui.Green));
                foreach (var a in _mi.Anonymous)
                {
                    string acc = a;
                    var warn = new Callout(CalloutKind.Warning, "Anonymous account " + acc + ": anyone can log in without a user name.", CalloutW - Px(140)) { Margin = new Padding(0, Px(4), Px(10), Px(4)) };
                    var rm = Btn("Remove…", "trash", () => DropAnonymous(acc));
                    Add(s3, Line(warn, rm));
                }
                Row(s3, "Root authentication", "The plugin each root account uses (MariaDB versions differ).", null, Mono(_mi.RootAccounts.Count == 0 ? "(couldn't read mysql.user)" : string.Join("\n", _mi.RootAccounts)));
            }
            return page;
        }

        void ChangePassword()
        {
            var pw = Ui.Prompt(this, "Database root password", "New root password (leave empty for none):", true);
            if (pw == null) return;
            string old = Cfg.MysqlRootPassword;
            SetBusy(true, "Changing the root password…");
            Task.Run(() =>
            {
                bool wasRunning = Services.MariaDb.IsRunning;
                if (!wasRunning) Services.MariaDb.Start();
                Services.MariaDb.ChangeRootPassword(old, pw);
                Cfg.MysqlRootPassword = pw;
                Cfg.PasswordUnreadable = false;
                Cfg.Save();
                new Installer(Cfg).UpdatePmaConfig();
                Log.Info("Root password changed.");
            }).ContinueWith(t =>
            {
                if (IsDisposed) return;
                SetBusy(false, "");
                if (t.IsFaulted) { Ui.Error(this, "Root password", t.Exception.GetBaseException().Message + "\n\nIf the saved password is wrong, enter the current one with Test connection first."); return; }
                Toast.Show(this, "Root password changed.", ToastKind.Success);
                Reload(null);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void EnterSavedPassword()
        {
            if (!Services.MariaDb.IsRunning) { Toast.Show(this, "Start MariaDB first, so the password can be checked.", ToastKind.Warning); return; }
            var pw = Ui.Prompt(this, "Current root password", "The root password MariaDB uses now:", true);
            if (pw == null) return;
            string err;
            var rows = Services.MariaDb.Query("SELECT 1;", pw, out err);
            if (rows.Count == 0) { Ui.Error(this, "Root password", "MariaDB didn't accept that password:\n" + err); return; }
            Cfg.MysqlRootPassword = pw;
            Cfg.PasswordUnreadable = false;
            Cfg.Save();
            try { new Installer(Cfg).UpdatePmaConfig(); } catch { }
            Log.Info("Saved root password updated (verified with MariaDB).");
            Toast.Show(this, "Password verified and saved (encrypted).", ToastKind.Success);
            Reload(null);
        }

        void TestConnection(Label result)
        {
            if (!Services.MariaDb.IsRunning) { result.Text = "MariaDB is stopped."; result.ForeColor = Ui.Amber; return; }
            result.Text = "Connecting…"; result.ForeColor = Ui.Muted;
            var cfg = Cfg;
            Task.Run(() => { string err; var rows = Services.MariaDb.Query("SELECT CURRENT_USER(), VERSION();", cfg.MysqlRootPassword, out err); return rows.Count > 0 ? "✓ Connected as " + rows[0][0] + " · MariaDB " + rows[0][1] : "✕ " + err; })
                .ContinueWith(t =>
                {
                    if (IsDisposed) return;
                    result.Text = t.IsFaulted ? "✕ " + t.Exception.GetBaseException().Message : t.Result;
                    result.ForeColor = result.Text.StartsWith("✓") ? Ui.Green : Ui.Red;
                    if (result.Text.Contains("Access denied")) result.Text += "\nThe saved password doesn't match. Use Change password, or fix it in phpMyAdmin.";
                }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void DropAnonymous(string account)
        {
            if (MessageBox.Show(this, "Remove the anonymous account " + account + "?", "Remove account", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            string err;
            Services.MariaDb.Query("DROP USER " + account + ";", Cfg.MysqlRootPassword, out err);
            if (!string.IsNullOrEmpty(err)) Ui.Error(this, "Remove account", err);
            else { Log.Info("Removed anonymous MariaDB account " + account + "."); Reload(null); }
        }

        Control MyLogging()
        {
            var page = Page("MariaDB · Logging & Diagnostics", "Error log, slow query log and live server information.");
            var s = Section(page, "logs", "Error log", "Set by WebHut's --log-error startup argument; a log-error line in my.ini would have no effect.");
            string err = Services.MariaDb.LogFile;
            Row(s, "Location", null, "WebHut runtime · read-only", Line(Btn("Open", "file", () => Sys.OpenInEditor(err)), Btn("Open folder", "folder", () => Sys.OpenFolder(K.Logs))), Mono(err, Ui.Muted));
            Add(s, new LogView(this, new[] { "mariadb.err" }, i => err, false));

            var s2 = Section(page, "pulse", "Slow query log", null);
            var sl = MariaCatalog.Get("mysqld.slow_query_log");
            var slSw = new Switch { Checked = MariaCatalog.Norm("bool", Cur(sl)) == "1" };
            slSw.CheckedChanged += (x, e) => EditMaria(sl.Id, slSw.Checked ? "1" : "0");
            Toggle(s2, slSw, "Log slow queries", "Off by default.", "my.ini [mysqld]", MStatus(sl));
            var lq = MariaCatalog.Get("mysqld.long_query_time");
            var lqF = new TextField(Px(120), true) { Value = Cur(lq) };
            lqF.Validate = v => { double d; return double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d) && d >= 0 && d <= 31536000; };
            lqF.ValueChanged += (x, e) => { if (lqF.IsValid) EditMaria(lq.Id, lqF.Value); };
            Row(s2, "Threshold (seconds)", "Queries that take longer may be logged while the slow log is on. Decimals such as 0.5 are allowed.", "my.ini [mysqld]", lqF, MStatus(lq));
            string slowFile = SlowLogPath();
            string output = _mi.LiveValue("log_output");
            Row(s2, "Log destination", "Resolved from the server (slow_query_log_file, log_output).", null, Mono(slowFile == null ? "(unknown until MariaDB has run)" : slowFile, Ui.Muted),
                output != null && !output.ToUpperInvariant().Contains("FILE") ? Note("log_output is " + output + ": slow queries go to the mysql.slow_log table, not a file.", Ui.Amber) : null);
            if (slowFile != null) Add(s2, new LogView(this, new[] { "Slow queries" }, i => slowFile, false));

            var s3 = Section(page, "server", "Diagnostics", "Live values from the running server, read when this window opened (nothing is polled continuously).");
            if (!_mi.Running) Add(s3, Note("MariaDB is stopped."));
            else if (!_mi.Connected) Add(s3, Call(CalloutKind.Warning, "Couldn't connect: " + _mi.ConnectError));
            else
            {
                Func<string, string> st = k => { string v; return _mi.Status.TryGetValue(k, out v) ? v : "—"; };
                long up; long.TryParse(st("uptime"), out up);
                var ts = TimeSpan.FromSeconds(up);
                var bp = MariaCatalog.Bytes(_mi.LiveValue("innodb_buffer_pool_size") ?? "");
                Add(s3, Mono(
                    "Status             Running on 127.0.0.1:" + Services.MariaDb.ActivePort + "\n" +
                    "Version            " + (_mi.LiveValue("version") ?? _mi.Version) + "\n" +
                    "Uptime             " + (ts.Days > 0 ? ts.Days + "d " : "") + ts.Hours + "h " + ts.Minutes + "m\n" +
                    "Connections        " + st("threads_connected") + " active · " + st("max_used_connections") + " peak · " + (_mi.LiveValue("max_connections") ?? "?") + " allowed\n" +
                    "Data directory     " + (_mi.LiveValue("datadir") ?? K.MariaDbData) + "\n" +
                    "Buffer pool        " + (bp == null ? "?" : MariaCatalog.Human(bp.Value)) + "\n" +
                    "Slow queries       " + st("slow_queries")));
                Add(s3, Line(Btn("Refresh", "refresh", () => Reload(() => Go("mariadb", "Logging & Diagnostics")))));
            }
            return page;
        }

        string SlowLogPath()
        {
            string f = _mi.LiveValue("slow_query_log_file");
            if (f == null && _mi.NextStart != null) _mi.NextStart.Vars.TryGetValue("slow_query_log_file", out f);
            if (string.IsNullOrEmpty(f)) return null;
            if (!Path.IsPathRooted(f)) f = Path.Combine(_mi.LiveValue("datadir") ?? K.MariaDbData, f);
            return f;
        }

        Control MyAdvanced()
        {
            var page = Page("MariaDB · Advanced", "Direct access, validation, defaults and where each setting comes from.");
            var s = Section(page, "terminal", "Files and status", null);
            Add(s, Line(Btn("Open my.ini", "file", () => Sys.OpenInEditor(K.MyIni)), Btn("Configuration folder", "folder", () => Sys.OpenFolder(Path.GetDirectoryName(K.MyIni))),
                        Btn("Open error log", "file", () => Sys.OpenInEditor(Services.MariaDb.LogFile))));
            Add(s, Note("Service: " + (Services.MariaDb.IsRunning ? "running (PID " + Services.MariaDb.Pid() + ", port " + Services.MariaDb.ActivePort + ")" : "stopped") + " · MariaDB " + _mi.Version));
            Add(s, Note("Startup arguments (these override my.ini):"));
            Add(s, Mono("mariadbd.exe --defaults-file=" + K.Fwd(K.MyIni) + "\n  --datadir=" + K.Fwd(K.MariaDbData) + " --port=" + Cfg.MysqlPort + "\n  --bind-address=127.0.0.1 --log-error=" + K.Fwd(Services.MariaDb.LogFile)));
            var incl = _ini.Includes();
            if (incl.Count > 0) Add(s, Call(CalloutKind.Info, "my.ini includes other files (" + string.Join(", ", incl) + "). Settings there can override these pages; the next-start values shown come from MariaDB itself and include them."));

            var s2 = Section(page, "check", "Validate", "Asks mariadbd how it reads my.ini (--help --verbose), then test-starts a throw-away server on an empty scratch folder and a free port. Your data folder is never opened.");
            var result = Note("");
            Add(s2, Line(Btn("Validate configuration", "play", () =>
            {
                result.Text = "Validating…"; result.ForeColor = Ui.Muted;
                var cfg = Cfg;
                Task.Run(() =>
                {
                    var c = MariaInfo.Check(K.MyIni, cfg);
                    if (!c.Ok) return "✕ " + string.Join("\n", c.Errors);
                    string start = MariaInfo.ScratchStart(K.MyIni, cfg);
                    return start == null ? "✓ my.ini is valid and MariaDB starts with it." + (c.Warnings.Count > 0 ? "\nWarnings:\n" + string.Join("\n", c.Warnings) : "") : "✕ " + start;
                }).ContinueWith(t =>
                {
                    if (IsDisposed) return;
                    result.Text = t.IsFaulted ? "✕ " + t.Exception.GetBaseException().Message : t.Result;
                    result.ForeColor = result.Text.StartsWith("✓") ? Ui.Green : Ui.Red;
                }, TaskScheduler.FromCurrentSynchronizationContext());
            }), Btn("Effective variables…", "file", ShowVariables), Btn("Preview unsaved changes", "file", PreviewMaria)));
            Add(s2, result);

            var s3 = Section(page, "key", "Where settings come from", "Highest priority first: startup arguments, then changes made while running (until restart), then my.ini, then MariaDB's defaults.");
            Row(s3, "Port, bind address, data folder, error log, PID file", null, null, Mono("WebHut settings → startup arguments"));
            Row(s3, "Buffer pool, connections, packet size, durability, logs, character set", null, null, Mono("my.ini [mysqld] — this window"));
            Row(s3, "Client character set", null, null, Mono("my.ini [client]"));
            Row(s3, "Anything else in my.ini", null, null, Mono("Yours — kept exactly as written"));
            var conflicts = _ini.Entries().Where(x => OptionFile.IsServerGroup(x.Section) && MariaCatalog.StartupArgs.Contains(x.Key)).ToList();
            foreach (var c in conflicts)
            {
                var cc = c;
                var chk = new CheckMark { Checked = _e.CommentOut.Contains(c.Line) };
                chk.CheckedChanged += (x, e) => { if (chk.Checked) { if (!_e.CommentOut.Contains(cc.Line)) _e.CommentOut.Add(cc.Line); } else _e.CommentOut.Remove(cc.Line); OnChanged(); };
                Add(s3, Call(CalloutKind.Warning, "my.ini line " + (c.Line + 1) + ": " + c.RawKey + (c.Value != null ? " = " + c.Value : "") + " — " + MariaCatalog.StartupOwner(c.Key)));
                Add(s3, Line(chk, new Label { UseMnemonic = false, Text = "Comment out this line when saving", Font = Ui.Small, ForeColor = Ui.Ink, AutoSize = true }));
            }
            foreach (var en in _ini.Entries().Where(x => OptionFile.IsServerGroup(x.Section) && x.Section != "mysqld" && MariaCatalog.All.Any(m => m.Section == "mysqld" && m.Key == x.Key)))
                Add(s3, Call(CalloutKind.Info, en.Key + " is set in [" + en.Section + "] (line " + (en.Line + 1) + "). That group is read after [mysqld], so this window shows it read-only; edit my.ini by hand."));

            var s4 = Section(page, "refresh", "Restore", "Puts WebHut's values back for the settings in these pages; your other my.ini lines are kept. Goes through Save, with validation and rollback.");
            Add(s4, Line(Btn("Restore managed defaults", "refresh", () =>
            {
                foreach (var m in MariaCatalog.All)
                {
                    string cur = _ini.Get(m.Section, m.Key);
                    if (cur == null || MariaCatalog.Norm(m.Kind, cur) != MariaCatalog.Norm(m.Kind, m.Default)) _e.Maria[m.Id] = m.Default; else _e.Maria.Remove(m.Id);
                }
                RebuildKeepingEdits();
            }), Btn("Backups folder", "folder", () => { Directory.CreateDirectory(ConfigManager.BackupRoot); Sys.OpenFolder(ConfigManager.BackupRoot); })));
            return page;
        }

        void ShowVariables()
        {
            var sb = new StringBuilder();
            if (_mi.Live.Count > 0)
            {
                sb.AppendLine("# Live values from the running server (information_schema.SYSTEM_VARIABLES). [ro] = needs a restart to change.");
                foreach (var v in _mi.Live.Values.OrderBy(v => v.Name)) sb.AppendLine(v.Name.PadRight(48) + (v.ReadOnly ? "[ro] " : "     ") + v.Value);
            }
            else if (_mi.NextStart != null && _mi.NextStart.Vars.Count > 0)
            {
                sb.AppendLine("# MariaDB is stopped. Values it would start with (my.ini + WebHut's startup arguments, from mariadbd --help --verbose).");
                foreach (var kv in _mi.NextStart.Vars.OrderBy(k => k.Key)) sb.AppendLine(kv.Key.PadRight(48) + kv.Value);
            }
            else sb.AppendLine("No information available (is MariaDB installed?).");
            TextDialog.Show(this, "Effective server variables", sb.ToString());
        }

        void PreviewMaria()
        {
            try
            {
                if (_e.Maria.Count == 0 && _e.CommentOut.Count == 0) { TextDialog.Show(this, "Preview", "No unsaved my.ini changes."); return; }
                var plan = ConfigManager.Plan(_e, Cfg, _mi, _ai);
                TextDialog.Show(this, "Pending my.ini changes", Diff(File.ReadAllText(K.MyIni), plan.MyIniText ?? File.ReadAllText(K.MyIni)));
            }
            catch (ConfigException ex) { Ui.Error(this, "Preview", string.Join("\n", ex.Problems)); }
        }

        // ================================================================== log viewer

        /// <summary>The newest lines of a log, with a severity filter and optional auto-refresh.</summary>
        class LogView : TableLayoutPanel
        {
            readonly TextBox _text;
            readonly Segmented _filter, _source;
            readonly Switch _auto;
            readonly Func<int, string> _file;
            readonly Timer _timer = new Timer { Interval = 2000 };
            public LogView(ConfigForm owner, string[] sources, Func<int, string> file, bool auto)
            {
                _file = file;
                AutoSize = true; ColumnCount = 1; BackColor = Color.Transparent; Margin = new Padding(0, Px(6), 0, Px(8));
                var bar = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, Px(6)) };
                _source = new Segmented(sources);
                _filter = new Segmented("All", "Warnings & errors", "Errors");
                _auto = new Switch { Checked = auto };
                var refresh = new IconButton("", "refresh", ButtonKind.Outline) { IconColor = Ui.NavInk };
                refresh.SetMin(Px(36), Px(32));
                refresh.Click += (s, e) => Load();
                var copy = new IconButton("", "copy", ButtonKind.Outline) { IconColor = Ui.NavInk };
                copy.SetMin(Px(36), Px(32));
                copy.Click += (s, e) => { try { Clipboard.SetText(_text.SelectionLength > 0 ? _text.SelectedText : _text.Text); Toast.Show(owner, "Copied.", ToastKind.Info); } catch { } };
                if (sources.Length > 1) bar.Controls.Add(_source);
                bar.Controls.Add(_filter);
                bar.Controls.Add(refresh); bar.Controls.Add(copy);
                bar.Controls.Add(_auto);
                bar.Controls.Add(new Label { UseMnemonic = false, Text = "Auto", Font = Ui.Small, ForeColor = Ui.Muted, AutoSize = true, Margin = new Padding(0, Px(8), 0, 0) });
                _text = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = Ui.LogFont, BackColor = Ui.LogBg, ForeColor = Ui.LogInk,
                                      BorderStyle = BorderStyle.None, Width = Px(680), Height = Px(230), Tag = "keep-colors" };
                Controls.Add(bar, 0, 0);
                Controls.Add(_text, 0, 1);
                _source.SelectedChanged += (s, e) => Load();
                _filter.SelectedChanged += (s, e) => Load();
                _auto.CheckedChanged += (s, e) => _timer.Enabled = _auto.Checked && Visible;
                _timer.Tick += (s, e) => { if (IsHandleCreated && Visible && TopLevelControl != null && TopLevelControl.Visible) Load(); };
                VisibleChanged += (s, e) => { _timer.Enabled = _auto.Checked && Visible; if (Visible) Load(); };
                owner._timers.Add(_timer);
                ParentChanged += (s, e) => { if (Parent != null) Parent.SizeChanged += (a, b) => _text.Width = Math.Max(Px(420), Parent.ClientSize.Width - Parent.Padding.Horizontal - Px(4)); };
                Load();
            }

            void Load()
            {
                string f = _file(_source.SelectedIndex);
                var lines = Sys.Tail(f, 400).Replace("\r\n", "\n").Split('\n').Where(l => l.Length > 0);
                if (_filter.SelectedIndex > 0)
                {
                    bool errorsOnly = _filter.SelectedIndex == 2;
                    lines = lines.Where(l =>
                    {
                        string x = l.ToLowerInvariant();
                        bool err = x.Contains(":error]") || x.Contains(":crit]") || x.Contains(":alert]") || x.Contains(":emerg]") || x.Contains("[error]") || x.Contains(" error ");
                        bool warn = x.Contains(":warn]") || x.Contains("[warning]") || x.Contains("[warn]");
                        return errorsOnly ? err : err || warn;
                    });
                }
                var arr = lines.ToList();
                string text = arr.Count == 0 ? (File.Exists(f) ? "(no matching lines)" : "(" + f + " doesn't exist yet)") : string.Join("\r\n", arr.Skip(Math.Max(0, arr.Count - 300)));
                if (_text.Text == text) return;
                _text.Text = text;
                _text.SelectionStart = text.LastIndexOf('\n') + 1;   // start of the newest line, scrolled fully left
                _text.SelectionLength = 0;
                _text.ScrollToCaret();
            }
        }
    }

    /// <summary>A read-only text window with a filter box (previews, variables, technical details).</summary>
    public static class TextDialog
    {
        public static void Show(IWin32Window owner, string title, string text)
        {
            using (var f = new Form())
            {
                Ui.Style(f);
                f.AutoScaleMode = AutoScaleMode.None;
                f.Text = title;
                f.StartPosition = FormStartPosition.CenterParent;
                f.ShowInTaskbar = false; f.MinimizeBox = false;
                f.ClientSize = new Size(Ui.Px(820), Ui.Px(560));
                f.BackColor = Ui.Bg;
                var box = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = Ui.LogFont, Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, Text = (text ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n") };
                var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = Ui.Px(54), Padding = new Padding(Ui.Px(12), Ui.Px(8), Ui.Px(12), 0), BackColor = Ui.Bg };
                var search = new TextField(Ui.Px(300), false);
                var copy = new IconButton("Copy", "copy", ButtonKind.Outline) { Font = Ui.BtnSmall, Margin = new Padding(Ui.Px(8), 0, 0, 0) };
                copy.SetMin(0, Ui.Px(38));
                copy.Click += (s, e) => { try { Clipboard.SetText(box.Text); } catch { } };
                string all = box.Text;
                search.ValueChanged += (s, e) =>
                {
                    string q = search.Value;
                    box.Text = q.Length == 0 ? all : string.Join("\r\n", all.Split(new[] { "\r\n" }, StringSplitOptions.None).Where(l => l.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0));
                };
                top.Controls.Add(search); top.Controls.Add(copy);
                var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Ui.Px(12)), BackColor = Ui.Bg };
                host.Controls.Add(box);
                f.Controls.Add(host);
                f.Controls.Add(top);
                f.ShowDialog(owner);
            }
        }
    }
}
