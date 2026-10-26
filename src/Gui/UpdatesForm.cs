using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ServNook
{
    /// <summary>
    /// Component update manager: compares installed and latest versions, lets the user pick eligible
    /// updates, explains what each one affects, and shows per-component progress and results.
    /// The update rules themselves live in <see cref="Updates"/>.
    /// </summary>
    public class UpdatesForm : Form
    {
        static int Px(float v) { return Ui.Px(v); }

        readonly Label _sub, _summary;
        readonly LinkLabel _openLog;
        readonly IconButton _recheck, _selectAll, _clear, _close, _go;
        readonly Panel _body;
        readonly Panel _placeholder;
        readonly Label _placeholderText;
        readonly ProgressLine _placeholderBar;
        readonly List<UpdRow> _rows = new List<UpdRow>();
        readonly ToolTip _tips = new ToolTip();
        bool _checking, _running;
        CancellationTokenSource _cts;

        ServNookConfig Cfg { get { return Services.Cfg; } }

        /// <summary>The version lookup (replaceable for previews and tests).</summary>
        public static Func<ServNookConfig, List<UpdateItem>> CheckFunc = Updates.Check;
        /// <summary>The installer run (replaceable for previews and tests).</summary>
        public static Action<List<UpdateItem>, ServNookConfig, Action<UpdateItem>, CancellationToken> RunFunc = Updates.Run;

        public UpdatesForm()
        {
            Ui.Style(this);
            AutoScaleMode = AutoScaleMode.None;
            Text = "Component updates";
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            BackColor = Ui.Bg;
            var wa = Screen.PrimaryScreen.WorkingArea;
            MinimumSize = new Size(Math.Min(Px(640), wa.Width), Math.Min(Px(400), wa.Height));
            ClientSize = new Size(Math.Min(Px(800), wa.Width - Px(40)), Math.Min(Px(520), wa.Height - Px(60)));

            // ---- header
            var head = new TableLayoutPanel { Dock = DockStyle.Top, Height = Px(84), ColumnCount = 2, BackColor = Ui.Bg, Padding = new Padding(Px(24), Px(18), Px(24), Px(6)) };
            head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            head.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var titles = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, BackColor = Color.Transparent, Margin = new Padding(0) };
            titles.Controls.Add(new Label { Text = "Component updates", Font = Ui.H2, ForeColor = Ui.Ink, AutoSize = true, Margin = new Padding(0) });
            _sub = new Label { Text = "", Font = Ui.Small, ForeColor = Ui.Muted, AutoSize = true, Margin = new Padding(0, Px(4), 0, 0) };
            titles.Controls.Add(_sub);
            _recheck = new IconButton("Check again", "refresh", ButtonKind.Outline) { Font = Ui.BtnSmall, IconColor = Ui.NavInk, Anchor = AnchorStyles.Right | AnchorStyles.Top, Margin = new Padding(0, Px(4), 0, 0) };
            _recheck.SetMin(0, Px(36));
            _recheck.Click += (s, e) => Check();
            _tips.SetToolTip(_recheck, "Look up the latest versions again");
            head.Controls.Add(titles, 0, 0);
            head.Controls.Add(_recheck, 1, 0);

            // ---- body: rows of components
            _body = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Ui.Bg, Padding = new Padding(Px(24), Px(6), Px(24), Px(10)) };
            _body.Layout += (s, e) => LayoutRows();
            _placeholder = new Panel { BackColor = Color.Transparent, Height = Px(90) };
            _placeholderText = new Label { Text = "Checking for updates…", Font = Ui.Base, ForeColor = Ui.Muted, AutoSize = true, Location = new Point(0, Px(18)) };
            _placeholderBar = new ProgressLine { Location = new Point(0, Px(52)) };
            _placeholder.Controls.Add(_placeholderText);
            _placeholder.Controls.Add(_placeholderBar);
            _body.Controls.Add(_placeholder);

            // ---- summary line + footer
            var sumBar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, BackColor = Ui.Bg, Padding = new Padding(Px(24), 0, Px(24), Px(8)), WrapContents = true };
            _summary = new Label { Text = "", Font = Ui.Small, ForeColor = Ui.Muted, AutoSize = true, Margin = new Padding(0, 0, Px(10), 0) };
            _openLog = new LinkLabel { Text = "Open log", Font = Ui.Small, AutoSize = true, Visible = false, LinkColor = Ui.AccentInk, ActiveLinkColor = Ui.AccentHover, LinkBehavior = LinkBehavior.HoverUnderline, Margin = new Padding(0) };
            _openLog.LinkClicked += (s, e) => Sys.OpenInEditor(System.IO.Path.Combine(K.Logs, "servnook.log"));
            sumBar.Controls.Add(_summary);
            sumBar.Controls.Add(_openLog);
            sumBar.SizeChanged += (s, e) => _summary.MaximumSize = new Size(Math.Max(Px(200), sumBar.Width - Px(140)), 0);

            var footer = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = Px(68), ColumnCount = 5, BackColor = Ui.Card, Padding = new Padding(Px(20), Px(14), Px(24), Px(14)) };
            footer.Paint += (s, e) => { using (var pen = new Pen(Ui.Line)) e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0); };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _selectAll = new IconButton("Select all updates", null, ButtonKind.Ghost) { Font = Ui.BtnSmall, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, Px(4), 0) };
            _selectAll.SetMin(0, Px(38));
            _selectAll.Click += (s, e) => { foreach (var r in _rows.Where(r => r.Check.Enabled)) r.Check.Checked = true; };
            _clear = new IconButton("Clear selection", null, ButtonKind.Ghost) { Font = Ui.BtnSmall, Anchor = AnchorStyles.Left, Margin = new Padding(0) };
            _clear.SetMin(0, Px(38));
            _clear.Click += (s, e) => { foreach (var r in _rows) r.Check.Checked = false; };
            _close = new IconButton("Close", null, ButtonKind.Outline) { Anchor = AnchorStyles.Right, Margin = new Padding(0, 0, Px(10), 0) };
            _close.SetMin(Px(96), Px(40));
            _close.Click += (s, e) => { if (_running) CancelRemaining(); else Close(); };
            _go = new IconButton("Update selected", null, ButtonKind.Primary) { Anchor = AnchorStyles.Right, Margin = new Padding(0) };
            _go.SetMin(Px(160), Px(40));
            _go.Click += (s, e) => Go();
            footer.Controls.Add(_selectAll, 0, 0);
            footer.Controls.Add(_clear, 1, 0);
            footer.Controls.Add(_close, 3, 0);
            footer.Controls.Add(_go, 4, 0);

            Controls.Add(ScrollHost.Wrap(_body));
            Controls.Add(head);
            Controls.Add(sumBar);
            Controls.Add(footer);
            CancelButton = _close;

            FormClosing += (s, e) =>
            {
                if (!_running) return;
                e.Cancel = true;
                Toast.Show(this, "Updates are still running. Use Cancel to stop after the current component.", ToastKind.Warning);
            };
            Shown += (s, e) => Check();
            UpdateFooter();
        }

        // ------------------------------------------------------------------ checking

        void Check()
        {
            if (_checking || _running) return;
            _checking = true;
            foreach (var r in _rows) { _body.Controls.Remove(r); r.Dispose(); }
            _rows.Clear();
            _placeholder.Visible = true;
            _placeholderText.Text = "Checking Apache, PHP, MariaDB, phpMyAdmin and Node.js for updates…";
            _placeholderText.ForeColor = Ui.Muted;
            _placeholderBar.Visible = true;
            _placeholderBar.Value = -1;
            _summary.Text = ""; _openLog.Visible = false;
            _sub.Text = "Looking up the latest versions from each publisher…";
            UpdateFooter();
            var cfg = Cfg;
            Task.Run(() => CheckFunc(cfg)).ContinueWith(t =>
            {
                if (IsDisposed) return;
                _checking = false;
                _placeholderBar.Visible = false;
                if (t.IsFaulted)
                {
                    _placeholderText.Text = "Couldn't check for updates: " + t.Exception.GetBaseException().Message + "\nCheck your internet connection and try again.";
                    _placeholderText.ForeColor = Ui.Red;
                    _sub.Text = LastChecked();
                    UpdateFooter();
                    return;
                }
                _placeholder.Visible = false;
                foreach (var it in t.Result)
                {
                    var row = new UpdRow(it, _tips);
                    row.Check.CheckedChanged += (s, e) => UpdateFooter();
                    row.Check.Checked = it.State == UpdState.Available;
                    _rows.Add(row);
                    _body.Controls.Add(row);
                }
                LayoutRows();
                FitHeight();
                RefreshSummary();
                UpdateFooter();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        string LastChecked()
        {
            DateTime t;
            if (!DateTime.TryParse(Cfg.LastUpdateCheck, CultureInfo.InvariantCulture, DateTimeStyles.None, out t)) return "Not checked yet";
            string when = t.Date == DateTime.Today ? "today at " + t.ToString("HH:mm") : t.Date == DateTime.Today.AddDays(-1) ? "yesterday at " + t.ToString("HH:mm") : t.ToString("d MMM yyyy, HH:mm");
            return "Last checked " + when;
        }

        void RefreshSummary()
        {
            int avail = _rows.Count(r => r.Item.Eligible);
            int blocked = _rows.Count(r => r.Item.State == UpdState.Blocked);
            int failed = _rows.Count(r => r.Item.State == UpdState.LookupFailed);
            var parts = new List<string> { LastChecked() };
            parts.Add(avail == 0 ? "everything is up to date" : avail + (avail == 1 ? " update" : " updates") + " available");
            if (blocked > 0) parts.Add(blocked + " need manual steps");
            if (failed > 0) parts.Add(failed + " couldn't be checked");
            _sub.Text = string.Join("  ·  ", parts);
            if (!_running && _summary.Text == "")
                _summary.Text = avail == 0 ? "" : "Only the services that use a component are stopped while it updates; everything else keeps running.";
        }

        // ------------------------------------------------------------------ layout

        void LayoutRows()
        {
            int w = _body.ClientSize.Width - _body.Padding.Horizontal;
            if (w < 100) return;
            int y = _body.Padding.Top + _body.AutoScrollPosition.Y;
            _placeholder.SetBounds(_body.Padding.Left, y, w, _placeholder.Height);
            _placeholderText.MaximumSize = new Size(w, 0);
            _placeholderBar.Width = Math.Min(w, Px(320));
            if (_placeholder.Visible) y += _placeholder.Height;
            for (int i = 0; i < _rows.Count; i++)
            {
                var r = _rows[i];
                r.First = i == 0; r.Last = i == _rows.Count - 1;
                int h = r.Measure(w);
                r.SetBounds(_body.Padding.Left, y, w, h);
                y += h;
            }
        }

        /// <summary>Compact window when only a few components are listed.</summary>
        void FitHeight()
        {
            int content = _rows.Sum(r => r.Height) + _body.Padding.Vertical;
            int chrome = ClientSize.Height - _body.ClientSize.Height;
            var wa = Screen.FromControl(this).WorkingArea;
            int want = Math.Max(MinimumSize.Height - (Height - ClientSize.Height), Math.Min(content + chrome, wa.Height - Px(80)));
            if (Math.Abs(want - ClientSize.Height) > Px(8)) ClientSize = new Size(ClientSize.Width, want);
        }

        void UpdateFooter()
        {
            int sel = _rows.Count(r => r.Check.Checked && r.Check.Enabled);
            int eligible = _rows.Count(r => r.Check.Enabled);
            _go.Text = sel == 0 ? "Update selected" : "Update selected (" + sel + ")";
            _go.Enabled = sel > 0 && !_running && !_checking;
            _selectAll.Enabled = eligible > 0 && sel < eligible && !_running;
            _clear.Enabled = sel > 0 && !_running;
            _recheck.Enabled = !_running && !_checking;
            _close.Text = _running ? "Cancel" : "Close";
            _tips.SetToolTip(_close, _running ? "Stop after the component that is updating now" : "");
            foreach (var r in _rows) r.Check.Enabled = !_running && r.Item.Eligible;
            _go.Invalidate(); _close.Invalidate();
        }

        // ------------------------------------------------------------------ updating

        void Go()
        {
            var chosen = _rows.Where(r => r.Check.Checked && r.Check.Enabled).Select(r => r.Item).ToList();
            if (chosen.Count == 0) return;
            using (var dlg = new UpdateConfirmForm(chosen, Cfg))
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

            _running = true;
            _cts = new CancellationTokenSource();
            _summary.Text = "Updating " + chosen.Count + (chosen.Count == 1 ? " component" : " components") + "… You can keep using ServNook.";
            _openLog.Visible = false;
            UpdateFooter();
            var cfg = Cfg;
            var token = _cts.Token;
            Task.Run(() => RunFunc(chosen, cfg, it =>
            {
                try { BeginInvoke(new Action(() => { var row = _rows.FirstOrDefault(r => r.Item == it); if (row != null) { row.Sync(); LayoutRows(); } })); } catch { }
            }, token)).ContinueWith(t =>
            {
                if (IsDisposed) return;
                _running = false;
                foreach (var it in chosen.Where(i => i.Stage == UpdStage.Done))
                {
                    it.Installed = (it.C.Version ?? "");
                    it.State = UpdState.UpToDate;
                    it.Note = "Up to date.";
                }
                foreach (var r in _rows) { if (chosen.Contains(r.Item)) r.Check.Checked = false; r.Sync(); }
                LayoutRows();
                int ok = chosen.Count(i => i.Stage == UpdStage.Done);
                int restored = chosen.Count(i => i.Stage == UpdStage.RolledBack);
                int failed = chosen.Count(i => i.Stage == UpdStage.Failed);
                int cancelled = chosen.Count(i => i.Stage == UpdStage.Cancelled);
                var parts = new List<string>();
                if (ok > 0) parts.Add(ok + " updated");
                if (restored > 0) parts.Add(restored + " kept on the previous version after a failed check");
                if (failed > 0) parts.Add(failed + " failed");
                if (cancelled > 0) parts.Add(cancelled + " cancelled");
                _summary.Text = string.Join("  ·  ", parts) + ". Previous versions are in tmp\\backup.";
                _summary.ForeColor = failed + restored > 0 ? Ui.Red : Ui.Muted;
                _openLog.Visible = true;
                RefreshSummary();
                UpdateFooter();
                if (Owner != null && !Owner.IsDisposed)
                    Toast.Show(Owner, failed + restored > 0 ? "Some updates didn't complete. See Component updates." : "Updates installed.", failed + restored > 0 ? ToastKind.Error : ToastKind.Success);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void CancelRemaining()
        {
            if (_cts == null || _cts.IsCancellationRequested) return;
            _cts.Cancel();
            _summary.Text = "Cancelling: the component that is installing now finishes (or is put back); the rest are skipped.";
            _close.Enabled = false;
        }

        // ================================================================== one component row

        class UpdRow : Panel
        {
            public readonly UpdateItem Item;
            public readonly CheckMark Check = new CheckMark();
            public bool First, Last;
            readonly Label _name, _versions, _detail, _tech;
            readonly LinkLabel _notes, _more;
            readonly Pill _pill = new Pill { Fit = true };
            readonly ProgressLine _bar = new ProgressLine { Visible = false };
            static int Px(float v) { return Ui.Px(v); }

            public UpdRow(UpdateItem item, ToolTip tips)
            {
                Item = item;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
                BackColor = Ui.Bg;
                _pill.Font = Ui.SmallBold; _pill.Height = Px(26);
                _name = new Label { Text = item.Name, Font = Ui.Bold, ForeColor = Ui.Ink, AutoSize = true, BackColor = Color.Transparent };
                _versions = new Label { Font = Ui.Mono, ForeColor = Ui.Ink, AutoSize = true, BackColor = Color.Transparent };
                _detail = new Label { Font = Ui.Small, ForeColor = Ui.Muted, AutoSize = false, BackColor = Color.Transparent, UseMnemonic = false };
                _tech = new Label { Font = Ui.LogFont, ForeColor = Ui.Muted, AutoSize = false, BackColor = Color.Transparent, Visible = false, UseMnemonic = false };
                _notes = Link("Release notes");
                _notes.LinkClicked += (s, e) => { if (Item.NotesUrl != null) Sys.OpenUrl(Item.NotesUrl); };
                if (item.NotesUrl != null) tips.SetToolTip(_notes, item.NotesUrl);
                _more = Link("Show details");
                _more.LinkClicked += (s, e) => { _tech.Visible = !_tech.Visible; _more.Text = _tech.Visible ? "Hide details" : "Show details"; Parent.PerformLayout(); };
                Controls.AddRange(new Control[] { Check, _name, _versions, _pill, _detail, _notes, _more, _tech, _bar });
                tips.SetToolTip(Check, "Include this update");
                Sync();
            }

            static LinkLabel Link(string text)
            {
                return new LinkLabel { Text = text, Font = Ui.Small, AutoSize = true, LinkColor = Ui.AccentInk, ActiveLinkColor = Ui.AccentHover, LinkBehavior = LinkBehavior.HoverUnderline, BackColor = Color.Transparent };
            }

            /// <summary>Reflect the item's state and progress.</summary>
            public void Sync()
            {
                var it = Item;
                string latest = it.C.Version ?? "?";
                _versions.Text = it.Installed == null ? "not installed → " + latest
                                : it.State == UpdState.UpToDate || it.State == UpdState.LookupFailed ? it.Installed
                                : it.Installed + "  →  " + latest;
                _versions.ForeColor = it.Eligible ? Ui.Ink : Ui.Muted;
                _notes.Visible = it.NotesUrl != null && it.State != UpdState.LookupFailed;
                _bar.Visible = false;
                _more.Visible = false;
                _detail.ForeColor = Ui.Muted;
                switch (it.Stage)
                {
                    case UpdStage.Idle:
                        switch (it.State)
                        {
                            case UpdState.UpToDate: _pill.Set(PillState.Running, "Up to date"); break;
                            case UpdState.Available: _pill.Set(PillState.Info, "Update available"); break;
                            case UpdState.NotInstalled: _pill.Set(PillState.Info, "Not installed"); break;
                            case UpdState.Blocked: _pill.Set(PillState.Partial, "Manual update"); _detail.ForeColor = Ui.Ink; break;
                            default: _pill.Set(PillState.Error, "Check failed"); break;
                        }
                        _detail.Text = it.State == UpdState.UpToDate ? "" : it.Note ?? "";
                        break;
                    case UpdStage.Done: _pill.Set(PillState.Running, "Updated"); _detail.Text = it.Message; break;
                    case UpdStage.Failed: _pill.Set(PillState.Error, "Failed"); _detail.Text = it.Message; _detail.ForeColor = Ui.Red; break;
                    case UpdStage.RolledBack: _pill.Set(PillState.Partial, "Previous version kept"); _detail.Text = it.Message; _detail.ForeColor = Ui.Ink; break;
                    case UpdStage.Cancelled: _pill.Set(PillState.Stopped, "Cancelled"); _detail.Text = it.Message; break;
                    case UpdStage.Waiting: _pill.Set(PillState.Stopped, "Waiting"); _detail.Text = "Waiting for the other updates…"; break;
                    default:
                        _pill.Set(PillState.Busy, StageName(it.Stage) + (it.Stage == UpdStage.Downloading && it.Pct >= 0 ? " " + it.Pct + "%" : ""));
                        _detail.Text = it.Message;
                        _bar.Visible = true;
                        _bar.Value = it.Stage == UpdStage.Downloading ? it.Pct : -1;
                        break;
                }
                if (!string.IsNullOrEmpty(it.Error) && (it.Stage == UpdStage.Failed || it.Stage == UpdStage.RolledBack))
                {
                    _tech.Text = it.Error.Length > 1500 ? it.Error.Substring(0, 1500) + "…" : it.Error;
                    _more.Visible = true;
                }
                else _tech.Visible = false;
                Invalidate();
            }

            static string StageName(UpdStage s)
            {
                switch (s)
                {
                    case UpdStage.Downloading: return "Downloading";
                    case UpdStage.Verifying: return "Verifying";
                    case UpdStage.BackingUp: return "Backing up";
                    case UpdStage.Stopping: return "Stopping";
                    case UpdStage.Installing: return "Installing";
                    case UpdStage.Checking: return "Health check";
                    case UpdStage.Restarting: return "Starting";
                }
                return s.ToString();
            }

            static Size Wrapped(Label l, int width)
            {
                var sz = TextRenderer.MeasureText(l.Text, l.Font, new Size(width, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                return new Size(width, sz.Height + 2);
            }

            /// <summary>Lay out for <paramref name="w"/> and return the height needed.</summary>
            public int Measure(int w)
            {
                int pad = Px(16), x0 = Px(52);
                Check.Location = new Point(pad, Px(20));
                _pill.Location = new Point(w - pad - _pill.Width, Px(16));
                bool wide = w >= Px(640);
                _name.Location = new Point(x0, Px(18));
                int y;
                if (wide)
                {
                    _versions.Location = new Point(Math.Max(x0 + _name.Width + Px(16), _pill.Left - Px(20) - _versions.Width), Px(19));
                    y = Px(44);
                }
                else
                {
                    _versions.Location = new Point(x0, Px(42));
                    y = Px(66);
                }
                int textW = Math.Max(Px(120), w - x0 - pad);
                _detail.Visible = _detail.Text.Length > 0;
                if (_detail.Visible)
                {
                    _detail.Size = Wrapped(_detail, textW);
                    _detail.Location = new Point(x0, y);
                    y += _detail.Height;
                }
                else y -= Px(6);
                if (_bar.Visible) { _bar.SetBounds(x0, y + Px(8), Math.Min(textW, Px(360)), Px(4)); y += Px(14); }
                int lx = x0; y += Px(6);
                if (_notes.Visible) { _notes.Location = new Point(lx, y); lx += _notes.Width + Px(14); }
                if (_more.Visible) { _more.Location = new Point(lx, y); }
                if (_notes.Visible || _more.Visible) y += _notes.Height;
                if (_tech.Visible)
                {
                    _tech.Size = Wrapped(_tech, textW);
                    _tech.Location = new Point(x0, y + Px(6));
                    y += Px(6) + _tech.Height;
                }
                return y + Px(16);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Ui.Bg);
                // The rows together form one rounded card: only the outer corners are rounded, and each
                // row except the last runs past its bottom edge so neighbours share a single divider line.
                float r = Px(12), top = 0.5f, bottom = Last ? Height - 1f : Height + 2f, right = Width - 1f;
                using (var p = new GraphicsPath())
                {
                    if (First) { p.AddArc(0.5f, top, 2 * r, 2 * r, 180, 90); p.AddArc(right - 2 * r, top, 2 * r, 2 * r, 270, 90); }
                    else { p.AddLine(0.5f, top, right, top); }
                    if (Last) { p.AddArc(right - 2 * r, bottom - 2 * r, 2 * r, 2 * r, 0, 90); p.AddArc(0.5f, bottom - 2 * r, 2 * r, 2 * r, 90, 90); }
                    else { p.AddLine(right, bottom, 0.5f, bottom); }
                    p.CloseFigure();
                    using (var b = new SolidBrush(Ui.Card)) g.FillPath(b, p);
                    using (var pen = new Pen(Ui.Line)) g.DrawPath(pen, p);
                }
            }
        }
    }

    /// <summary>Confirmation before updating: what each update does, and what it stops.</summary>
    public class UpdateConfirmForm : Form
    {
        static int Px(float v) { return Ui.Px(v); }

        public UpdateConfirmForm(List<UpdateItem> items, ServNookConfig cfg)
        {
            Ui.Style(this);
            AutoScaleMode = AutoScaleMode.None;
            Text = "Install updates";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            BackColor = Ui.Card;
            int w = Px(560), inner = w - Px(48);

            var body = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Dock = DockStyle.Fill, BackColor = Ui.Card, Padding = new Padding(Px(24), Px(20), Px(12), Px(8)) };
            body.Controls.Add(new Label { Text = items.Count == 1 ? "Install this update?" : "Install " + items.Count + " updates?", Font = Ui.SectionTitle, ForeColor = Ui.Ink, AutoSize = true, Margin = new Padding(0, 0, 0, Px(4)) });
            body.Controls.Add(new Label { Text = "Your settings, sites, virtual hosts, databases and Node projects are kept.", Font = Ui.Small, ForeColor = Ui.Muted, AutoSize = true, MaximumSize = new Size(inner, 0), Margin = new Padding(0, 0, 0, Px(12)) });
            foreach (var it in items)
            {
                body.Controls.Add(new Label { Text = it.Name + "   " + (it.Installed ?? "new") + " → " + it.C.Version, Font = Ui.Bold, ForeColor = Ui.Ink, AutoSize = true, Margin = new Padding(0, Px(6), 0, Px(2)) });
                foreach (var line in Updates.Plan(it, cfg))
                    body.Controls.Add(new Label { Text = "•  " + line, Font = Ui.Small, ForeColor = Ui.Muted, AutoSize = true, MaximumSize = new Size(inner - Px(8), 0), Margin = new Padding(Px(4), Px(1), 0, 0) });
            }
            var unverified = items.Where(i => !i.Verified).Select(i => i.Name).ToList();
            if (unverified.Count > 0)
                body.Controls.Add(new Callout(CalloutKind.Warning, string.Join(", ", unverified) + (unverified.Count == 1 ? " has" : " have") +
                    " no published checksum. ServNook downloads it only over HTTPS from the official site and checks the package contents before installing.", inner) { Margin = new Padding(0, Px(12), 0, 0) });
            if (items.Any(i => i.Id == "mariadb"))
                body.Controls.Add(new Callout(CalloutKind.Info, "Your databases are backed up to data\\backups before MariaDB is replaced.", inner) { Margin = new Padding(0, Px(8), 0, 0) });

            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = Px(68), BackColor = Ui.Card, Padding = new Padding(Px(20), Px(14), Px(20), Px(14)) };
            footer.Paint += (s, e) => { using (var pen = new Pen(Ui.Line)) e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0); };
            var ok = new IconButton(items.Count == 1 ? "Install update" : "Install updates", null, ButtonKind.Primary) { Margin = new Padding(0) };
            ok.SetMin(Px(150), Px(40));
            ok.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };
            var cancel = new IconButton("Cancel", null, ButtonKind.Outline) { Margin = new Padding(0, 0, Px(10), 0) };
            cancel.SetMin(Px(96), Px(40));
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            footer.Controls.Add(ok);
            footer.Controls.Add(cancel);
            AcceptButton = ok; CancelButton = cancel;

            Controls.Add(ScrollHost.Wrap(body));
            Controls.Add(footer);
            int h = body.Padding.Vertical + body.Controls.Cast<Control>().Sum(c => c.GetPreferredSize(new Size(inner, 0)).Height + c.Margin.Vertical) + footer.Height;
            ClientSize = new Size(w, Math.Min(h + Px(8), Screen.PrimaryScreen.WorkingArea.Height - Px(120)));
        }
    }
}
