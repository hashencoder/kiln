using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Threading;

namespace ServNook
{
    public enum UpdState { UpToDate, Available, NotInstalled, Blocked, LookupFailed }
    public enum UpdStage { Idle, Waiting, Downloading, Verifying, BackingUp, Stopping, Installing, Checking, Restarting, Done, Failed, RolledBack, Cancelled }

    /// <summary>One component in the update manager: what's installed, what's available, and how an update went.</summary>
    public class UpdateItem
    {
        public Component C;
        public string Installed;        // null = not installed
        public UpdState State;
        public string Note;             // why it's blocked / up to date / failed lookup
        public string NotesUrl;         // official release notes
        public bool Verified { get { return C != null && C.Sha256 != null; } }
        public bool Eligible { get { return State == UpdState.Available || State == UpdState.NotInstalled; } }

        // progress of the current run
        public volatile UpdStage Stage = UpdStage.Idle;
        public int Pct = -1;
        public string Message = "";
        public string Error;            // full technical message when Failed / RolledBack

        public string Id { get { return C.Id; } }
        public string Name { get { return C.Name; } }
    }

    /// <summary>
    /// Component update manager. Each component has its own rules: what must be stopped, what is backed up,
    /// what is carried over from the old version, and how the new version is health-checked. A failed health
    /// check puts the previous version back.
    /// </summary>
    public static class Updates
    {
        /// <summary>Overridable for tests (the real check reads the Windows registry).</summary>
        public static Func<bool> VcRuntimeOk = Installer.VcRedistOk;

        static readonly Dictionary<string, string[]> OfficialHosts = new Dictionary<string, string[]>
        {
            { "apache", new[] { "www.apachelounge.com", "apachelounge.com" } },
            { "php", new[] { "windows.php.net", "downloads.php.net" } },
            { "mariadb", new[] { "dlm.mariadb.com", "downloads.mariadb.org", "archive.mariadb.org", "mirror.mariadb.org" } },
            { "phpmyadmin", new[] { "files.phpmyadmin.net" } },
            { "node", new[] { "nodejs.org" } },
        };

        /// <summary>Files a genuine Windows x64 package must contain (paths inside the package's top folder).</summary>
        static readonly Dictionary<string, string[]> Required = new Dictionary<string, string[]>
        {
            { "apache", new[] { "bin/httpd.exe", "modules/mod_proxy_http.so", "conf/mime.types" } },
            { "php", new[] { "php.exe", "php8apache2_4.dll", "ext/" } },
            { "mariadb", new[] { "bin/mariadbd.exe", "bin/mariadb.exe", "bin/mariadb-dump.exe" } },
            { "phpmyadmin", new[] { "index.php", "libraries/" } },
            { "node", new[] { "node.exe", "npm.cmd" } },
        };

        // ------------------------------------------------------------------ detection

        public static string InstalledVersion(string dir)
        {
            return Directory.Exists(dir) ? ServNookConfig.FullVersion(dir) : null;
        }

        /// <summary>Look up the newest version of every installed component (network).</summary>
        public static List<UpdateItem> Check(ServNookConfig cfg)
        {
            var items = new List<UpdateItem>();
            items.Add(Evaluate(Resolver.Apache(cfg), InstalledVersion(K.Apache), cfg));
            if (!string.IsNullOrEmpty(cfg.Php))
                items.Add(Evaluate(Resolver.Php(cfg, cfg.Php), InstalledVersion(cfg.PhpDir), cfg));
            items.Add(Evaluate(Resolver.MariaDb(cfg), InstalledVersion(K.MariaDb), cfg));
            items.Add(Evaluate(Resolver.PhpMyAdmin(cfg), InstalledVersion(K.Pma), cfg));
            if (!string.IsNullOrEmpty(cfg.Node))
                items.Add(Evaluate(Resolver.Node(cfg, cfg.Node), InstalledVersion(cfg.NodeDir), cfg));
            cfg.LastUpdateCheck = DateTime.Now.ToString("s");
            try { cfg.Save(); } catch { }
            return items;
        }

        static Version Key(string v) { return ServNookConfig.VersionKey(v); }
        static string Series(string v) { var k = Key(v); return k.Major + "." + k.Minor; }

        /// <summary>Decide whether a component can be updated, and if not, why. No network access.</summary>
        public static UpdateItem Evaluate(Component c, string installed, ServNookConfig cfg)
        {
            var it = new UpdateItem { C = c, Installed = string.IsNullOrEmpty(installed) ? null : installed };
            it.NotesUrl = NotesUrl(c);
            if (!c.Ready)
            {
                it.State = UpdState.LookupFailed;
                it.Note = "Couldn't look up the latest version: " + (c.Error ?? "no download found") + ".";
                return it;
            }
            if (it.Installed == null)
            {
                it.State = UpdState.NotInstalled;
                it.Note = "Not installed. It will be installed.";
            }
            else if (Key(c.Version) > Key(it.Installed) ||
                     (c.Id == "apache" && Key(c.Version) == Key(it.Installed) && c.Version != it.Installed && BuildNumber(c.Version) > BuildNumber(it.Installed)))
            {
                it.State = UpdState.Available;
                it.Note = it.Verified ? "Checksum published by the publisher; it is checked after download."
                                      : "The publisher doesn't publish a checksum file; the package comes from its official site over HTTPS and its contents are checked.";
            }
            else
            {
                it.State = UpdState.UpToDate;
                it.Note = "Up to date.";
                return it;
            }

            string block = BlockReason(c, it.Installed, cfg);
            if (block != null) { it.State = UpdState.Blocked; it.Note = block; }
            return it;
        }

        static int BuildNumber(string v)
        {
            var m = System.Text.RegularExpressions.Regex.Match(v ?? "", @"VS(\d+)");
            return m.Success ? int.Parse(m.Groups[1].Value) : 0;
        }

        /// <summary>Why an update must not run automatically, or null when it is safe.</summary>
        public static string BlockReason(Component c, string installed, ServNookConfig cfg)
        {
            Uri u;
            if (c.LocalFile == null && (c.Url == null || !Uri.TryCreate(c.Url, UriKind.Absolute, out u) || u.Scheme != "https"))
                return "The download address isn't HTTPS, so the package can't be trusted.";
            if (c.LocalFile == null && c.Sha256 == null && Uri.TryCreate(c.Url, UriKind.Absolute, out u))
            {
                string[] hosts;
                if (OfficialHosts.TryGetValue(c.Id, out hosts) && !hosts.Contains(u.Host.ToLowerInvariant()))
                    return "The package isn't hosted by the official publisher (" + u.Host + ") and has no checksum, so its authenticity can't be verified.";
            }
            switch (c.Id)
            {
                case "apache":
                    if (!VcRuntimeOk())
                        return "This Apache build needs a newer Microsoft Visual C++ Redistributable. Install it first (Settings › Maintenance › Visual C++ runtime).";
                    break;
                case "php":
                    if (installed != null && Series(c.Version) != Series(installed))
                        return "PHP " + Series(c.Version) + " is a different version line from your PHP " + Series(installed) + ". Install it side by side in Settings › Runtime instead.";
                    if (!VcRuntimeOk())
                        return "This PHP build needs a newer Microsoft Visual C++ Redistributable. Install it first (Settings › Maintenance).";
                    break;
                case "mariadb":
                    if (installed != null && Series(c.Version) != Series(installed))
                        return "MariaDB " + Series(c.Version) + " is a new release series. Moving from " + Series(installed) +
                               " changes the data format, so it needs a manual upgrade with a full backup (see Component updates in docs/06-control-panel.md). Patch updates within " + Series(installed) + " are installed here.";
                    break;
                case "node":
                    if (installed != null && Key(c.Version).Major != Key(installed).Major)
                        return "Node.js " + Key(c.Version).Major + " is a different major version. Install it side by side in Settings › Runtime instead.";
                    break;
                case "phpmyadmin":
                    if (installed != null && Key(c.Version).Major != Key(installed).Major)
                        return "phpMyAdmin " + Key(c.Version).Major + " is a new major version with different PHP requirements. Update it by hand after checking its release notes.";
                    break;
            }
            return null;
        }

        /// <summary>Release notes on the publisher's own site.</summary>
        public static string NotesUrl(Component c)
        {
            if (c == null || string.IsNullOrEmpty(c.Version)) return c == null ? null : c.Homepage;
            string v = c.Version.Split(' ')[0];
            switch (c.Id)
            {
                case "apache": return "https://downloads.apache.org/httpd/CHANGES_2.4";
                case "php": return "https://www.php.net/ChangeLog-" + Key(v).Major + ".php#" + v;
                case "mariadb": return "https://mariadb.com/docs/release-notes/community-server/" + Series(v) + "/" + v;
                case "phpmyadmin": return "https://www.phpmyadmin.net/files/" + v + "/";
                case "node": return "https://nodejs.org/en/blog/release/v" + v;
            }
            return c.Homepage;
        }

        /// <summary>Running services an update of this component stops and starts again. Unrelated services keep running.</summary>
        public static List<Service> Affected(UpdateItem it, ServNookConfig cfg)
        {
            var list = new List<Service>();
            switch (it.Id)
            {
                case "apache": list.Add(Services.Apache); break;
                case "php": if (it.C.Branch == null || it.C.Branch == cfg.Php) list.Add(Services.Apache); break;
                case "mariadb": list.Add(Services.MariaDb); break;
                case "node":
                    if (it.C.Branch == null || it.C.Branch == cfg.Node)
                        list.AddRange(Services.All().OfType<NodeService>());
                    break;
            }
            return list;
        }

        /// <summary>Plain-language description of what happens during this update (for the confirmation dialog).</summary>
        public static List<string> Plan(UpdateItem it, ServNookConfig cfg)
        {
            var p = new List<string>();
            var running = Affected(it, cfg).Where(s => s.IsRunning).Select(s => s.Title).ToList();
            if (running.Count > 0) p.Add("Stops " + string.Join(", ", running) + " during the update, then starts " + (running.Count == 1 ? "it" : "them") + " again.");
            else if (it.Id != "phpmyadmin") p.Add("Nothing needs to be stopped.");
            switch (it.Id)
            {
                case "apache": p.Add("Keeps your httpd.conf, virtual hosts and extra modules (config\\apache is outside the Apache folder)."); break;
                case "php": p.Add("Keeps php.ini and extra extensions you added to ext\\."); break;
                case "mariadb":
                    p.Add("Backs up every database first; the update stops if the backup fails.");
                    p.Add("Your data folder (data\\mariadb) and my.ini are not touched; mariadb-upgrade runs afterwards.");
                    break;
                case "phpmyadmin": p.Add("Keeps config.inc.php. Apache keeps running."); break;
                case "node": p.Add("Keeps globally installed npm packages. Your Node projects in www\\ are not touched."); break;
            }
            p.Add(it.Verified ? "Checks the download's SHA-256 against the publisher's checksum." : "No published checksum: checks the HTTPS source and the package contents instead.");
            p.Add("The previous version is kept in tmp\\backup and put back automatically if the new one fails its health check.");
            return p;
        }

        // ------------------------------------------------------------------ running updates

        /// <summary>
        /// Install the chosen updates one by one. Each item reports its stage through <paramref name="changed"/>.
        /// A failure affects only that component: its previous version is restored and the next item runs.
        /// </summary>
        public static void Run(List<UpdateItem> items, ServNookConfig cfg, Action<UpdateItem> changed, CancellationToken ct)
        {
            foreach (var it in items) { it.Stage = UpdStage.Waiting; it.Pct = -1; it.Message = "Waiting…"; it.Error = null; changed(it); }
            foreach (var it in items)
            {
                if (ct.IsCancellationRequested) { Set(it, changed, UpdStage.Cancelled, "Cancelled before it started. Nothing was changed."); continue; }
                try { RunOne(it, cfg, changed, ct); }
                catch (Exception e) { Log.Error(it.Name + " update: " + e.Message); }
            }
        }

        static void Set(UpdateItem it, Action<UpdateItem> changed, UpdStage stage, string msg, int pct = -1)
        {
            it.Stage = stage; it.Message = msg; it.Pct = pct;
            changed(it);
        }

        static void RunOne(UpdateItem it, ServNookConfig cfg, Action<UpdateItem> changed, CancellationToken ct)
        {
            var c = it.C;
            var inst = new Installer(cfg);
            inst.Progress += (m, p) =>
            {
                if (m.StartsWith("Verifying")) Set(it, changed, UpdStage.Verifying, "Verifying the download…");
                else if (m.StartsWith("Downloading") || m.Contains("cached download")) Set(it, changed, UpdStage.Downloading, m, p);
            };
            Log.Info("Updating " + c.Name + " " + (it.Installed ?? "(not installed)") + " -> " + c.Version);

            // 1. Download and verify before anything is stopped, so services are down only for the swap.
            string zip;
            try
            {
                Set(it, changed, UpdStage.Downloading, "Downloading…", 0);
                zip = c.LocalFile ?? inst.Download(c, ct);
                Set(it, changed, UpdStage.Verifying, "Checking the package…");
                CheckPackage(c, zip);
            }
            catch (OperationCanceledException)
            {
                Set(it, changed, UpdStage.Cancelled, "Cancelled. Nothing was changed.");
                return;
            }
            catch (Exception e)
            {
                Fail(it, changed, UpdStage.Failed, "Download or verification failed. Nothing was changed.", e.Message);
                return;
            }

            // 2. Database backup before MariaDB is touched.
            if (c.Id == "mariadb" && Directory.Exists(K.MariaDbData) && Directory.EnumerateFileSystemEntries(K.MariaDbData).Any())
            {
                Set(it, changed, UpdStage.BackingUp, "Backing up your databases…");
                try
                {
                    bool wasUp = Services.MariaDb.IsRunning;
                    if (!wasUp) Services.MariaDb.Start();
                    string file = Backup.Create(cfg, "before updating MariaDB to " + c.Version);
                    if (!wasUp) Services.MariaDb.Stop(false);
                    Log.Info("Database backup before the update: " + (file ?? "no user databases"));
                }
                catch (Exception e)
                {
                    Fail(it, changed, UpdStage.Failed, "Couldn't back up your databases, so MariaDB was not updated.", e.Message);
                    return;
                }
            }

            // 3. Stop only what this component needs stopped.
            var stopped = new List<Service>();
            try
            {
                foreach (var s in Affected(it, cfg).Where(s => s.IsRunning))
                {
                    Set(it, changed, UpdStage.Stopping, "Stopping " + s.Title + "…");
                    s.Busy = true;
                    if (s is MariaDbService) ((MariaDbService)s).Stop(false); else s.Stop();
                    stopped.Add(s);
                }
            }
            catch (Exception e)
            {
                RestartAll(stopped);
                Fail(it, changed, UpdStage.Failed, "Couldn't stop the services that use " + c.Name + ". Nothing was changed.", e.Message);
                return;
            }

            // 4. Swap in the new version (the old one is moved to tmp\backup).
            string targetDir = TargetDir(it, cfg);
            string backupDir = null;
            try
            {
                Set(it, changed, UpdStage.Installing, "Installing " + c.Version + "…");
                var local = new Component { Id = c.Id, Name = c.Name, Branch = c.Branch, Version = c.Version, LocalFile = zip, FileName = c.FileName, Sha256 = c.Sha256 };
                inst.Install(local, ct);
                backupDir = inst.LastBackupDir;

                // 5. Health check of the new files, then bring the stopped services back.
                Set(it, changed, UpdStage.Checking, "Checking the new version…");
                HealthCheck(it, cfg, false);
                if (stopped.Count > 0)
                {
                    Set(it, changed, UpdStage.Restarting, "Starting " + string.Join(", ", stopped.Select(s => s.Title)) + "…");
                    foreach (var s in stopped) s.Start();
                    HealthCheck(it, cfg, true);
                }
                foreach (var s in stopped) s.Busy = false;
                Set(it, changed, UpdStage.Done, "Updated to " + c.Version + (stopped.Count > 0 ? "; " + string.Join(", ", stopped.Select(s => s.Title)) + " running again." : "."));
                Log.Info(c.Name + " updated to " + c.Version + ".");
            }
            catch (Exception e)
            {
                string reason = e.Message;
                Log.Error(c.Name + " " + c.Version + " failed: " + reason);
                Set(it, changed, UpdStage.Installing, "Putting the previous version back…");
                string rollbackError = null;
                try
                {
                    foreach (var s in stopped) { try { if (s.IsRunning) s.Stop(); } catch { } }
                    if (backupDir != null && Directory.Exists(backupDir))
                    {
                        if (Directory.Exists(targetDir))
                        {
                            if (it.Id == "node") Installer.CarryFolder(targetDir, backupDir, "global");   // hand npm globals back
                            string failed = Path.Combine(K.Tmp, "backup", Path.GetFileName(targetDir) + "-failed-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                            Directory.Move(targetDir, failed);
                        }
                        Directory.Move(backupDir, targetDir);
                        Log.Info("Restored the previous " + c.Name + " from " + backupDir);
                    }
                }
                catch (Exception re) { rollbackError = re.Message; }
                var restartErrors = RestartAll(stopped);
                if (rollbackError != null)
                    Fail(it, changed, UpdStage.Failed, "The update failed and the previous version couldn't be put back automatically. It is in " + backupDir + ".",
                         reason + "\nRollback: " + rollbackError);
                else
                    Fail(it, changed, UpdStage.RolledBack, "The new version failed its check, so the previous version " + (it.Installed ?? "") + " was restored" +
                         (restartErrors.Count > 0 ? ", but some services didn't start again." : "."), reason + (restartErrors.Count > 0 ? "\n" + string.Join("\n", restartErrors) : ""));
            }
        }

        static List<string> RestartAll(List<Service> stopped)
        {
            var errors = new List<string>();
            foreach (var s in stopped)
            {
                try { if (!s.IsRunning) s.Start(); }
                catch (Exception e) { errors.Add(s.Title + ": " + e.Message); Log.Error(s.Title + " didn't start: " + e.Message); }
                s.Busy = false;
            }
            return errors;
        }

        static void Fail(UpdateItem it, Action<UpdateItem> changed, UpdStage stage, string msg, string detail)
        {
            it.Error = detail;
            Log.Error(it.Name + ": " + msg + (detail != null ? " (" + detail.Split('\n')[0] + ")" : ""));
            Set(it, changed, stage, msg);
        }

        public static string TargetDir(UpdateItem it, ServNookConfig cfg)
        {
            switch (it.Id)
            {
                case "apache": return K.Apache;
                case "php": return K.PhpDir(it.C.Branch ?? cfg.Php);
                case "mariadb": return K.MariaDb;
                case "phpmyadmin": return K.Pma;
                case "node": return K.NodeDir(it.C.Branch ?? cfg.Node);
            }
            throw new InvalidOperationException("Unknown component " + it.Id);
        }

        /// <summary>Open the zip and make sure it is the right kind of package before anything is replaced.</summary>
        public static void CheckPackage(Component c, string zip)
        {
            string[] need;
            if (!Required.TryGetValue(c.Id, out need)) return;
            List<string> names;
            try
            {
                using (var z = ZipFile.OpenRead(zip))
                    names = z.Entries.Select(e => e.FullName.Replace('\\', '/')).ToList();
            }
            catch (Exception e) { throw new InvalidDataException(Path.GetFileName(zip) + " is not a valid zip file (" + e.Message + ")."); }
            // Packages wrap their files in one top folder (Apache24/, mariadb-x-winx64/, ...); accept either layout.
            var missing = need.Where(n => !names.Any(e => e.Equals(n, StringComparison.OrdinalIgnoreCase) ||
                                                          e.EndsWith("/" + n, StringComparison.OrdinalIgnoreCase) && e.Count(ch => ch == '/') - n.Count(ch => ch == '/') <= 1 ||
                                                          (n.EndsWith("/") && (e.StartsWith(n, StringComparison.OrdinalIgnoreCase) || e.IndexOf("/" + n, StringComparison.OrdinalIgnoreCase) >= 0)))).ToList();
            if (missing.Count > 0)
                throw new InvalidDataException("The downloaded file doesn't look like a Windows x64 " + c.Name + " package (missing " + string.Join(", ", missing) + ").");
        }

        /// <summary>Make sure the new version actually works. afterStart: services are running again.</summary>
        public static void HealthCheck(UpdateItem it, ServNookConfig cfg, bool afterStart)
        {
            string dir = TargetDir(it, cfg);
            string want = (it.C.Version ?? "").Split(' ')[0];
            switch (it.Id)
            {
                case "apache":
                    if (!afterStart)
                    {
                        var r = Sys.Run(K.Httpd, "-v", null, 20000, Path.Combine(K.Apache, "bin"));
                        if (r.ExitCode != 0) throw new Exception("httpd.exe didn't run: " + r.Output.Trim());
                        if (want.Length > 0 && !r.Output.Contains(want)) throw new Exception("httpd.exe reports a different version:\n" + r.Output.Trim());
                        if (File.Exists(Path.Combine(cfg.PhpDir, "php8apache2_4.dll"))) Services.Apache.Check();
                    }
                    else HttpOk(cfg, "/");
                    break;
                case "php":
                    if (!afterStart)
                    {
                        var r = Sys.Run(Path.Combine(dir, "php.exe"), "-n -v", null, 20000, dir);
                        if (r.ExitCode != 0) throw new Exception("php.exe didn't run: " + r.Output.Trim());
                        if (want.Length > 0 && !r.Output.Contains(want)) throw new Exception("php.exe reports a different version:\n" + r.Output.Trim());
                        if (File.Exists(K.Httpd) && (it.C.Branch ?? cfg.Php) == cfg.Php) Services.Apache.Check();
                    }
                    else HttpOk(cfg, "/");
                    break;
                case "mariadb":
                    if (!afterStart)
                    {
                        var exe = Installer.MariaExe("mariadbd.exe", "mysqld.exe");
                        if (exe == null) throw new Exception("mariadbd.exe is missing from the new version.");
                        var r = Sys.Run(exe, "--version", null, 20000, K.MariaDbBin);
                        if (r.ExitCode != 0) throw new Exception("mariadbd.exe didn't run: " + r.Output.Trim());
                        if (want.Length > 0 && !r.Output.Contains(want)) throw new Exception("mariadbd.exe reports a different version:\n" + r.Output.Trim());
                    }
                    else
                    {
                        var upgrade = Installer.MariaExe("mariadb-upgrade.exe", "mysql_upgrade.exe");
                        var env = Services.Env();
                        env["MYSQL_PWD"] = cfg.MysqlRootPassword ?? "";
                        if (upgrade != null)
                        {
                            var u = Sys.Run(upgrade, "--protocol=TCP -h 127.0.0.1 -P " + cfg.MysqlPort + " -u root", env, 300000, K.MariaDbBin);
                            if (u.ExitCode != 0) Log.Warn("mariadb-upgrade: " + u.Output.Trim());
                        }
                        var q = Services.MariaDb.Sql("SELECT VERSION()", cfg.MysqlRootPassword);
                        if (q.ExitCode != 0) throw new Exception("MariaDB started but doesn't answer queries:\n" + q.Output.Trim());
                        if (want.Length > 0 && !q.Output.Contains(want)) throw new Exception("MariaDB reports a different version:\n" + q.Output.Trim());
                    }
                    break;
                case "phpmyadmin":
                    if (!File.Exists(Path.Combine(dir, "index.php"))) throw new Exception("index.php is missing from the new phpMyAdmin.");
                    if (!File.Exists(Path.Combine(dir, "config.inc.php"))) throw new Exception("config.inc.php wasn't kept.");
                    if (Services.Apache.IsRunning) HttpOk(cfg, "/phpmyadmin/");
                    break;
                case "node":
                    if (!afterStart)
                    {
                        var r = Sys.Run(Path.Combine(dir, "node.exe"), "-v", null, 20000, dir);
                        if (r.ExitCode != 0) throw new Exception("node.exe didn't run: " + r.Output.Trim());
                        if (want.Length > 0 && r.Output.Trim().TrimStart('v') != want) throw new Exception("node.exe reports " + r.Output.Trim() + " instead of v" + want + ".");
                    }
                    break;
            }
        }

        /// <summary>Apache answers over HTTP (any status below 500 counts; a PHP error page means PHP is broken).</summary>
        public static void HttpOk(ServNookConfig cfg, string path)
        {
            if (!Services.Apache.IsRunning) return;
            string url = "http://127.0.0.1:" + cfg.HttpPort + path;
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Timeout = 15000; req.Proxy = null; req.AllowAutoRedirect = false;
            try
            {
                using (var resp = (HttpWebResponse)req.GetResponse()) { }
            }
            catch (WebException e)
            {
                var resp = e.Response as HttpWebResponse;
                if (resp == null) throw new Exception("Apache doesn't answer at " + url + ": " + e.Message);
                int code = (int)resp.StatusCode;
                resp.Close();
                if (code >= 500) throw new Exception(url + " returned HTTP " + code + ". See logs\\apache-error.log.");
            }
        }
    }
}
