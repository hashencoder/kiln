using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WebHut
{
    /// <summary>Backups, restore, rebuild-from-backup, and import from XAMPP.</summary>
    public class DatabaseForm : Form
    {
        readonly ListView _list = new ListView { View = View.Details, FullRowSelect = true, MultiSelect = false, Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle };
        readonly CheckBox _auto = new CheckBox { Text = "Back up automatically every", AutoSize = true };
        readonly NumericUpDown _hours = new NumericUpDown { Minimum = 1, Maximum = 168, Width = 56 };
        readonly NumericUpDown _keep = new NumericUpDown { Minimum = 1, Maximum = 200, Width = 56 };
        readonly Label _status = Ui.Lbl("", Ui.Small, Ui.Muted);
        readonly List<Button> _actions = new List<Button>();

        WebHutConfig Cfg { get { return Services.Cfg; } }

        public DatabaseForm()
        {
            Ui.Style(this);
            Text = "Database";
            ClientSize = new Size(720, 500);
            MinimumSize = new Size(640, 440);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;

            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(18, 14, 18, 14) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Controls.Add(t);

            var h1 = Ui.Lbl("Backups", Ui.Bold);
            var intro = Ui.Lbl("WebHut backs up your databases while MariaDB runs and before it stops. If a crash or power cut ever damages the database, " +
                               "\"Rebuild from backup\" sets the damaged files aside and restores the newest backup.", Ui.Small, Ui.Muted);
            intro.MaximumSize = new Size(670, 0);
            intro.Margin = new Padding(0, 2, 0, 8);
            t.Controls.Add(h1);
            t.Controls.Add(intro);

            var autoRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 8) };
            _auto.Margin = new Padding(0, 6, 4, 0);
            autoRow.Controls.Add(_auto);
            autoRow.Controls.Add(_hours);
            autoRow.Controls.Add(Ui.Lbl("hours, keep the newest"));
            autoRow.Controls.Add(_keep);
            autoRow.Controls.Add(Ui.Lbl("backups"));
            t.Controls.Add(autoRow);

            _list.Columns.Add("Backup", 330);
            _list.Columns.Add("File", 300);
            t.Controls.Add(_list);

            var row = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
            row.Controls.Add(Act(Ui.Btn("Back up now", (s, e) => BackupNow(), true)));
            row.Controls.Add(Act(Ui.Btn("Restore selected…", (s, e) => RestoreSelected())));
            row.Controls.Add(Act(Ui.Btn("Rebuild from backup…", (s, e) => RebuildSelected())));
            row.Controls.Add(Ui.Btn("Open folder", (s, e) => Sys.OpenFolder(K.Backups)));
            t.Controls.Add(row);

            var h2 = Ui.Lbl("Moving from XAMPP", Ui.Bold);
            h2.Margin = new Padding(0, 18, 0, 0);
            t.Controls.Add(h2);
            var x = Ui.Lbl("Copy your XAMPP databases (and htdocs projects) into WebHut. XAMPP itself isn't changed, and this works even when XAMPP's MySQL won't start.", Ui.Small, Ui.Muted);
            x.MaximumSize = new Size(670, 0);
            x.Margin = new Padding(0, 2, 0, 6);
            t.Controls.Add(x);
            t.Controls.Add(Act(Ui.Btn("Import from XAMPP…", (s, e) => { using (var f = new XamppImportForm()) f.ShowDialog(this); Reload(); })));

            t.Controls.Add(_status);

            for (int i = 0; i < t.Controls.Count; i++) t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles[t.Controls.GetChildIndex(_list)] = new RowStyle(SizeType.Percent, 100);

            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 50, Padding = new Padding(16, 8, 16, 8) };
            var close = Ui.Btn("Close", (s, e) => Close(), false, 90);
            bottom.Controls.Add(close);
            Controls.Add(bottom);
            CancelButton = close;

            Load += (s, e) =>
            {
                _auto.Checked = Cfg.AutoBackup;
                _hours.Value = Math.Max(1, Math.Min(168, Cfg.BackupEveryHours));
                _keep.Value = Math.Max(1, Math.Min(200, Cfg.BackupKeep));
                Reload();
            };
            FormClosing += (s, e) => SaveSettings();
        }

        Button Act(Button b) { _actions.Add(b); return b; }

        void SaveSettings()
        {
            if (Cfg.AutoBackup == _auto.Checked && Cfg.BackupEveryHours == (int)_hours.Value && Cfg.BackupKeep == (int)_keep.Value) return;
            Cfg.AutoBackup = _auto.Checked;
            Cfg.BackupEveryHours = (int)_hours.Value;
            Cfg.BackupKeep = (int)_keep.Value;
            Cfg.Save();
            Log.Info("Backup settings saved.");
        }

        void Reload()
        {
            _list.Items.Clear();
            foreach (var b in Backup.List())
            {
                var item = new ListViewItem(b.Label) { Tag = b };
                item.SubItems.Add(Path.GetFileName(b.Path));
                _list.Items.Add(item);
            }
            if (_list.Items.Count > 0) _list.Items[0].Selected = true;
            _status.Text = _list.Items.Count == 0 ? "No backups yet." : _list.Items.Count + " backup(s) in data\\backups.";
        }

        Backup.Info Selected { get { return _list.SelectedItems.Count == 0 ? null : (Backup.Info)_list.SelectedItems[0].Tag; } }

        void RunBg(string msg, Action act, string done)
        {
            foreach (var b in _actions) b.Enabled = false;
            _status.Text = msg;
            Cursor = Cursors.AppStarting;
            Task.Run(act).ContinueWith(t =>
            {
                if (IsDisposed) { if (t.IsFaulted) Log.Error(t.Exception.GetBaseException().Message); return; }
                foreach (var b in _actions) b.Enabled = true;
                Cursor = Cursors.Default;
                Reload();
                if (t.IsFaulted)
                {
                    _status.Text = "Failed.";
                    Ui.Error(this, "Database", t.Exception.GetBaseException().Message);
                }
                else _status.Text = done;
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void BackupNow()
        {
            RunBg("Backing up…", () =>
            {
                if (!Services.MariaDb.IsRunning) Services.MariaDb.Start();
                Backup.Create(Cfg, "manual");
            }, "Backup saved.");
        }

        void RestoreSelected()
        {
            var b = Selected;
            if (b == null) return;
            var ok = MessageBox.Show(this, "Restore the backup from " + b.Time.ToString("g") + "?\n\nTables in the backup replace the current tables with the same name. " +
                "Databases and tables that aren't in the backup are left alone. A backup of the current state is taken first.",
                "Restore backup", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (ok != DialogResult.OK) return;
            RunBg("Restoring…", () =>
            {
                if (!Services.MariaDb.IsRunning) Services.MariaDb.Start();
                try { Backup.Create(Cfg, "before restore"); } catch (Exception e) { Log.Warn("Pre-restore backup skipped: " + e.Message); }
                Backup.Restore(Cfg, b.Path);
            }, "Restored.");
        }

        void RebuildSelected()
        {
            var b = Selected;
            if (b == null) { MessageBox.Show(this, "There are no backups yet.", "WebHut"); return; }
            var ok = MessageBox.Show(this, "Use this when MariaDB won't start after a crash.\n\n" +
                "WebHut will stop MariaDB, move data\\mariadb aside to data\\mariadb-damaged-<date> (nothing is deleted), " +
                "create a fresh database folder and load the backup from " + b.Time.ToString("g") + ".\n\n" +
                "Changes made after that backup are not in the rebuilt database.",
                "Rebuild from backup", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
            if (ok != DialogResult.OK) return;
            RunBg("Rebuilding…", () => Backup.Rebuild(Cfg, b.Path), "Rebuilt from backup.");
        }
    }

    public class XamppImportForm : Form
    {
        readonly TextBox _path = new TextBox { Width = 380 };
        readonly CheckedListBox _dbs = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, BorderStyle = BorderStyle.FixedSingle, IntegralHeight = false };
        readonly CheckedListBox _projects = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, BorderStyle = BorderStyle.FixedSingle, IntegralHeight = false };
        readonly CheckBox _damaged = new CheckBox { Text = "XAMPP's MySQL is damaged (won't start) - try recovery mode", AutoSize = true };
        readonly Label _status = Ui.Lbl("", Ui.Small, Ui.Muted);
        Button _scan, _import;
        bool _busy;

        public XamppImportForm()
        {
            Ui.Style(this);
            Text = "Import from XAMPP";
            ClientSize = new Size(700, 540);
            MinimumSize = new Size(620, 480);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;

            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(18, 14, 18, 8) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            Controls.Add(t);

            var steps = Ui.Lbl("1. Stop MySQL in the XAMPP Control Panel.   2. Scan.   3. Tick what to copy.   4. Import.\n" +
                               "XAMPP's files are never moved or deleted; WebHut makes copies.", Ui.Small, Ui.Muted);
            steps.MaximumSize = new Size(660, 0);
            t.Controls.Add(steps, 0, 0); t.SetColumnSpan(steps, 2);

            var pathRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 10, 0, 4) };
            pathRow.Controls.Add(Ui.Lbl("XAMPP folder"));
            pathRow.Controls.Add(_path);
            _path.Margin = new Padding(0, 4, 6, 0);
            pathRow.Controls.Add(Ui.Btn("Browse…", (s, e) => Browse()));
            _scan = Ui.Btn("Scan", (s, e) => Scan(), true);
            pathRow.Controls.Add(_scan);
            t.Controls.Add(pathRow, 0, 1); t.SetColumnSpan(pathRow, 2);

            _damaged.Margin = new Padding(0, 2, 0, 8);
            t.Controls.Add(_damaged, 0, 2); t.SetColumnSpan(_damaged, 2);

            t.Controls.Add(Ui.Lbl("Databases", Ui.Bold), 0, 3);
            t.Controls.Add(Ui.Lbl("htdocs projects (copied into www\\)", Ui.Bold), 1, 3);
            _dbs.Margin = new Padding(0, 4, 8, 0);
            _projects.Margin = new Padding(8, 4, 0, 0);
            t.Controls.Add(_dbs, 0, 4);
            t.Controls.Add(_projects, 1, 4);
            _status.MaximumSize = new Size(660, 0);
            _status.Margin = new Padding(0, 10, 0, 0);
            t.Controls.Add(_status, 0, 5); t.SetColumnSpan(_status, 2);

            for (int i = 0; i < 6; i++) t.RowStyles.Add(new RowStyle(i == 4 ? SizeType.Percent : SizeType.AutoSize, 100));

            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 50, Padding = new Padding(16, 8, 16, 8) };
            var close = Ui.Btn("Close", (s, e) => Close(), false, 90);
            _import = Ui.Btn("Import", (s, e) => Import(), true, 110);
            _import.Enabled = false;
            bottom.Controls.Add(close);
            bottom.Controls.Add(_import);
            Controls.Add(bottom);
            CancelButton = close;

            FormClosing += (s, e) => { if (_busy) { e.Cancel = true; _status.Text = "Please wait until the import finishes."; } };
            Load += (s, e) =>
            {
                _path.Text = XamppImport.Find() ?? @"C:\xampp";
                _status.Text = XamppImport.IsXampp(_path.Text) ? "Found XAMPP. Stop its MySQL, then click Scan." : "Choose your XAMPP folder, then click Scan.";
            };
        }

        void Browse()
        {
            using (var d = new FolderBrowserDialog { Description = "Your XAMPP folder (the one containing mysql and htdocs)" })
                if (d.ShowDialog(this) == DialogResult.OK) _path.Text = d.SelectedPath;
        }

        void SetBusy(bool on, string msg)
        {
            _busy = on;
            _scan.Enabled = _import.Enabled = !on;
            _path.Enabled = _damaged.Enabled = _dbs.Enabled = _projects.Enabled = !on;
            Cursor = on ? Cursors.AppStarting : Cursors.Default;
            if (msg != null) _status.Text = msg;
        }

        XamppImport NewImport()
        {
            var x = new XamppImport(_path.Text.Trim());
            x.Progress += m => { try { BeginInvoke(new Action(() => _status.Text = m)); } catch { } };
            return x;
        }

        void Scan()
        {
            string root = _path.Text.Trim();
            if (!XamppImport.IsXampp(root)) { Ui.Error(this, "Import from XAMPP", "That folder doesn't look like XAMPP (no mysql\\data and mysql\\bin\\mysqld.exe)."); return; }
            if (!Installer.VcRedistOk()) Log.Warn("Visual C++ runtime looks old; XAMPP's MySQL may need it too.");
            bool recovery = _damaged.Checked;
            SetBusy(true, "Scanning…");
            List<string> dbs = null, projects = null;
            int level = 0;
            Task.Run(() =>
            {
                using (var x = NewImport())
                {
                    projects = x.HtdocsProjects();
                    level = x.StartServerWithRecovery(recovery);
                    dbs = x.Databases();
                }
            }).ContinueWith(t =>
            {
                if (IsDisposed) return;
                SetBusy(false, null);
                if (t.IsFaulted)
                {
                    _status.Text = "Scan failed.";
                    Ui.Error(this, "Import from XAMPP", t.Exception.GetBaseException().Message);
                    return;
                }
                _dbs.Items.Clear();
                foreach (var d in dbs) _dbs.Items.Add(d, !d.Equals("test", StringComparison.OrdinalIgnoreCase));
                _projects.Items.Clear();
                foreach (var p in projects) _projects.Items.Add(p, !Directory.Exists(Path.Combine(K.Www, p)));
                _import.Enabled = dbs.Count + projects.Count > 0;
                _status.Text = "Found " + dbs.Count + " database(s) and " + projects.Count + " project(s)" +
                               (level > 0 ? " (XAMPP's MySQL only started in recovery mode " + level + "; some recent changes may be missing)" : "") + ".";
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void Import()
        {
            var dbs = _dbs.CheckedItems.Cast<string>().ToList();
            var projects = _projects.CheckedItems.Cast<string>().ToList();
            if (dbs.Count + projects.Count == 0) return;
            string existing = "";
            try
            {
                if (dbs.Count > 0 && Services.MariaDb.IsRunning)
                {
                    var clash = Backup.Databases(Services.Cfg).Intersect(dbs, StringComparer.OrdinalIgnoreCase).ToList();
                    if (clash.Count > 0) existing = "\n\nWebHut already has: " + string.Join(", ", clash) + ". Their tables will be replaced by XAMPP's (WebHut backs them up first).";
                }
            }
            catch { }
            var ok = MessageBox.Show(this, "Copy " + dbs.Count + " database(s) and " + projects.Count + " project(s) from XAMPP into WebHut?" + existing,
                "Import from XAMPP", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (ok != DialogResult.OK) return;
            bool recovery = _damaged.Checked;
            SetBusy(true, "Importing…");
            Task.Run(() => { using (var x = NewImport()) x.Run(Services.Cfg, dbs, recovery, projects); })
                .ContinueWith(t =>
                {
                    if (IsDisposed) return;
                    SetBusy(false, null);
                    if (t.IsFaulted)
                    {
                        _status.Text = "Import failed.";
                        Ui.Error(this, "Import from XAMPP", t.Exception.GetBaseException().Message);
                        return;
                    }
                    _status.Text = "Done. Databases are in WebHut's MariaDB; projects are in www\\. Click \"Refresh sites\" in WebHut, " +
                                   "and update database passwords in your apps if they used XAMPP users other than root.";
                    MessageBox.Show(this, "Import complete.", "Import from XAMPP", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }, TaskScheduler.FromCurrentSynchronizationContext());
        }
    }
}
