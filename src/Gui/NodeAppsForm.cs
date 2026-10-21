using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace WebHut
{
    public class NodeAppsForm : Form
    {
        public bool Changed;
        readonly ListView _list = new ListView { View = View.Details, FullRowSelect = true, Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, MultiSelect = false };
        List<NodeApp> _apps;

        public NodeAppsForm()
        {
            Ui.Style(this);
            Text = "Node apps";
            ClientSize = new Size(820, 440);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            _list.Columns.Add("Name / URL", 200);
            _list.Columns.Add("Port", 60);
            _list.Columns.Add("Folder", 230);
            _list.Columns.Add("Command", 150);
            _list.Columns.Add("Auto-start", 80);
            _list.DoubleClick += (s, e) => Edit();

            var t = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1 };
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var intro = Ui.Lbl("Each app runs its command with PORT set, and Apache serves it at http://<name>.localhost (websockets included).", Ui.Small, Ui.Muted);
            intro.Margin = new Padding(0, 0, 0, 10);
            t.Controls.Add(intro);
            t.Controls.Add(_list);
            var b = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 10, 0, 0), Dock = DockStyle.Fill };
            b.Controls.Add(Ui.Btn("Add…", (s, e) => Add(), true));
            b.Controls.Add(Ui.Btn("Edit…", (s, e) => Edit()));
            b.Controls.Add(Ui.Btn("Remove", (s, e) => Remove()));
            var close = Ui.Btn("Close", (s, e) => Close(), false, 90);
            close.Anchor = AnchorStyles.Right;
            b.Controls.Add(close);
            t.Controls.Add(b);
            Controls.Add(t);
            CancelButton = close;
            Load += (s, e) => Reload();
        }

        void Reload()
        {
            _apps = NodeApp.LoadAll();
            _list.Items.Clear();
            foreach (var a in _apps)
            {
                var i = new ListViewItem(a.Host) { Tag = a };
                i.SubItems.Add(a.Port.ToString());
                i.SubItems.Add(a.Dir);
                i.SubItems.Add(a.Command);
                i.SubItems.Add(a.AutoStart ? "Yes" : "");
                _list.Items.Add(i);
            }
        }

        NodeApp Selected { get { return _list.SelectedItems.Count == 0 ? null : (NodeApp)_list.SelectedItems[0].Tag; } }

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

        void Edit()
        {
            var app = Selected;
            if (app == null) return;
            var svc = new NodeService(app);
            if (svc.IsRunning) { MessageBox.Show(this, "Stop " + app.Name + " before editing it.", "WebHut"); return; }
            string oldName = app.Name;
            using (var f = new NodeAppEditor(app, _apps, oldName))
            {
                if (f.ShowDialog(this) != DialogResult.OK) { Reload(); return; }
                Save();
            }
        }

        void Remove()
        {
            var app = Selected;
            if (app == null) return;
            var svc = new NodeService(app);
            if (svc.IsRunning)
            {
                if (MessageBox.Show(this, app.Name + " is running. Stop and remove it?", "WebHut", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
                try { svc.Stop(); } catch (Exception e) { Log.Error(e.Message); }
            }
            else if (MessageBox.Show(this, "Remove " + app.Name + " from WebHut? Its files are not touched.", "WebHut", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            _apps.Remove(app);
            Save();
        }

        void Save()
        {
            NodeApp.SaveAll(_apps);
            Changed = true;
            Reload();
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
