using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace WebHut
{
    /// <summary>Node apps: a small process manager. Each app is a card row with status, Start/Stop, Open and a ⋯ menu.</summary>
    public class NodeAppsForm : Form
    {
        public bool Changed;

        class AppRow
        {
            public NodeApp App;
            public NodeService Svc;
            public Pill Status;
            public Label Pid, Auto;
            public IconButton Toggle, Open;
            public bool Busy, Failed, WasRunning, StopRequested;
            public string FailReason;
        }

        static int Px(float v) { return Ui.Px(v); }

        List<NodeApp> _apps = new List<NodeApp>();
        readonly List<AppRow> _rows = new List<AppRow>();
        readonly Timer _timer = new Timer { Interval = 2000 };
        readonly ToolTip _tips = new ToolTip { InitialDelay = 400 };
        Panel _listHost;
        TableLayoutPanel _list;
        Label _summary;
        Panel _searchBox;
        TextBox _search;
        bool _refreshing;

        public NodeAppsForm()
        {
            Ui.Style(this);
            AutoScaleMode = AutoScaleMode.None;
            Text = "Node apps";
            var wa = Screen.PrimaryScreen.WorkingArea;
            ClientSize = new Size(Math.Min(Px(1000), wa.Width - Px(40)), Math.Min(Px(650), wa.Height - Px(60)));
            MinimumSize = new Size(Math.Min(Px(820), wa.Width - Px(20)), Math.Min(Px(460), wa.Height - Px(20)));
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            BackColor = Ui.Bg;

            // ---- header: title + summary on the left, search + Add app on the right
            var head = new TableLayoutPanel { Dock = DockStyle.Top, Height = Px(96), ColumnCount = 2, BackColor = Ui.Bg, Padding = new Padding(Px(28), Px(22), Px(28), Px(8)) };
            head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            head.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var titles = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, BackColor = Ui.Bg, Margin = new Padding(0) };
            titles.Controls.Add(new Label { Text = "Node apps", Font = Ui.H2, ForeColor = Ui.Ink, AutoSize = true, Margin = new Padding(0) });
            _summary = new Label { Text = "Manage local Node.js processes and development servers", Font = Ui.Base, ForeColor = Ui.Muted, AutoSize = true, Margin = new Padding(0, Px(2), 0, 0) };
            titles.Controls.Add(_summary);
            var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Ui.Bg, Anchor = AnchorStyles.Right | AnchorStyles.Top, Margin = new Padding(0, Px(4), 0, 0) };
            _searchBox = BuildSearch();
            var add = new IconButton("Add app", "plus", ButtonKind.Primary) { Margin = new Padding(0) };
            add.SetMin(Px(120), Px(40));
            add.Click += (s, e) => Add();
            actions.Controls.Add(_searchBox);
            actions.Controls.Add(add);
            head.Controls.Add(titles, 0, 0);
            head.Controls.Add(actions, 1, 0);

            // ---- list (scrolls)
            _listHost = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Ui.Bg, Padding = new Padding(Px(28), Px(6), Px(28), Px(12)) };
            _list = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, BackColor = Ui.Bg };
            _list.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _listHost.Controls.Add(_list);

            // ---- footer: help on the left, Close on its own on the right
            var footer = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = Px(64), ColumnCount = 2, BackColor = Ui.Card, Padding = new Padding(Px(28), Px(12), Px(28), Px(12)) };
            footer.Paint += (s, e) => { using (var pen = new Pen(Ui.Line)) e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0); };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var help = new IconButton("How Node apps work", "info", ButtonKind.Ghost) { Font = Ui.Small, IconColor = Ui.Muted, Anchor = AnchorStyles.Left, Margin = new Padding(0) };
            help.Click += (s, e) => ShowHelp();
            var close = new IconButton("Close", null, ButtonKind.Outline) { Anchor = AnchorStyles.Right, Margin = new Padding(0) };
            close.SetMin(Px(100), Px(40));
            close.Click += (s, e) => Close();
            footer.Controls.Add(help, 0, 0);
            footer.Controls.Add(close, 1, 0);

            Controls.Add(ScrollHost.Wrap(_listHost));
            Controls.Add(head);
            Controls.Add(footer);
            CancelButton = close;

            _timer.Tick += (s, e) => RefreshStatus();
            Load += (s, e) => { Reload(); _timer.Start(); };
            FormClosed += (s, e) => _timer.Stop();
            _listHost.Resize += (s, e) => FitRows();
        }

        Panel BuildSearch()
        {
            var box = new Panel { Size = new Size(Px(220), Px(40)), BackColor = Ui.Bg, Margin = new Padding(0, 0, Px(10), 0), Padding = new Padding(Px(34), 0, Px(10), 0) };
            box.Paint += (s, e) =>
            {
                var g = e.Graphics; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var p = Ui.Rounded(new RectangleF(0.5f, 0.5f, box.Width - 1.5f, box.Height - 1.5f), Px(8)))
                {
                    using (var b = new SolidBrush(Ui.Card)) g.FillPath(b, p);
                    using (var pen = new Pen(_search != null && _search.Focused ? Ui.AccentSoftLine : Ui.Line)) g.DrawPath(pen, p);
                }
                // magnifier
                using (var pen = new Pen(Ui.Muted, Math.Max(1.5f, Px(1.6f))))
                {
                    float c = Px(12), x = Px(12), y = (box.Height - c) / 2f - Px(1);
                    g.DrawEllipse(pen, x, y, c * 0.75f, c * 0.75f);
                    g.DrawLine(pen, x + c * 0.65f, y + c * 0.65f, x + c, y + c);
                }
            };
            _search = new TextBox { BorderStyle = BorderStyle.None, Font = Ui.Base, BackColor = Ui.Card, ForeColor = Ui.Ink };
            _search.TextChanged += (s, e) => Reload();
            _search.GotFocus += (s, e) => box.Invalidate();
            _search.LostFocus += (s, e) => box.Invalidate();
            box.Controls.Add(_search);
            box.Resize += (s, e) => _search.SetBounds(box.Padding.Left, (box.Height - _search.Height) / 2, box.Width - box.Padding.Horizontal, _search.Height);
            _tips.SetToolTip(_search, "Filter by name, URL, port or command");
            return box;
        }

        // ------------------------------------------------------------------ list

        void Reload()
        {
            _apps = NodeApp.LoadAll();
            var keep = _rows.ToDictionary(r => r.App.Name, r => r);   // keep busy/failed state across rebuilds
            string q = _search == null ? "" : _search.Text.Trim();
            _searchBox.Visible = _apps.Count > 5 || q.Length > 0;

            _list.SuspendLayout();
            foreach (Control c in _list.Controls.Cast<Control>().ToList()) c.Dispose();
            _list.Controls.Clear();
            _list.RowStyles.Clear();
            _list.RowCount = 0;
            _rows.Clear();

            if (_apps.Count == 0) AddToList(EmptyState());
            else
            {
                var shown = _apps.Where(a => q.Length == 0 || (a.Name + " " + a.Host + " " + a.Port + " " + a.Command + " " + a.Dir).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                if (shown.Count == 0) AddToList(new Label { Text = "No apps match \"" + q + "\".", Font = Ui.Base, ForeColor = Ui.Muted, AutoSize = true, Margin = new Padding(Px(4), Px(16), 0, 0) });
                foreach (var a in shown)
                {
                    AppRow old;
                    var r = new AppRow { App = a, Svc = new NodeService(a) };
                    if (keep.TryGetValue(a.Name, out old)) { r.Busy = old.Busy; r.Failed = old.Failed; r.FailReason = old.FailReason; r.WasRunning = old.WasRunning; }
                    AddToList(BuildRow(r));
                    _rows.Add(r);
                }
            }
            _list.ResumeLayout();
            FitRows();
            UpdateSummary(-1);
            RefreshStatus();
        }

        void AddToList(Control c)
        {
            int i = _list.RowCount++;
            _list.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _list.Controls.Add(c, 0, i);
        }

        void FitRows()
        {
            int w = _listHost.ClientSize.Width - _listHost.Padding.Horizontal;
            foreach (Control c in _list.Controls) if (c is Card && c.Width != w) c.Width = w;
        }

        Control EmptyState()
        {
            var card = new Card { Height = Px(320), Width = Px(900), Margin = new Padding(0, Px(8), 0, 0) };
            var stack = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, BackColor = Ui.Card };
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            stack.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            for (int i = 0; i < 5; i++) stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            stack.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            Func<Control, Control> center = c => { c.Anchor = AnchorStyles.None; return c; };
            stack.Controls.Add(new Panel { BackColor = Ui.Card }, 0, 0);
            stack.Controls.Add(center(new IconBox("cube", Ui.AccentInk, Px(40)) { Margin = new Padding(0, 0, 0, Px(12)) }), 0, 1);
            stack.Controls.Add(center(new Label { Text = "No Node apps yet", Font = Ui.SectionTitle, ForeColor = Ui.Ink, AutoSize = true, BackColor = Ui.Card }), 0, 2);
            stack.Controls.Add(center(new Label { Text = "Add a Node.js project to run it locally and reach it at a friendly .localhost address.", Font = Ui.Base, ForeColor = Ui.Muted, AutoSize = true, BackColor = Ui.Card, Margin = new Padding(0, Px(6), 0, Px(16)) }), 0, 3);
            var add = new IconButton("Add your first app", "plus", ButtonKind.Primary);
            add.SetMin(Px(180), Px(42));
            add.Click += (s, e) => Add();
            stack.Controls.Add(center(add), 0, 4);
            stack.Controls.Add(center(new Label { Text = "Example:  myapp.localhost  →  localhost:3000", Font = Ui.Mono, ForeColor = Ui.Faint, AutoSize = true, BackColor = Ui.Card, Margin = new Padding(0, Px(14), 0, 0) }), 0, 5);
            stack.Controls.Add(new Panel { BackColor = Ui.Card }, 0, 6);
            card.Controls.Add(stack);
            return card;
        }

        Control BuildRow(AppRow r)
        {
            var a = r.App;
            var card = new Card { Height = Px(84), Width = Px(900), Margin = new Padding(0, 0, 0, Px(10)), Padding = new Padding(Px(18), Px(10), Px(14), Px(10)) };
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 7, BackColor = Ui.Card };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Px(40)));   // icon
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));      // identity
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Px(140)));  // status
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Px(130)));  // auto-start
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Px(80)));   // pid
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));           // start/stop + open
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));           // ⋯

            t.Controls.Add(new IconBox("cube", Ui.AccentInk, Px(24)) { Anchor = AnchorStyles.Left }, 0, 0);

            var id = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, BackColor = Ui.Card, Anchor = AnchorStyles.Left, Margin = new Padding(0) };
            id.Controls.Add(new Label { Text = a.Name, Font = Ui.ServiceName, ForeColor = Ui.Ink, AutoSize = true, Margin = new Padding(0) });
            id.Controls.Add(new Label { Text = a.Host + "  ·  :" + a.Port, Font = Ui.Mono, ForeColor = Ui.Muted, AutoSize = true, Margin = new Padding(0, Px(3), 0, 0) });
            var cmd = new Label { Text = a.Command, Font = Ui.Mono, ForeColor = Ui.Faint, AutoSize = true, AutoEllipsis = true, MaximumSize = new Size(Px(320), 0), Margin = new Padding(0, Px(2), 0, 0) };
            _tips.SetToolTip(cmd, a.Command + "\n" + a.FullDir);
            id.Controls.Add(cmd);
            t.Controls.Add(id, 1, 0);

            r.Status = new Pill { Anchor = AnchorStyles.Left, Size = new Size(Px(128), Px(30)), Margin = new Padding(0) };
            r.Status.Set(PillState.Stopped, "Stopped");
            t.Controls.Add(r.Status, 2, 0);

            // Auto-start is configuration, not state: plain text, not a switch, so it can't be mistaken for Start/Stop.
            r.Auto = new Label { Text = a.AutoStart ? "Auto-start on" : "Manual start", Font = Ui.Small, ForeColor = a.AutoStart ? Ui.Ink : Ui.Muted, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0) };
            _tips.SetToolTip(r.Auto, a.AutoStart ? "Starts with Start all (and when WebHut opens, if that setting is on). Change it in Edit." : "Only starts when you click Start. Change it in Edit.");
            t.Controls.Add(r.Auto, 3, 0);

            r.Pid = new Label { Text = "PID —", Font = Ui.Mono, ForeColor = Ui.Faint, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0) };
            t.Controls.Add(r.Pid, 4, 0);

            var btns = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Ui.Card, Anchor = AnchorStyles.Right, Margin = new Padding(0) };
            r.Toggle = new IconButton("Start", "play", ButtonKind.Primary) { Font = Ui.BtnSmall };
            r.Toggle.SetMin(Px(88), 0);
            r.Toggle.Click += (s, e) => Toggle(r);
            r.Open = new IconButton("Open", "external", ButtonKind.Outline) { Font = Ui.BtnSmall, IconColor = Ui.NavInk, Margin = new Padding(0) };
            r.Open.Click += (s, e) => Sys.OpenUrl(r.Svc.Url);
            btns.Controls.Add(r.Toggle); btns.Controls.Add(r.Open);
            t.Controls.Add(btns, 5, 0);

            var more = new IconButton("", "more", ButtonKind.Ghost) { IconColor = Ui.Muted, Anchor = AnchorStyles.Right, Margin = new Padding(Px(6), 0, 0, 0) };
            _tips.SetToolTip(more, "More: restart, edit, logs, folder, terminal, remove");
            more.Click += (s, e) => ShowMore(r, more);
            t.Controls.Add(more, 6, 0);

            card.Controls.Add(t);
            return card;
        }

        void ShowMore(AppRow r, Control anchor)
        {
            bool running = r.Svc.IsRunning;
            PopupMenu.Show(anchor, PopupMenu.Side.BelowRight, r.App.Name, new List<PopupItem>
            {
                new PopupItem { Title = "Restart", Icon = "refresh", Enabled = running && !r.Busy, Act = () => Run(r, () => { r.Svc.Stop(); r.Svc.Start(); }, "Restarting…") },
                new PopupItem { Title = "View logs", Icon = "file", Act = () => Sys.OpenInEditor(r.Svc.LogFile) },
                new PopupItem { Title = "Open folder", Icon = "folder", Act = () => Sys.OpenFolder(r.App.FullDir) },
                new PopupItem { Title = "Open terminal here", Icon = "terminal", Act = () => Services.OpenShell(r.App.FullDir) },
                PopupItem.Line(),
                new PopupItem { Title = "Edit…", Description = running ? "Stop the app first" : null, Icon = "gear", Enabled = !running && !r.Busy, Act = () => Edit(r.App) },
                new PopupItem { Title = "Remove…", Icon = "trash", Danger = true, Enabled = !r.Busy, Act = () => Remove(r) }
            }, Ui.Px(250));
        }

        // ------------------------------------------------------------------ status

        void RefreshStatus()
        {
            if (_refreshing || _rows.Count == 0) { UpdateSummary(0); return; }
            _refreshing = true;
            var rows = _rows.ToList();
            bool apache = Services.Apache.IsRunning;
            System.Threading.Tasks.Task.Run(() => rows.Select(r =>
            {
                int pid = 0; bool listening = false;
                try { pid = r.Svc.Pid(); } catch { }
                if (pid > 0) listening = Sys.IsPortListening(r.App.Port);
                return new { r, pid, listening };
            }).ToList()).ContinueWith(t =>
            {
                _refreshing = false;
                if (IsDisposed || t.IsFaulted) return;
                int running = 0;
                foreach (var x in t.Result)
                {
                    var r = x.r;
                    if (r.Status.IsDisposed) continue;
                    bool up = x.pid > 0;
                    if (up) running++;
                    if (r.Busy) continue;

                    // Crash handling: running last time, gone now, and nobody asked it to stop.
                    if (r.WasRunning && !up && !r.StopRequested)
                    {
                        r.Failed = true;
                        r.FailReason = r.App.Name + " exited unexpectedly. Last lines of its log:\n" + Sys.Tail(r.Svc.LogFile, 12);
                        Log.Warn(r.App.Name + " exited unexpectedly. See " + r.Svc.LogFile);
                    }
                    r.WasRunning = up;
                    r.StopRequested = false;

                    if (up && x.listening) { r.Failed = false; r.Status.Set(PillState.Running, "Running"); }
                    else if (up) r.Status.Set(PillState.Partial, "No response");
                    else if (r.Failed) r.Status.Set(PillState.Error, "Failed");
                    else r.Status.Set(PillState.Stopped, "Stopped");
                    _tips.SetToolTip(r.Status, up && !x.listening ? "The process runs, but nothing answers on port " + r.App.Port + ". Make sure the app listens on PORT (e.g. add --port %PORT% to the command)."
                                              : r.Failed ? (r.FailReason ?? "Start failed. See the log.") : "");

                    r.Pid.Text = up ? "PID " + x.pid : "PID —";
                    r.Pid.ForeColor = up ? Ui.Ink : Ui.Faint;
                    r.Toggle.Enabled = true;
                    r.Toggle.Set(up ? "Stop" : "Start", up ? "stop" : "play", ButtonKind.Primary);
                    r.Open.Enabled = up && x.listening && apache;
                    _tips.SetToolTip(r.Open, r.Open.Enabled ? "Open " + r.Svc.Url
                        : !up ? "Start the app first" : !x.listening ? "The app isn't answering on port " + r.App.Port + " yet" : "Start Apache first: it serves " + r.App.Host);
                }
                UpdateSummary(running);
            }, System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext());
        }

        void UpdateSummary(int running)
        {
            if (_apps.Count == 0) { _summary.Text = "Manage local Node.js processes and development servers"; return; }
            if (running < 0) return;
            _summary.Text = _apps.Count + (_apps.Count == 1 ? " app" : " apps") + "   ·   " + running + " running   ·   " + (_apps.Count - running) + " stopped";
        }

        void Toggle(AppRow r)
        {
            bool running = r.Svc.IsRunning;
            if (running) { r.StopRequested = true; Run(r, r.Svc.Stop, "Stopping…"); }
            else
            {
                // Port conflict check before starting, with a clear message instead of a silent failure.
                if (Sys.IsPortListening(r.App.Port))
                {
                    string owner = null;
                    try { owner = Sys.PortOwner(r.App.Port); } catch { }
                    r.Failed = true;
                    r.FailReason = "Port " + r.App.Port + " is already in use" + (string.IsNullOrEmpty(owner) ? "" : " by " + owner) + ". Stop that program or choose another port in Edit.";
                    Ui.Error(this, r.App.Name, r.FailReason);
                    RefreshStatus();
                    return;
                }
                r.Failed = false;
                Run(r, r.Svc.Start, "Starting…");
            }
        }

        void Run(AppRow r, Action act, string busy)
        {
            if (r.Busy) return;
            r.Busy = true;
            r.Toggle.Enabled = false; r.Open.Enabled = false;
            r.Status.Set(PillState.Busy, busy);
            System.Threading.Tasks.Task.Run(act).ContinueWith(t =>
            {
                r.Busy = false;
                if (t.IsFaulted)
                {
                    r.Failed = busy != "Stopping…";
                    r.FailReason = t.Exception.GetBaseException().Message;
                    Log.Error(r.App.Name + ": " + r.FailReason);
                    if (!IsDisposed) Ui.Error(this, r.App.Name, r.FailReason);
                }
                else r.WasRunning = busy != "Stopping…";
                if (!IsDisposed) RefreshStatus();
            }, System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext());
        }

        // ------------------------------------------------------------------ add / edit / remove

        void Add()
        {
            int port = 3000;
            while (_apps.Any(a => a.Port == port) || port == Services.Cfg.HttpPort || port == Services.Cfg.MysqlPort) port++;
            var app = new NodeApp { Port = port };
            using (var f = new NodeAppEditor(app, _apps, null))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                _apps.Add(app);
                Save();
            }
        }

        void Edit(NodeApp app)
        {
            if (app == null) return;
            if (new NodeService(app).IsRunning) { MessageBox.Show(this, "Stop " + app.Name + " before editing it.", "WebHut"); return; }
            using (var f = new NodeAppEditor(app, _apps, app.Name))
            {
                if (f.ShowDialog(this) != DialogResult.OK) { Reload(); return; }
                Save();
            }
        }

        void Remove(AppRow r)
        {
            var app = r.App;
            if (r.Svc.IsRunning)
            {
                if (MessageBox.Show(this, app.Name + " is running. Stop and remove it?\n\nIts files are not touched.", "Remove app", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                r.StopRequested = true;
                try { r.Svc.Stop(); } catch (Exception e) { Log.Error(e.Message); }
            }
            else if (MessageBox.Show(this, "Remove " + app.Name + " from WebHut?\n\nIts files are not touched.", "Remove app", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            _apps.RemoveAll(a => a.Name == app.Name);
            Save();
        }

        void Save()
        {
            NodeApp.SaveAll(_apps);
            Changed = true;
            Reload();
        }

        void ShowHelp()
        {
            MessageBox.Show(this,
                "WebHut runs each app's command in its folder with the PORT environment variable set, and Apache serves it at http://<name>.localhost (websockets included).\n\n" +
                "Some dev servers ignore PORT. Pass it on the command line instead, for example:\n" +
                "   npx vite --port %PORT%\n   next dev -p %PORT%\n\n" +
                "Running means the process is up and answering on its port. No response means the process runs but nothing listens on the port yet.\n\n" +
                "Stop ends the whole process tree (npm and the node processes it started).",
                "How Node apps work", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    public class NodeAppEditor : Form
    {
        readonly NodeApp _app;
        readonly List<NodeApp> _all;
        readonly string _originalName;
        readonly TextBox _name = new TextBox { Width = 220 };
        readonly TextBox _dir = new TextBox { Width = 330 };
        readonly ComboBox _cmd = new ComboBox { Width = 330 };
        readonly NumericUpDown _port = new NumericUpDown { Minimum = 1, Maximum = 65535, Width = 90 };
        readonly CheckBox _auto = new CheckBox { Text = "Start with \"Start all\"", AutoSize = true };
        readonly TextBox _env = new TextBox { Multiline = true, Width = 330, Height = 70, ScrollBars = ScrollBars.Vertical, Font = Ui.Mono };

        public NodeAppEditor(NodeApp app, List<NodeApp> all, string originalName)
        {
            _app = app; _all = all; _originalName = originalName;
            Ui.Style(this);
            Text = originalName == null ? "Add Node app" : "Edit " + originalName;
            ClientSize = new Size(560, 400);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;

            _cmd.Items.AddRange(new object[] { "npm run dev", "npm start", "node server.js", "npx vite", "npm run preview" });

            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(18, 14, 18, 8) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Controls.Add(t);

            var browse = Ui.Btn("Browse…", (s, e) => Browse());
            var dirRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            dirRow.Controls.Add(_dir); dirRow.Controls.Add(browse);
            _dir.Margin = new Padding(0, 4, 6, 0);

            Row(t, "Name", _name, "Lowercase letters, digits, hyphens. URL: http://<name>.localhost");
            Row(t, "Folder", dirRow, "Inside www\\ is easiest; any folder works.");
            Row(t, "Command", _cmd, "Run in that folder. WebHut sets PORT, so the app should listen on process.env.PORT.");
            Row(t, "Port", _port, null);
            Row(t, "", _auto, null);
            Row(t, "Environment", _env, "Optional, one KEY=value per line.");

            var b = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 50, Padding = new Padding(16, 8, 16, 8) };
            var cancel = Ui.Btn("Cancel", null, false, 90); cancel.DialogResult = DialogResult.Cancel;
            var ok = Ui.Btn("Save", (s, e) => SaveIfValid(), true, 90);
            b.Controls.Add(cancel); b.Controls.Add(ok);
            Controls.Add(b);
            AcceptButton = ok; CancelButton = cancel;

            _name.Text = app.Name;
            _dir.Text = app.Dir;
            _cmd.Text = app.Command;
            _port.Value = Math.Max(1, Math.Min(65535, app.Port));
            _auto.Checked = app.AutoStart;
            _env.Text = string.Join(Environment.NewLine, app.Env.Select(kv => kv.Key + "=" + kv.Value));
        }

        static void Row(TableLayoutPanel t, string label, Control c, string hint)
        {
            int r = t.RowCount++;
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var l = Ui.Lbl(label, Ui.Bold); l.Margin = new Padding(0, 8, 8, 0);
            t.Controls.Add(l, 0, r);
            if (hint == null) { c.Margin = new Padding(0, 4, 0, 6); t.Controls.Add(c, 1, r); return; }
            var stack = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 4) };
            c.Margin = new Padding(0, 4, 0, 0);
            stack.Controls.Add(c);
            var h = Ui.Lbl(hint, Ui.Small, Ui.Muted); h.Margin = new Padding(0, 2, 0, 0);
            stack.Controls.Add(h);
            t.Controls.Add(stack, 1, r);
        }

        void Browse()
        {
            using (var d = new FolderBrowserDialog { Description = "Folder that contains the app's package.json", SelectedPath = Directory.Exists(_app.FullDir) ? _app.FullDir : K.Www })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                string p = d.SelectedPath;
                // Store paths inside WebHut as relative, so the folder stays portable.
                if (p.StartsWith(K.Root + "\\", StringComparison.OrdinalIgnoreCase)) p = p.Substring(K.Root.Length + 1).Replace('\\', '/');
                _dir.Text = p;
                if (string.IsNullOrWhiteSpace(_name.Text))
                {
                    var guess = System.Text.RegularExpressions.Regex.Replace(Path.GetFileName(d.SelectedPath).ToLowerInvariant(), "[^a-z0-9-]", "-").Trim('-');
                    if (NodeApp.NameRule.IsMatch(guess)) _name.Text = guess;
                }
            }
        }

        void SaveIfValid()
        {
            string name = _name.Text.Trim().ToLowerInvariant();
            int port = (int)_port.Value;
            string err = null;
            if (!NodeApp.NameRule.IsMatch(name)) err = "Name must be lowercase letters, digits and hyphens (no leading or trailing hyphen).";
            else if (name == "localhost") err = "'localhost' is reserved.";
            else if (_all.Any(a => a != _app && a.Name == name)) err = "Another app is already called '" + name + "'.";
            else if (port == Services.Cfg.HttpPort || port == Services.Cfg.MysqlPort) err = "Port " + port + " is used by Apache or MariaDB.";
            else if (_all.Any(a => a != _app && a.Port == port)) err = "Another app already uses port " + port + ".";
            else if (string.IsNullOrWhiteSpace(_cmd.Text)) err = "Enter the command that starts the app, e.g. npm run dev.";
            else if (string.IsNullOrWhiteSpace(_dir.Text)) err = "Choose the app's folder.";
            if (err == null)
            {
                var test = new NodeApp { Dir = _dir.Text.Trim() };
                if (!Directory.Exists(test.FullDir)) err = "Folder not found: " + test.FullDir;
            }
            if (err != null) { Ui.Error(this, "Node app", err); return; }

            var env = new Dictionary<string, string>();
            foreach (var line in _env.Lines)
            {
                var s = line.Trim();
                if (s.Length == 0 || s.StartsWith("#")) continue;
                int eq = s.IndexOf('=');
                if (eq <= 0) { Ui.Error(this, "Node app", "Environment lines must look like KEY=value: " + s); return; }
                env[s.Substring(0, eq).Trim()] = s.Substring(eq + 1).Trim();
            }

            _app.Name = name;
            _app.Dir = _dir.Text.Trim();
            _app.Command = _cmd.Text.Trim();
            _app.Port = port;
            _app.AutoStart = _auto.Checked;
            _app.Env = env;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
