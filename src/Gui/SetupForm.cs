using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Kiln
{
    /// <summary>First-run wizard: pick components, download, install, configure.</summary>
    public class SetupForm : Form
    {
        static readonly string[] Required = { "apache", "php" };

        readonly ListView _list = new ListView();
        readonly NumericUpDown _http = new NumericUpDown { Minimum = 1, Maximum = 65535, Value = 80, Width = 80 };
        readonly NumericUpDown _mysql = new NumericUpDown { Minimum = 1, Maximum = 65535, Value = 3306, Width = 80 };
        readonly TextBox _pw = new TextBox { UseSystemPasswordChar = true, Width = 180 };
        readonly ProgressBar _bar = new ProgressBar { Dock = DockStyle.Fill, Height = 18 };
        readonly Label _status = Ui.Lbl("Looking up the latest versions…", Ui.Small, Ui.Muted);
        readonly RichTextBox _log = Ui.LogBox();
        Label _vcLabel;
        Button _vcBtn, _install, _cancel, _zipBtn, _pageBtn, _retryBtn;
        List<Component> _components = new List<Component>();
        CancellationTokenSource _cts;
        bool _working;
        readonly Action<string> _logHandler;

        public SetupForm()
        {
            Ui.Style(this);
            Text = "Set up Kiln";
            ClientSize = new Size(820, 660);
            MinimumSize = new Size(720, 600);
            StartPosition = FormStartPosition.CenterScreen;

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(20, 16, 20, 16) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Controls.Add(root);
            _status.MaximumSize = new Size(760, 0);

            var title = Ui.Lbl("Set up Kiln", Ui.Title);
            var intro = Ui.Lbl("Kiln downloads the official Windows builds below into " + K.Root + ".\nNothing is installed system-wide; delete the folder to remove everything.", Ui.Base, Ui.Muted);
            intro.Margin = new Padding(0, 4, 0, 12);
            root.Controls.Add(title);
            root.Controls.Add(intro);

            _list.View = View.Details;
            _list.CheckBoxes = true;
            _list.FullRowSelect = true;
            _list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            _list.Dock = DockStyle.Fill;
            _list.Height = 210;
            _list.BorderStyle = BorderStyle.FixedSingle;
            _list.Columns.Add("Component", 170);
            _list.Columns.Add("Version", 160);
            _list.Columns.Add("Status", 420);
            _list.ItemCheck += (s, e) =>
            {
                var c = _list.Items[e.Index].Tag as Component;
                if (c != null && Required.Contains(c.Id) && e.NewValue == CheckState.Unchecked) e.NewValue = CheckState.Checked;
            };
            root.Controls.Add(_list);

            var listButtons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
            _zipBtn = Ui.Btn("Use a zip I downloaded…", (s, e) => PickZip());
            _pageBtn = Ui.Btn("Open download page", (s, e) => { var c = Selected(); if (c != null) Sys.OpenUrl(c.Homepage); });
            _retryBtn = Ui.Btn("Look up again", (s, e) => ResolveAsync());
            listButtons.Controls.AddRange(new Control[] { _zipBtn, _pageBtn, _retryBtn });
            root.Controls.Add(listButtons);

            // VC++ runtime
            var vc = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 10, 0, 0) };
            _vcLabel = Ui.Lbl("");
            _vcBtn = Ui.Btn("Install it (needs admin)", (s, e) => InstallVc());
            vc.Controls.Add(_vcLabel);
            vc.Controls.Add(_vcBtn);
            root.Controls.Add(vc);
            UpdateVc();

            // Options
            var opts = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
            opts.Controls.Add(Ui.Lbl("HTTP port"));
            opts.Controls.Add(_http);
            var l2 = Ui.Lbl("MySQL port"); l2.Margin = new Padding(16, 6, 8, 0);
            opts.Controls.Add(l2);
            opts.Controls.Add(_mysql);
            var l3 = Ui.Lbl("Database root password"); l3.Margin = new Padding(16, 6, 8, 0);
            opts.Controls.Add(l3);
            opts.Controls.Add(_pw);
            root.Controls.Add(opts);
            var pwHint = Ui.Lbl("Leave the password empty for XAMPP-style passwordless root (fine for local-only use).", Ui.Small, Ui.Muted);
            pwHint.Margin = new Padding(0, 2, 0, 8);
            root.Controls.Add(pwHint);

            root.Controls.Add(_status);
            root.Controls.Add(_bar);
            var logCard = new Panel { Dock = DockStyle.Fill, BackColor = Ui.Card, Padding = new Padding(8, 6, 4, 6), Height = 110, Margin = new Padding(0, 8, 0, 0) };
            logCard.Controls.Add(_log);
            root.Controls.Add(logCard);

            var bottom = new FlowLayoutPanel { AutoSize = true, Anchor = AnchorStyles.Right, FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(0, 12, 0, 0) };
            _cancel = Ui.Btn("Cancel", (s, e) => CancelOrClose(), false, 100);
            _install = Ui.Btn("Install", (s, e) => InstallAsync(), true, 120);
            _install.Enabled = false;
            bottom.Controls.Add(_cancel);
            bottom.Controls.Add(_install);
            root.Controls.Add(bottom);

            for (int i = 0; i < root.Controls.Count; i++) root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles[root.Controls.GetChildIndex(logCard)] = new RowStyle(SizeType.Percent, 100);
            root.RowStyles[root.Controls.GetChildIndex(_list)] = new RowStyle(SizeType.Absolute, 220);

            _logHandler = line => Ui.AppendLog(_log, line);
            Log.Line += _logHandler;
            FormClosed += (s, e) => Log.Line -= _logHandler;
            FormClosing += (s, e) => { if (_working && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; CancelOrClose(); } };
            Shown += (s, e) => ResolveAsync();
        }

        Component Selected()
        {
            return _list.SelectedItems.Count == 0 ? null : _list.SelectedItems[0].Tag as Component;
        }

        void UpdateVc()
        {
            var v = Installer.VcRedistVersion();
            bool ok = Installer.VcRedistOk();
            _vcLabel.Text = ok
                ? "✓ Visual C++ Redistributable " + v.Major + "." + v.Minor + " is installed."
                : "⚠ Visual C++ Redistributable 2017-2026 (x64) is " + (v == null ? "missing" : "too old (" + v.Major + "." + v.Minor + ")") + ". Apache and PHP need it.";
            _vcLabel.ForeColor = ok ? Ui.Green : Ui.Amber;
            _vcBtn.Visible = !ok;
        }

        void ResolveAsync()
        {
            _install.Enabled = false;
            _retryBtn.Enabled = false;
            _status.Text = "Looking up the latest versions…";
            _bar.Style = ProgressBarStyle.Marquee;
            var cfg = Services.Cfg;
            var previous = _components.Where(c => c.Id != null && c.LocalFile != null).GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First().LocalFile);
            Task.Run(() => Setup.ResolveAll(cfg)).ContinueWith(t =>
            {
                if (IsDisposed) return;
                _bar.Style = ProgressBarStyle.Continuous;
                _retryBtn.Enabled = true;
                if (t.IsFaulted) { _status.Text = "Lookup failed: " + t.Exception.GetBaseException().Message; return; }
                _components = t.Result;
                foreach (var c in _components)
                {
                    string local;
                    if (c.Id != null && previous.TryGetValue(c.Id, out local) && local != null) c.LocalFile = local;
                }
                FillList();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void FillList()
        {
            _list.Items.Clear();
            foreach (var c in _components)
            {
                var item = new ListViewItem(c.Name) { Tag = c, Checked = true };
                item.SubItems.Add(c.Version ?? "-");
                item.SubItems.Add(StatusText(c));
                if (!c.Ready) item.ForeColor = Ui.Red;
                _list.Items.Add(item);
            }
            bool ok = _components.Where(c => Required.Contains(c.Id)).All(c => c.Ready);
            _install.Enabled = ok;
            int bad = _components.Count(c => !c.Ready);
            _status.Text = bad == 0
                ? "Ready to install " + _components.Count + " components."
                : bad + " component(s) couldn't be found automatically. Select one and use \"Open download page\", then \"Use a zip I downloaded…\". Optional ones can be unticked.";
        }

        static string StatusText(Component c)
        {
            if (c.LocalFile != null) return "Using " + Path.GetFileName(c.LocalFile);
            if (c.Error != null) return "Not found: " + c.Error;
            return (c.Sha256 != null ? "Checksum available · " : "") + c.FileName;
        }

        void PickZip()
        {
            var c = Selected();
            if (c == null) { MessageBox.Show(this, "Select a component in the list first.", "Kiln"); return; }
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
                FillList();
            }
        }

        void InstallVc()
        {
            _vcBtn.Enabled = false;
            var inst = new Installer(Services.Cfg);
            inst.Progress += OnProgress;
            Task.Run(() => inst.InstallVcRedist(CancellationToken.None)).ContinueWith(t =>
            {
                if (IsDisposed) return;
                _vcBtn.Enabled = true;
                if (t.IsFaulted) Ui.Error(this, "Visual C++ Redistributable", t.Exception.GetBaseException().Message);
                UpdateVc();
                _status.Text = "";
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void OnProgress(string msg, int pct)
        {
            if (InvokeRequired) { BeginInvoke(new Action(() => OnProgress(msg, pct))); return; }
            _status.Text = msg;
            if (pct < 0) _bar.Style = ProgressBarStyle.Marquee;
            else { _bar.Style = ProgressBarStyle.Continuous; _bar.Value = Math.Max(0, Math.Min(100, pct)); }
        }

        void InstallAsync()
        {
            var selected = _list.Items.Cast<ListViewItem>().Where(i => i.Checked).Select(i => (Component)i.Tag).ToList();
            var notReady = selected.Where(c => !c.Ready).ToList();
            if (notReady.Count > 0)
            {
                Ui.Error(this, "Kiln", "These still need a zip or must be unticked:\n" + string.Join("\n", notReady.Select(c => "• " + c.Name)));
                return;
            }
            if ((int)_http.Value == (int)_mysql.Value) { Ui.Error(this, "Kiln", "HTTP and MySQL ports must differ."); return; }
            if (!Installer.VcRedistOk())
            {
                var a = MessageBox.Show(this, "The Visual C++ Redistributable is missing or old, so Apache probably won't start.\n\nContinue anyway? (You can install it later from Settings.)",
                    "Kiln", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (a != DialogResult.Yes) return;
            }

            var cfg = Services.Cfg;
            cfg.HttpPort = (int)_http.Value;
            cfg.MysqlPort = (int)_mysql.Value;
            cfg.MysqlRootPassword = _pw.Text;

            SetWorking(true);
            _cts = new CancellationTokenSource();
            var inst = new Installer(cfg);
            inst.Progress += OnProgress;
            var ct = _cts.Token;
            Task.Run(() => Setup.Run(inst, selected, ct)).ContinueWith(t =>
            {
                if (IsDisposed) return;
                SetWorking(false);
                _bar.Style = ProgressBarStyle.Continuous;
                if (t.IsCanceled || (t.IsFaulted && t.Exception.GetBaseException() is OperationCanceledException))
                {
                    _status.Text = "Cancelled. Downloads finished so far are cached in tmp\\downloads.";
                    return;
                }
                if (t.IsFaulted)
                {
                    var msg = t.Exception.GetBaseException().Message;
                    _status.Text = "Setup failed: " + msg;
                    Log.Error(msg);
                    Ui.Error(this, "Setup failed", msg + "\n\nFix the problem and click Install again - finished downloads are reused.");
                    return;
                }
                _bar.Value = 100;
                _status.Text = "Done!";
                MessageBox.Show(this, "Kiln is ready.\n\nClick \"Start all\" in the next window, then open http://localhost.", "Kiln", MessageBoxButtons.OK, MessageBoxIcon.Information);
                DialogResult = DialogResult.OK;
                Close();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void SetWorking(bool on)
        {
            _working = on;
            _install.Enabled = !on;
            _list.Enabled = !on;
            _zipBtn.Enabled = _pageBtn.Enabled = _retryBtn.Enabled = !on;
            _http.Enabled = _mysql.Enabled = _pw.Enabled = !on;
            _cancel.Text = on ? "Stop" : "Cancel";
        }

        void CancelOrClose()
        {
            if (_working) { if (_cts != null) _cts.Cancel(); _status.Text = "Stopping…"; return; }
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }
}
