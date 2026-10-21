using System;
using System.Collections.Generic;
using System.Drawing;
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
            public Label Dot, Name, Detail, Pid, Port;
            public Button Toggle;
        }

        readonly TableLayoutPanel _grid = new TableLayoutPanel();
        readonly RichTextBox _log = Ui.LogBox();
        readonly List<Row> _rows = new List<Row>();
        readonly Timer _timer = new Timer { Interval = 2000 };
        readonly Timer _backupTimer = new Timer { Interval = 10 * 60 * 1000 };
        volatile bool _backingUp;
        readonly NotifyIcon _tray = new NotifyIcon();
        Label _sub;
        bool _exiting, _refreshing, _trayHintShown;
        Button _allBtn;
        ToolStripMenuItem _trayAll;
        bool _anyRunning, _sequenceRunning;

        WebHutConfig Cfg { get { return Services.Cfg; } }

        public MainForm()
        {
            Ui.Style(this);
            Text = "WebHut";
            ClientSize = new Size(940, 620);
            MinimumSize = new Size(780, 520);
            StartPosition = FormStartPosition.CenterScreen;

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(18, 14, 18, 14), BackColor = Ui.Bg };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(root);

            root.Controls.Add(BuildHeader(), 0, 0);
            root.Controls.Add(BuildServicesCard(), 0, 1);
            root.Controls.Add(BuildToolbar(), 0, 2);
            var logTitle = Ui.Lbl("Activity", Ui.Bold, Ui.Muted);
            logTitle.Margin = new Padding(2, 14, 0, 6);
            root.Controls.Add(logTitle, 0, 3);
            var logCard = new Panel { Dock = DockStyle.Fill, BackColor = Ui.Card, Padding = new Padding(10, 8, 6, 8), Margin = new Padding(0) };
            logCard.Paint += (s, e) => { using (var p = new Pen(Ui.Line)) e.Graphics.DrawRectangle(p, 0, 0, logCard.Width - 1, logCard.Height - 1); };
            logCard.Controls.Add(_log);
            root.Controls.Add(logCard, 0, 4);

            BuildTray();
            Log.Line += line => Ui.AppendLog(_log, line);
            _timer.Tick += (s, e) => RefreshStatus();
            _backupTimer.Tick += (s, e) => ScheduledBackup();
            Load += OnLoad;
            FormClosing += OnClosing;
        }

        // ------------------------------------------------------------------ layout

        Control BuildHeader()
        {
            var t = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, Margin = new Padding(0, 0, 0, 12) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var logo = new PictureBox { Image = Ui.DrawLogo(40), Size = new Size(40, 40), Margin = new Padding(0, 2, 12, 0) };
            var titles = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            var title = Ui.Lbl("WebHut", Ui.Title); title.Margin = new Padding(0, 0, 0, 0);
            _sub = Ui.Lbl("", Ui.Small, Ui.Muted); _sub.Margin = new Padding(2, 0, 0, 0);
            titles.Controls.Add(title);
            titles.Controls.Add(_sub);

            var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Right, Margin = new Padding(0, 8, 0, 0) };
            // One button that flips between "Start all" and "Stop all" with the servers' state.
            _allBtn = Ui.Btn("Start all", (s, e) => ToggleAll(), true, 96);
            actions.Controls.Add(_allBtn);

            t.Controls.Add(logo, 0, 0);
            t.Controls.Add(titles, 1, 0);
            t.Controls.Add(actions, 2, 0);
            return t;
        }

        Control BuildServicesCard()
        {
            var card = new Panel { Dock = DockStyle.Top, AutoSize = true, BackColor = Ui.Card, Padding = new Padding(14, 8, 14, 8), Margin = new Padding(0) };
            card.Paint += (s, e) => { using (var p = new Pen(Ui.Line)) e.Graphics.DrawRectangle(p, 0, 0, card.Width - 1, card.Height - 1); };
            _grid.Dock = DockStyle.Top;
            _grid.AutoSize = true;
            _grid.ColumnCount = 6;
            _grid.BackColor = Ui.Card;
            foreach (var w in new[] { 22, 130, -1, 70, 64, 0 })
                _grid.ColumnStyles.Add(w < 0 ? new ColumnStyle(SizeType.Percent, 100)
                                     : w == 0 ? new ColumnStyle(SizeType.AutoSize)
                                     : new ColumnStyle(SizeType.Absolute, w));
            card.Controls.Add(_grid);
            return card;
        }

        Control BuildToolbar()
        {
            var f = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Margin = new Padding(0, 12, 0, 0), WrapContents = true };
            f.Controls.Add(Ui.Btn("Open localhost", (s, e) => Sys.OpenUrl(Services.Apache.Url)));
            f.Controls.Add(Ui.Btn("phpMyAdmin", (s, e) => Sys.OpenUrl(Services.MariaDb.Url)));
            f.Controls.Add(Ui.Btn("www folder", (s, e) => Sys.OpenFolder(K.Www)));
            f.Controls.Add(Ui.Btn("Shell", (s, e) => Services.OpenShell()));
            f.Controls.Add(Ui.Btn("Refresh sites", (s, e) => RefreshSites()));
            f.Controls.Add(Ui.Btn("Node apps…", (s, e) => ShowNodeApps()));
            f.Controls.Add(Ui.Btn("Database…", (s, e) => { using (var d = new DatabaseForm()) d.ShowDialog(this); }));
            f.Controls.Add(Ui.Btn("Settings…", (s, e) => ShowSettings()));
            f.Controls.Add(Ui.Btn("Logs", (s, e) => Sys.OpenFolder(K.Logs)));
            return f;
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
                r.Dot = new Label { Text = "●", ForeColor = Ui.Grey, AutoSize = true, Font = new Font("Segoe UI", 11f), Margin = new Padding(0, 5, 0, 0) };
                r.Name = Ui.Lbl(svc.Title, Ui.Bold);
                r.Detail = Ui.Lbl(svc.Detail, Ui.Small, Ui.Muted);
                r.Detail.AutoEllipsis = true; r.Detail.AutoSize = false; r.Detail.Dock = DockStyle.Fill; r.Detail.Margin = new Padding(0, 9, 8, 0);
                r.Pid = Ui.Lbl("-", Ui.Small, Ui.Muted);
                r.Port = Ui.Lbl(svc.Port.ToString(), Ui.Small, Ui.Muted);
                r.Toggle = Ui.Btn("Start", null, true, 78);
                var row = r;
                r.Toggle.Click += (s, e) => Toggle(row);
                var open = Ui.Btn("Open", (s, e) => Sys.OpenUrl(row.Svc.Url), false, 58);
                var logs = Ui.Btn("Logs", (s, e) => Sys.OpenInEditor(row.Svc.LogFile), false, 58);
                var conf = Ui.Btn("Config", (s, e) => Sys.OpenInEditor(row.Svc.ConfigFile), false, 64);

                var actions = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(0, 4, 0, 4), BackColor = Ui.Card };
                actions.Controls.AddRange(new Control[] { r.Toggle, open, logs, conf });

                int i = _grid.RowCount++;
                _grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                _grid.Controls.Add(r.Dot, 0, i);
                _grid.Controls.Add(r.Name, 1, i);
                _grid.Controls.Add(r.Detail, 2, i);
                _grid.Controls.Add(r.Pid, 3, i);
                _grid.Controls.Add(r.Port, 4, i);
                _grid.Controls.Add(actions, 5, i);
                foreach (Control c in new Control[] { r.Dot, r.Name, r.Pid, r.Port }) c.Margin = new Padding(0, 10, 8, 0);
                r.Dot.Margin = new Padding(0, 8, 0, 0);
                r.Detail.Margin = new Padding(0, 12, 8, 0);
                _rows.Add(r);
            }
            _grid.ResumeLayout();
            RefreshStatus();
        }

        void AddHeaderRow()
        {
            int i = _grid.RowCount++;
            _grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            string[] heads = { "", "Service", "Details", "PID", "Port" };
            for (int c = 0; c < heads.Length; c++)
            {
                var l = Ui.Lbl(heads[c], Ui.Small, Ui.Muted);
                l.Margin = new Padding(0, 6, 0, 0);
                _grid.Controls.Add(l, c, i);
            }
        }

        // ------------------------------------------------------------------ behaviour

        void OnLoad(object sender, EventArgs e)
        {
            Log.Info("WebHut " + K.AppVersion + " · " + K.Root);
            try { new Installer(Cfg).RelocateIfMoved(); } catch (Exception ex) { Log.Error(ex.Message); }
            UpdateSubtitle();
            BuildRows();
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
                            r.Dot.ForeColor = Ui.Amber;
                            continue;
                        }
                        r.Dot.ForeColor = running ? Ui.Green : Ui.Grey;
                        r.Pid.Text = running ? x.pid.ToString() : "-";
                        r.Port.Text = r.Svc.Port.ToString();
                        r.Detail.Text = r.Svc.Detail;
                        r.Toggle.Enabled = true;
                        r.Toggle.Text = running ? "Stop" : "Start";
                        r.Toggle.BackColor = running ? Ui.Card : Ui.Accent;
                        r.Toggle.ForeColor = running ? Ui.Ink : Color.White;
                        r.Toggle.FlatAppearance.BorderColor = running ? Ui.Line : Ui.Accent;
                    }
                    int up = t.Result.Count(x => x.pid > 0);
                    _tray.Text = "WebHut - " + up + " of " + t.Result.Count + " running";
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
            if (row != null) { row.Toggle.Enabled = false; row.Toggle.Text = busyText; row.Dot.ForeColor = Ui.Amber; }
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
            if (_allBtn == null) return;
            if (_sequenceRunning) return;
            _allBtn.Enabled = true;
            _allBtn.Text = _anyRunning ? "Stop all" : "Start all";
            _allBtn.BackColor = _anyRunning ? Ui.Card : Ui.Accent;
            _allBtn.ForeColor = _anyRunning ? Ui.Ink : Color.White;
            _allBtn.FlatAppearance.BorderColor = _anyRunning ? Ui.Line : Ui.Accent;
            _allBtn.FlatAppearance.MouseOverBackColor = _anyRunning ? Color.FromArgb(245, 241, 235) : Ui.AccentHover;
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
            menu.Items.Add("Exit", null, (s, e) => ExitApp());
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
                    _tray.ShowBalloonTip(3000, "WebHut is still running", "Your servers keep running. Right-click the tray icon to exit.", ToolTipIcon.Info);
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
                var answer = MessageBox.Show(this, "Stop all servers before exiting?\n\nYes - stop them\nNo - leave them running in the background",
                    "Exit WebHut", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
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
