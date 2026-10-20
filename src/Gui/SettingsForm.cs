using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Kiln
{
    public class SettingsForm : Form
    {
        public bool RestartApache, RestartMariaDb;

        readonly ComboBox _php = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
        readonly ComboBox _node = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
        readonly NumericUpDown _http = new NumericUpDown { Minimum = 1, Maximum = 65535, Width = 90 };
        readonly NumericUpDown _mysql = new NumericUpDown { Minimum = 1, Maximum = 65535, Width = 90 };
        readonly CheckBox _startOnLaunch = new CheckBox { Text = "Start Apache, MariaDB and auto-start Node apps when Kiln opens", AutoSize = true };
        readonly CheckBox _tray = new CheckBox { Text = "Closing the window keeps Kiln in the system tray", AutoSize = true };
        readonly CheckBox _lan = new CheckBox { Text = "Allow access from other devices on my network (phones, other PCs)", AutoSize = true };
        readonly Label _status = Ui.Lbl("", Ui.Small, Ui.Muted);
        readonly Label _pwState = Ui.Lbl("", Ui.Small, Ui.Muted);
        Button _addPhp, _addNode, _updates, _vc, _pwBtn;
        bool _busy;

        KilnConfig Cfg { get { return Services.Cfg; } }

        public SettingsForm()
        {
            Ui.Style(this);
            Text = "Kiln settings";
            ClientSize = new Size(660, 560);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;

            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Padding = new Padding(20, 16, 20, 12), AutoScroll = true };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Controls.Add(t);

            _addPhp = Ui.Btn("Install another version…", (s, e) => AddPhp());
            _addNode = Ui.Btn("Install latest LTS", (s, e) => AddNode());
            _pwBtn = Ui.Btn("Change…", (s, e) => ChangePassword());
            _updates = Ui.Btn("Check for updates…", (s, e) => CheckUpdates());
            _vc = Ui.Btn("Install / repair Visual C++ runtime", (s, e) => InstallVc());

            AddRow(t, "PHP version", _php, _addPhp);
            AddRow(t, "Node.js version", _node, _addNode);
            AddRow(t, "HTTP port", _http, Ui.Lbl("Apache, default 80", Ui.Small, Ui.Muted));
            AddRow(t, "MySQL port", _mysql, Ui.Lbl("MariaDB, default 3306", Ui.Small, Ui.Muted));
            AddRow(t, "Database root", _pwBtn, _pwState);
            AddRow(t, "", _startOnLaunch, null, 2);
            AddRow(t, "", _tray, null, 2);
            AddRow(t, "Network", _lan, null, 2);
            var lanHint = Ui.Lbl("Sites become reachable at http://<this PC's IP>/... from your Wi-Fi/LAN. phpMyAdmin and the database stay on this PC only. " +
                                 "Only turn this on at home or on networks you trust. Windows may ask to allow Apache through the firewall - choose Private networks.", Ui.Small, Ui.Muted);
            lanHint.MaximumSize = new Size(430, 0);
            AddRow(t, "", lanHint, null, 2);
            AddRow(t, "Components", _updates, _vc);
            AddRow(t, "", _status, null, 2);

            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 52, Padding = new Padding(16, 10, 16, 10), BackColor = Ui.Bg };
            var close = Ui.Btn("Cancel", (s, e) => Close(), false, 100);
            var save = Ui.Btn("Save", (s, e) => SaveAndClose(), true, 100);
            bottom.Controls.Add(close);
            bottom.Controls.Add(save);
            Controls.Add(bottom);
            CancelButton = close;

            Load += (s, e) => Populate();
        }

        static void AddRow(TableLayoutPanel t, string label, Control a, Control b, int span = 1)
        {
            int r = t.RowCount++;
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var l = Ui.Lbl(label, Ui.Bold);
            l.Margin = new Padding(0, 10, 8, 0);
            t.Controls.Add(l, 0, r);
            a.Margin = new Padding(0, 6, 8, 4);
            t.Controls.Add(a, 1, r);
            if (span > 1) t.SetColumnSpan(a, span);
            if (b != null) { b.Margin = new Padding(0, b is Label ? 10 : 6, 0, 4); t.Controls.Add(b, 2, r); }
        }

        void Populate()
        {
            _php.Items.Clear();
            foreach (var v in KilnConfig.InstalledPhp()) _php.Items.Add(v);
            _php.SelectedItem = Cfg.Php;
            if (_php.SelectedIndex < 0 && _php.Items.Count > 0) _php.SelectedIndex = 0;
            _node.Items.Clear();
            foreach (var v in KilnConfig.InstalledNode()) _node.Items.Add(v);
            _node.SelectedItem = Cfg.Node;
            if (_node.SelectedIndex < 0 && _node.Items.Count > 0) _node.SelectedIndex = 0;
            _http.Value = Cfg.HttpPort;
            _mysql.Value = Cfg.MysqlPort;
            _startOnLaunch.Checked = Cfg.StartOnLaunch;
            _tray.Checked = Cfg.MinimizeToTray;
            _lan.Checked = Cfg.LanAccess;
            _pwState.Text = string.IsNullOrEmpty(Cfg.MysqlRootPassword) ? "No password (XAMPP-style)" : "Password set";
            _vc.Visible = true;
        }

        void SaveAndClose()
        {
            if (_busy) return;
            int http = (int)_http.Value, mysql = (int)_mysql.Value;
            if (http == mysql) { Ui.Error(this, "Settings", "HTTP and MySQL ports must differ."); return; }
            var clash = NodeApp.LoadAll().FirstOrDefault(a => a.Port == http || a.Port == mysql);
            if (clash != null) { Ui.Error(this, "Settings", "Port " + clash.Port + " is used by the Node app '" + clash.Name + "'."); return; }

            string php = _php.SelectedItem as string ?? Cfg.Php;
            string node = _node.SelectedItem as string ?? Cfg.Node;
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
            Cfg.Save();
            if (mysqlChanged)
            {
                try { new Installer(Cfg).UpdatePmaConfig(); } catch (Exception e) { Log.Error(e.Message); }
            }
            if (restartNodeHint) Log.Info("Node.js " + node + " selected. Restart running Node apps to use it.");
            Log.Info("Settings saved.");
            Close();
        }

        void SetBusy(bool on, string msg)
        {
            _busy = on;
            _addPhp.Enabled = _addNode.Enabled = _updates.Enabled = _vc.Enabled = _pwBtn.Enabled = !on;
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
                if (t.IsFaulted) Ui.Error(this, "Kiln", t.Exception.GetBaseException().Message);
                Populate();
                if (!t.IsFaulted && done != null) done();
            }, TaskScheduler.FromCurrentSynchronizationContext());
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
                if (t.IsFaulted) { Ui.Error(this, "Kiln", "Couldn't reach windows.php.net: " + t.Exception.GetBaseException().Message); return; }
                var installed = KilnConfig.InstalledPhp();
                var choices = t.Result.Where(b => !installed.Contains(b)).ToList();
                if (choices.Count == 0) { MessageBox.Show(this, "Every supported PHP version is already installed.", "Kiln"); return; }
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
                if (KilnConfig.InstalledNode().Contains(c.Branch) && KilnConfig.FullVersion(K.NodeDir(c.Branch)) == c.Version)
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
            Populate();
        }

        string Choose(string title, string label, List<string> items)
        {
            using (var f = new Form { Text = title, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(320, 120), ShowInTaskbar = false })
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

    /// <summary>Compares installed versions with the latest and updates what the user ticks.</summary>
    public class UpdatesForm : Form
    {
        class Item { public Component C; public string Installed; public Service Owner; }

        readonly ListView _list = new ListView { View = View.Details, CheckBoxes = true, FullRowSelect = true, Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle };
        readonly Label _status = Ui.Lbl("Checking…", Ui.Small, Ui.Muted);
        Button _go;

        public UpdatesForm()
        {
            Ui.Style(this);
            Text = "Component updates";
            ClientSize = new Size(640, 360);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            _list.Columns.Add("Component", 160);
            _list.Columns.Add("Installed", 150);
            _list.Columns.Add("Latest", 150);
            _list.Columns.Add("Note", 160);

            var t = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1 };
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.Controls.Add(_list);
            t.Controls.Add(_status);
            var b = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Anchor = AnchorStyles.Right };
            var close = Ui.Btn("Close", (s, e) => Close(), false, 90);
            _go = Ui.Btn("Update selected", (s, e) => Go(), true, 140);
            _go.Enabled = false;
            b.Controls.Add(close);
            b.Controls.Add(_go);
            t.Controls.Add(b);
            Controls.Add(t);
            Shown += (s, e) => Check();
        }

        void Check()
        {
            var cfg = Services.Cfg;
            Task.Run(() =>
            {
                var items = new List<Item>();
                Func<string, string> inst = dir => System.IO.Directory.Exists(dir) ? KilnConfig.FullVersion(dir) : null;
                items.Add(new Item { C = Resolver.Apache(cfg), Installed = inst(K.Apache), Owner = Services.Apache });
                if (!string.IsNullOrEmpty(cfg.Php))
                    items.Add(new Item { C = Resolver.Php(cfg, cfg.Php), Installed = inst(cfg.PhpDir), Owner = Services.Apache });
                items.Add(new Item { C = Resolver.MariaDb(cfg), Installed = inst(K.MariaDb), Owner = Services.MariaDb });
                items.Add(new Item { C = Resolver.PhpMyAdmin(cfg), Installed = inst(K.Pma) });
                if (!string.IsNullOrEmpty(cfg.Node))
                    items.Add(new Item { C = Resolver.Node(cfg, cfg.Node), Installed = inst(cfg.NodeDir) });
                return items;
            }).ContinueWith(t =>
            {
                if (IsDisposed) return;
                if (t.IsFaulted) { _status.Text = "Check failed: " + t.Exception.GetBaseException().Message; return; }
                int n = 0;
                foreach (var it in t.Result)
                {
                    string note = "";
                    bool missing = it.C.Ready && it.Installed == null;
                    bool newer = it.C.Ready && (missing || KilnConfig.VersionKey(it.C.Version) > KilnConfig.VersionKey(it.Installed));
                    if (it.C.Id == "mariadb" && newer && !missing)
                    {
                        var a = KilnConfig.VersionKey(it.C.Version); var b = KilnConfig.VersionKey(it.Installed);
                        if (a.Major != b.Major || a.Minor != b.Minor) { newer = false; note = "New LTS series: upgrade by hand (see docs)"; }
                    }
                    if (!it.C.Ready) note = "Lookup failed";
                    else if (!newer && note == "") note = "Up to date";
                    var item = new ListViewItem(it.C.Name) { Tag = it, Checked = newer };
                    item.SubItems.Add(it.Installed ?? "-");
                    item.SubItems.Add(it.C.Version ?? "-");
                    item.SubItems.Add(missing ? "Not installed - will install" : newer ? "Update available" : note);
                    if (!newer) item.ForeColor = Ui.Muted;
                    _list.Items.Add(item);
                    if (newer) n++;
                }
                _status.Text = n == 0 ? "Everything is up to date." : n + " update(s) available. Running servers are stopped during the update and started again afterwards.";
                _go.Enabled = n > 0;
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void Go()
        {
            var chosen = _list.Items.Cast<ListViewItem>().Where(i => i.Checked).Select(i => (Item)i.Tag).ToList();
            if (chosen.Count == 0) return;
            _go.Enabled = false;
            var inst = new Installer(Services.Cfg);
            inst.Progress += (m, p) => { try { BeginInvoke(new Action(() => _status.Text = m)); } catch { } };
            Task.Run(() =>
            {
                var owners = chosen.Where(c => c.Owner != null).Select(c => c.Owner).Distinct().Where(s => s.IsRunning).ToList();
                // node.exe can't be replaced while apps run from it.
                if (chosen.Any(c => c.C.Id == "node"))
                    owners.AddRange(Services.All().OfType<NodeService>().Where(s => s.IsRunning).Cast<Service>());
                foreach (var s in owners) s.Stop();
                foreach (var c in chosen) inst.Install(c.C, CancellationToken.None);
                if (chosen.Any(c => c.C.Id == "mariadb")) inst.WriteConfigFiles(false);
                foreach (var s in owners) s.Start();
            }).ContinueWith(t =>
            {
                if (IsDisposed) { if (t.IsFaulted) Log.Error("Update failed: " + t.Exception.GetBaseException().Message); return; }
                if (t.IsFaulted) { _status.Text = "Update failed: " + t.Exception.GetBaseException().Message; Ui.Error(this, "Update", _status.Text); }
                else { _status.Text = "Updated. Previous versions were moved to tmp\\backup."; }
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }
    }
}
