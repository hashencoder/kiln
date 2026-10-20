using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Kiln
{
    /// <summary>
    /// Copies databases (and optionally htdocs projects) from an XAMPP install into Kiln.
    ///
    /// How: XAMPP's own mysqld is started temporarily on a private port with --skip-grant-tables
    /// (so XAMPP's root password doesn't matter), its databases are dumped with Kiln's mariadb-dump,
    /// the temporary server is stopped, and the dump is loaded into Kiln's MariaDB. This is a logical
    /// copy, so it works across MariaDB versions (XAMPP ships 10.4; Kiln 12.x). XAMPP's files are only
    /// opened by XAMPP's own server, exactly as XAMPP itself would; nothing is moved or deleted.
    /// </summary>
    public class XamppImport : IDisposable
    {
        public static readonly string[] DefaultHtdocs = { "dashboard", "img", "webalizer", "xampp", "forbidden", "restricted" };

        public readonly string Root;          // e.g. C:\xampp
        public event Action<string> Progress;
        Process _server;
        int _port;
        string ErrLog { get { return Path.Combine(K.Logs, "xampp-import.err"); } }

        public XamppImport(string root) { Root = root.TrimEnd('\\', '/'); }

        public string MysqlDir { get { return Path.Combine(Root, "mysql"); } }
        public string DataDir { get { return Path.Combine(MysqlDir, "data"); } }
        public string Htdocs { get { return Path.Combine(Root, "htdocs"); } }
        string Mysqld
        {
            get
            {
                foreach (var n in new[] { "mysqld.exe", "mariadbd.exe" })
                {
                    var p = Path.Combine(MysqlDir, "bin", n);
                    if (File.Exists(p)) return p;
                }
                return null;
            }
        }

        void Report(string m) { Log.Info(m); var h = Progress; if (h != null) h(m); }

        // ------------------------------------------------------------------ discovery

        /// <summary>First XAMPP folder found on the usual drives, or null.</summary>
        public static string Find()
        {
            var candidates = new List<string>();
            foreach (var d in DriveInfo.GetDrives())
            {
                try { if (d.DriveType == DriveType.Fixed && d.IsReady) candidates.Add(Path.Combine(d.RootDirectory.FullName, "xampp")); }
                catch { }
            }
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "xampp"));
            return candidates.FirstOrDefault(c => IsXampp(c));
        }

        public static bool IsXampp(string root)
        {
            if (string.IsNullOrEmpty(root)) return false;
            return Directory.Exists(Path.Combine(root, "mysql", "data")) &&
                   (File.Exists(Path.Combine(root, "mysql", "bin", "mysqld.exe")) || File.Exists(Path.Combine(root, "mysql", "bin", "mariadbd.exe")));
        }

        /// <summary>XAMPP's MySQL is running right now (it must be stopped first: same files, same port).</summary>
        public bool XamppServerRunning()
        {
            return Sys.ProcessesUnder("mysqld", MysqlDir).Count > 0 || Sys.ProcessesUnder("mariadbd", MysqlDir).Count > 0;
        }

        /// <summary>Project folders in htdocs, without XAMPP's own demo folders.</summary>
        public List<string> HtdocsProjects()
        {
            if (!Directory.Exists(Htdocs)) return new List<string>();
            return Directory.GetDirectories(Htdocs).Select(Path.GetFileName)
                .Where(n => !DefaultHtdocs.Contains(n, StringComparer.OrdinalIgnoreCase))
                .OrderBy(n => n).ToList();
        }

        // ------------------------------------------------------------------ temporary XAMPP server

        static int FreePort()
        {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            int p = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return p;
        }

        /// <summary>
        /// Start XAMPP's mysqld privately. recovery 0 = normal; 1-3 = innodb_force_recovery level for a
        /// damaged XAMPP that won't start (levels 1-3 don't modify data files).
        /// </summary>
        public void StartServer(int recovery)
        {
            if (Mysqld == null) throw new FileNotFoundException("No mysqld.exe in " + Path.Combine(MysqlDir, "bin"));
            if (XamppServerRunning())
                throw new Exception("XAMPP's MySQL is running. Stop it in the XAMPP Control Panel first, then try again.");
            Directory.CreateDirectory(K.Logs);
            Directory.CreateDirectory(K.Pids);
            _port = FreePort();
            string ini = Path.Combine(MysqlDir, "bin", "my.ini");
            var args = new List<string>();
            if (File.Exists(ini)) args.Add("--defaults-file=\"" + K.Fwd(ini) + "\""); // must be first
            args.Add("--basedir=\"" + K.Fwd(MysqlDir) + "\"");
            args.Add("--datadir=\"" + K.Fwd(DataDir) + "\"");
            args.Add("--port=" + _port);
            args.Add("--bind-address=127.0.0.1");
            args.Add("--skip-grant-tables");
            args.Add("--log-error=\"" + K.Fwd(ErrLog) + "\"");
            args.Add("--pid-file=\"" + K.Fwd(Path.Combine(K.Pids, "xampp-import.pid")) + "\"");
            if (recovery > 0) args.Add("--innodb-force-recovery=" + recovery);

            Report("Starting XAMPP's MySQL temporarily" + (recovery > 0 ? " in recovery mode " + recovery : "") + "...");
            var psi = new ProcessStartInfo(Mysqld, string.Join(" ", args))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.Combine(MysqlDir, "bin")
            };
            _server = Process.Start(psi);
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 90000 && !_server.HasExited && !Sys.IsPortListening(_port)) Thread.Sleep(300);
            if (_server.HasExited || !Sys.IsPortListening(_port))
            {
                StopServer();
                throw new XamppStartException("XAMPP's MySQL didn't start" + (recovery > 0 ? " even in recovery mode " + recovery : "") +
                    ". Last lines of logs\\xampp-import.err:\n" + Sys.Tail(ErrLog, 12));
            }
        }

        /// <summary>Try normal start, then recovery levels 1-3 if allowed.</summary>
        public int StartServerWithRecovery(bool allowRecovery)
        {
            int[] levels = allowRecovery ? new[] { 0, 1, 2, 3 } : new[] { 0 };
            XamppStartException last = null;
            foreach (var level in levels)
            {
                try { StartServer(level); return level; }
                catch (XamppStartException e) { last = e; Log.Warn(e.Message); }
            }
            throw new Exception(last.Message + (allowRecovery ? "" :
                "\n\nIf XAMPP's MySQL crashed, tick \"XAMPP's MySQL is damaged\" and try again."));
        }

        Sys.RunResult Client(string exeNames, string args, int timeoutMs)
        {
            var exe = Installer.MariaExe(exeNames.Split('|'));
            if (exe == null) throw new FileNotFoundException("Kiln's MariaDB client tools are missing. Install MariaDB first.");
            var env = Services.Env();
            env["MYSQL_PWD"] = "";
            return Sys.Run(exe, "--protocol=TCP -h 127.0.0.1 -P " + _port + " -u root " + args, env, timeoutMs, K.MariaDbBin);
        }

        public List<string> Databases()
        {
            var r = Client("mariadb.exe|mysql.exe", "-N -e \"SHOW DATABASES\"", 60000);
            if (r.ExitCode != 0) throw new Exception("Couldn't list XAMPP's databases:\n" + r.Output.Trim());
            // XAMPP's phpMyAdmin keeps its own settings in "phpmyadmin"; "test" is an empty sample.
            return Backup.UserDatabases(r.Output)
                .Where(d => !d.Equals("phpmyadmin", StringComparison.OrdinalIgnoreCase)).ToList();
        }

        public string Dump(List<string> dbs)
        {
            Directory.CreateDirectory(K.Tmp);
            string file = Path.Combine(K.Tmp, "xampp-import-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".sql");
            Report("Copying " + dbs.Count + " database(s) out of XAMPP...");
            var r = Client("mariadb-dump.exe|mysqldump.exe",
                "--single-transaction --routines --events --triggers --hex-blob --default-character-set=utf8mb4 " +
                "--result-file=\"" + file + "\" --databases " + string.Join(" ", dbs.Select(d => "\"" + d + "\"")),
                2 * 60 * 60 * 1000);
            if (r.ExitCode != 0 || !File.Exists(file))
                throw new Exception("Copying from XAMPP failed:\n" + r.Output.Trim());
            return file;
        }

        public void StopServer()
        {
            if (_server == null) return;
            try
            {
                if (!_server.HasExited)
                {
                    Client("mariadb-admin.exe|mysqladmin.exe", "shutdown", 30000);
                    if (!_server.WaitForExit(30000)) Sys.KillTree(_server.Id);
                }
            }
            catch { try { Sys.KillTree(_server.Id); } catch { } }
            _server = null;
            Report("XAMPP's MySQL stopped again.");
        }

        public void Dispose() { StopServer(); }

        // ------------------------------------------------------------------ into Kiln

        /// <summary>Load a dump into Kiln's MariaDB (which must be running).</summary>
        public void ImportIntoKiln(KilnConfig cfg, string dumpFile)
        {
            if (!Services.MariaDb.IsRunning) Services.MariaDb.Start();
            // Safety net: back up Kiln's current databases before overwriting anything.
            try { Backup.Create(cfg, "before XAMPP import"); } catch (Exception e) { Log.Warn("Pre-import backup skipped: " + e.Message); }
            Report("Loading databases into Kiln...");
            var exe = Installer.MariaExe("mariadb.exe", "mysql.exe");
            var env = Services.Env();
            env["MYSQL_PWD"] = cfg.MysqlRootPassword ?? "";
            var r = Sys.Run(exe, "--protocol=TCP -h 127.0.0.1 -P " + cfg.MysqlPort + " -u root --default-character-set=utf8mb4 -e \"source " + K.Fwd(dumpFile) + "\"",
                env, 2 * 60 * 60 * 1000, K.MariaDbBin);
            if (r.ExitCode != 0) throw new Exception("Loading into Kiln failed:\n" + r.Output.Trim());
        }

        /// <summary>Copy htdocs projects into www\. Existing folders in www\ are skipped, never overwritten.</summary>
        public List<string> CopyProjects(IEnumerable<string> names, out List<string> skipped)
        {
            var copied = new List<string>();
            skipped = new List<string>();
            Directory.CreateDirectory(K.Www);
            foreach (var n in names)
            {
                string src = Path.Combine(Htdocs, n), dest = Path.Combine(K.Www, n);
                if (!Directory.Exists(src)) continue;
                if (Directory.Exists(dest)) { skipped.Add(n); continue; }
                Report("Copying htdocs\\" + n + " to www\\" + n + "...");
                CopyDir(src, dest);
                copied.Add(n);
            }
            return copied;
        }

        public static void CopyDir(string src, string dest)
        {
            Directory.CreateDirectory(dest);
            foreach (var f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(dest, Path.GetFileName(f)), false);
            foreach (var d in Directory.GetDirectories(src)) CopyDir(d, Path.Combine(dest, Path.GetFileName(d)));
        }

        /// <summary>Whole import in one call (used by kilnctl and the import window).</summary>
        public void Run(KilnConfig cfg, List<string> dbs, bool allowRecovery, IEnumerable<string> projects)
        {
            if (dbs != null && dbs.Count > 0)
            {
                string dump;
                StartServerWithRecovery(allowRecovery);
                try { dump = Dump(dbs); }
                finally { StopServer(); }
                try { ImportIntoKiln(cfg, dump); }
                finally { try { File.Delete(dump); } catch { } }
                Report("Imported databases: " + string.Join(", ", dbs));
            }
            if (projects != null)
            {
                List<string> skipped;
                var copied = CopyProjects(projects, out skipped);
                if (copied.Count > 0) Report("Copied projects: " + string.Join(", ", copied));
                if (skipped.Count > 0) Report("Skipped (already in www\\): " + string.Join(", ", skipped));
                if (copied.Count > 0) Vhosts.Write(cfg);
            }
        }
    }

    public class XamppStartException : Exception
    {
        public XamppStartException(string m) : base(m) { }
    }
}
