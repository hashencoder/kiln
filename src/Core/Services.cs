using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace Kiln
{
    public enum SvcState { Stopped, Starting, Running, Stopping }

    /// <summary>A server Kiln can start and stop: Apache, MariaDB or one Node app.</summary>
    public abstract class Service
    {
        public abstract string Key { get; }          // apache, mariadb, node:<name>
        public abstract string Title { get; }
        public abstract string Detail { get; }       // version / port text for the panel
        public abstract int Port { get; }
        public abstract string LogFile { get; }
        public abstract string ConfigFile { get; }
        public abstract string Url { get; }
        public abstract int Pid();                   // 0 when not running
        public abstract void Start();
        public abstract void Stop();
        public virtual void Restart() { Stop(); Start(); }
        public volatile bool Busy;

        public bool IsRunning { get { return Pid() > 0; } }

        protected KilnConfig Cfg { get { return Services.Cfg; } }

        protected void EnsurePortFree(int port, string what)
        {
            if (!Sys.IsPortListening(port)) return;
            throw new Exception("Port " + port + " is already in use by " + Sys.PortOwner(port) +
                ". Stop that program or change the " + what + " port in Settings.");
        }

        protected static bool WaitFor(Func<bool> cond, int timeoutMs)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (cond()) return true;
                Thread.Sleep(250);
            }
            return cond();
        }
    }

    public static class Services
    {
        public static KilnConfig Cfg = KilnConfig.Load();
        public static readonly ApacheService Apache = new ApacheService();
        public static readonly MariaDbService MariaDb = new MariaDbService();

        public static List<Service> All()
        {
            var list = new List<Service> { Apache, MariaDb };
            list.AddRange(NodeApp.LoadAll().Select(a => (Service)new NodeService(a)));
            return list;
        }

        public static Service Find(string key)
        {
            key = (key ?? "").ToLowerInvariant();
            if (key == "apache" || key == "httpd") return Apache;
            if (key == "mariadb" || key == "mysql" || key == "db") return MariaDb;
            if (key.StartsWith("node:")) key = key.Substring(5);
            return All().FirstOrDefault(s => s.Key == "node:" + key);
        }

        /// <summary>Environment for every child process: tools on PATH, npm kept inside Kiln.</summary>
        public static Dictionary<string, string> Env()
        {
            var path = new List<string>();
            if (!string.IsNullOrEmpty(Cfg.Php)) path.Add(Cfg.PhpDir);
            if (Cfg.NodeDir != null) { path.Add(Cfg.NodeDir); path.Add(Path.Combine(Cfg.NodeDir, "global")); }
            path.Add(K.MariaDbBin);
            path.Add(K.Tools);
            path.Add(Path.Combine(K.Apache, "bin"));
            var env = new Dictionary<string, string>
            {
                {"KILN_ROOT", K.RootFwd},
                {"PATH", string.Join(";", path.Where(Directory.Exists)) + ";" + Environment.GetEnvironmentVariable("PATH")},
                {"NPM_CONFIG_CACHE", Path.Combine(K.Tmp, "npm-cache")},
                {"COMPOSER_HOME", Path.Combine(K.Tmp, "composer")},
                {"MYSQL_HOST", "127.0.0.1"},
                {"MYSQL_TCP_PORT", Cfg.MysqlPort.ToString()}
            };
            if (Cfg.NodeDir != null) env["NPM_CONFIG_PREFIX"] = Path.Combine(Cfg.NodeDir, "global");
            return env;
        }

        public static void ApplyEnv(ProcessStartInfo psi)
        {
            foreach (var kv in Env()) psi.EnvironmentVariables[kv.Key] = kv.Value;
        }

        /// <summary>Open a console with php, node, npm, composer and mariadb on PATH.</summary>
        public static void OpenShell()
        {
            var psi = new ProcessStartInfo("cmd.exe",
                "/k \"title Kiln shell && echo Kiln shell - php, node, npm, npx, composer and mariadb are on PATH. && echo.\"")
            {
                UseShellExecute = false,
                CreateNoWindow = false,
                WorkingDirectory = K.Www
            };
            ApplyEnv(psi);
            Directory.CreateDirectory(K.Www);
            Process.Start(psi);
        }
    }

    // ====================================================================== Apache

    public class ApacheService : Service
    {
        public override string Key { get { return "apache"; } }
        public override string Title { get { return "Apache"; } }
        public override string Detail
        {
            get
            {
                string php = string.IsNullOrEmpty(Cfg.Php) ? "no PHP" : "PHP " + KilnConfig.FullVersion(Cfg.PhpDir);
                return KilnConfig.FullVersion(K.Apache).Split(' ')[0] + " · " + php;
            }
        }
        public override int Port { get { return Cfg.HttpPort; } }
        public override string LogFile { get { return Path.Combine(K.Logs, "apache-error.log"); } }
        public override string ConfigFile { get { return K.HttpdConf; } }
        public override string Url { get { return "http://localhost" + (Cfg.HttpPort == 80 ? "" : ":" + Cfg.HttpPort) + "/"; } }
        string PidFile { get { return Path.Combine(K.Pids, "httpd.pid"); } }

        public string Args()
        {
            var sb = new StringBuilder();
            sb.Append("-d \"").Append(K.Fwd(K.Apache)).Append("\" -f \"").Append(K.Fwd(K.HttpdConf)).Append('"');
            sb.Append(" -C \"Define KILN_ROOT ").Append(K.RootFwd).Append('"');
            sb.Append(" -C \"Define PHP_DIR ").Append(K.Fwd(Cfg.PhpDir)).Append('"');
            sb.Append(" -C \"Define KILN_HTTP_PORT ").Append(Cfg.HttpPort).Append('"');
            sb.Append(" -C \"Define KILN_MYSQL_PORT ").Append(Cfg.MysqlPort).Append('"');
            return sb.ToString();
        }

        public override int Pid()
        {
            int pid = Sys.LivePid(PidFile, "httpd");
            if (pid > 0) return pid;
            // The parent httpd is the oldest one running from our apache folder.
            var orphan = Sys.ProcessesUnder("httpd", K.Apache).OrderBy(p => p.StartTime).FirstOrDefault();
            return orphan == null ? 0 : orphan.Id;
        }

        /// <summary>Regenerate vhosts and run httpd -t. Throws with Apache's message on error.</summary>
        public void Check()
        {
            if (!File.Exists(K.Httpd)) throw new FileNotFoundException("Apache is not installed (missing " + K.Httpd + "). Run setup.");
            if (string.IsNullOrEmpty(Cfg.Php) || !File.Exists(Path.Combine(Cfg.PhpDir, "php8apache2_4.dll")))
                throw new FileNotFoundException("PHP " + Cfg.Php + " for Apache is missing (php8apache2_4.dll). Install a Thread Safe x64 PHP build in Settings.");
            var inst = new Installer(Cfg);
            inst.CreateFolders();
            inst.WriteConfigFiles(false);   // recreate any missing config file; never overwrites
            Vhosts.Write(Cfg);
            var r = Sys.Run(K.Httpd, Args() + " -t", Services.Env(), 30000, Path.Combine(K.Apache, "bin"));
            if (r.ExitCode == -1073741515 || r.ExitCode == -1073741701) // 0xC0000135 DLL not found, 0xC000007B bad image
                throw new Exception("Windows couldn't load Apache's runtime DLLs. Install the Microsoft Visual C++ Redistributable 2017-2026 (x64) from Settings, then try again.");
            if (r.ExitCode != 0)
            {
                string hint = "";
                if (r.Output.Contains("php8apache2_4.dll"))
                    hint = "\n\nPHP couldn't be loaded into Apache. Make sure php\\" + Cfg.Php + " is an x64 Thread Safe build (not NTS) and the Visual C++ Redistributable is up to date.";
                throw new Exception("Apache configuration test failed:\n" + r.Output.Trim() + hint);
            }
        }

        public override void Start()
        {
            if (IsRunning) { Log.Info("Apache is already running."); return; }
            Check();
            EnsurePortFree(Cfg.HttpPort, "HTTP");
            Log.Info("Starting Apache on port " + Cfg.HttpPort + " with PHP " + Cfg.Php + "...");
            var psi = new ProcessStartInfo(K.Httpd, Args())
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.Combine(K.Apache, "bin")
            };
            Services.ApplyEnv(psi);
            var p = Process.Start(psi);
            bool up = WaitFor(() => p.HasExited || Sys.IsPortListening(Cfg.HttpPort), 15000);
            // A server can open its port and then abort during startup; make sure it stays up.
            if (up && !p.HasExited) WaitFor(() => p.HasExited, 1500);
            if (p.HasExited || !up)
                throw new Exception("Apache didn't start. Last lines of apache-error.log:\n" + Sys.Tail(LogFile, 15));
            Log.Info("Apache is running: " + Url);
        }

        public override void Stop()
        {
            int pid = Pid();
            if (pid == 0) { Log.Info("Apache is not running."); return; }
            Log.Info("Stopping Apache...");
            // A console (non-service) Apache on Windows is stopped through the named event its parent
            // process waits on: "ap<parent pid>_shutdown". ("httpd -k shutdown" only talks to services.)
            if (!Native.Signal("ap" + pid + "_shutdown"))
                Log.Warn("Couldn't signal Apache to shut down gracefully; it will be ended.");
            if (!WaitFor(() => Pid() == 0, 15000))
            {
                Log.Warn("Apache didn't stop cleanly; ending its processes.");
                Sys.KillTree(pid);
                foreach (var p in Sys.ProcessesUnder("httpd", K.Apache)) Sys.KillTree(p.Id);
            }
            try { File.Delete(PidFile); } catch { }
            Log.Info("Apache stopped.");
        }

        public override void Restart()
        {
            if (!IsRunning) { Start(); return; }
            Check();
            Log.Info("Restarting Apache...");
            // The PHP version and ports are passed on the command line, so a full stop/start is needed
            // (the "ap<pid>_restart" event would reuse the old command line).
            Stop();
            Start();
        }
    }

    // ====================================================================== MariaDB

    public class MariaDbService : Service
    {
        public override string Key { get { return "mariadb"; } }
        public override string Title { get { return "MariaDB"; } }
        public override string Detail { get { return KilnConfig.FullVersion(K.MariaDb); } }
        public override int Port { get { return Cfg.MysqlPort; } }
        public override string LogFile { get { return Path.Combine(K.Logs, "mariadb.err"); } }
        public override string ConfigFile { get { return K.MyIni; } }
        public override string Url { get { return "http://localhost" + (Cfg.HttpPort == 80 ? "" : ":" + Cfg.HttpPort) + "/phpmyadmin/"; } }
        string PidFile { get { return Path.Combine(K.Pids, "mariadb.pid"); } }
        string PortFile { get { return Path.Combine(K.Pids, "mariadb.port"); } }

        /// <summary>The port the running server was started on (may differ from settings after a port change).</summary>
        int RunningPort
        {
            get
            {
                int port;
                try { if (File.Exists(PortFile) && int.TryParse(File.ReadAllText(PortFile).Trim(), out port)) return port; } catch { }
                return Cfg.MysqlPort;
            }
        }

        string Server { get { return Installer.MariaExe("mariadbd.exe", "mysqld.exe"); } }

        public override int Pid()
        {
            int pid = Sys.LivePid(PidFile, "mariadbd", "mysqld");
            if (pid > 0) return pid;
            var orphan = Sys.ProcessesUnder("mariadbd", K.MariaDb).Concat(Sys.ProcessesUnder("mysqld", K.MariaDb)).FirstOrDefault();
            return orphan == null ? 0 : orphan.Id;
        }

        public override void Start()
        {
            if (IsRunning) { Log.Info("MariaDB is already running."); return; }
            if (Server == null) throw new FileNotFoundException("MariaDB is not installed (no mariadbd.exe in " + K.MariaDbBin + "). Run setup.");
            var inst = new Installer(Cfg);
            inst.CreateFolders();
            inst.WriteConfigFiles(false);
            if (!Directory.Exists(K.MariaDbData) || !Directory.EnumerateFileSystemEntries(K.MariaDbData).Any())
                inst.InitMariaDb();
            EnsurePortFree(Cfg.MysqlPort, "MySQL");
            Directory.CreateDirectory(K.Logs);
            Directory.CreateDirectory(K.Pids);
            Log.Info("Starting MariaDB on port " + Cfg.MysqlPort + "...");
            string args = "--defaults-file=\"" + K.Fwd(K.MyIni) + "\"" +
                          " --datadir=\"" + K.Fwd(K.MariaDbData) + "\"" +
                          " --port=" + Cfg.MysqlPort +
                          " --bind-address=127.0.0.1" +
                          " --log-error=\"" + K.Fwd(LogFile) + "\"" +
                          " --pid-file=\"" + K.Fwd(PidFile) + "\"";
            var psi = new ProcessStartInfo(Server, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = K.MariaDbBin
            };
            Services.ApplyEnv(psi);
            var p = Process.Start(psi);
            bool up = WaitFor(() => p.HasExited || Sys.IsPortListening(Cfg.MysqlPort), 30000);
            // MariaDB opens its port before finishing startup and can still abort; make sure it stays up.
            if (up && !p.HasExited) WaitFor(() => p.HasExited, 2000);
            if (p.HasExited || !up)
                throw new Exception("MariaDB didn't start. Last lines of mariadb.err:\n" + Sys.Tail(LogFile, 15));
            if (!File.Exists(PidFile)) File.WriteAllText(PidFile, p.Id.ToString());
            File.WriteAllText(PortFile, Cfg.MysqlPort.ToString());
            Log.Info("MariaDB is running on 127.0.0.1:" + Cfg.MysqlPort);
        }

        public override void Stop() { Stop(true); }

        /// <summary>Clean stop. With backupFirst, takes an automatic backup first (if due and enabled).</summary>
        public void Stop(bool backupFirst) { Stop(backupFirst, 20000); }

        /// <summary>
        /// Fast clean stop for Windows shutdown/logoff: no backup, shorter waits. A clean stop flushes
        /// everything to disk, so the next start needs no crash recovery at all.
        /// </summary>
        public void StopForShutdown() { Stop(false, 12000); }

        void Stop(bool backupFirst, int waitMs)
        {
            int pid = Pid();
            if (pid == 0) { Log.Info("MariaDB is not running."); return; }
            if (backupFirst) Backup.CreateIfDue(Cfg, TimeSpan.FromMinutes(15), "before stopping");
            Log.Info("Stopping MariaDB...");
            var admin = Installer.MariaExe("mariadb-admin.exe", "mysqladmin.exe");
            if (admin != null)
            {
                var env = Services.Env();
                env["MYSQL_PWD"] = Cfg.MysqlRootPassword ?? "";
                var r = Sys.Run(admin, "--protocol=TCP -h 127.0.0.1 -P " + RunningPort + " -u root shutdown", env, 30000, K.MariaDbBin);
                if (r.ExitCode != 0) Log.Warn("mariadb-admin shutdown: " + r.Output.Trim());
            }
            if (!WaitFor(() => Pid() == 0, waitMs))
            {
                Log.Warn("MariaDB didn't stop cleanly; ending its process (InnoDB will recover on next start).");
                Sys.KillTree(pid);
            }
            try { File.Delete(PidFile); File.Delete(PortFile); } catch { }
            Log.Info("MariaDB stopped.");
        }

        /// <summary>Run SQL as root with the mariadb client.</summary>
        public Sys.RunResult Sql(string sql, string password)
        {
            var client = Installer.MariaExe("mariadb.exe", "mysql.exe");
            if (client == null) throw new FileNotFoundException("mariadb.exe not found.");
            var env = Services.Env();
            env["MYSQL_PWD"] = password ?? "";
            return Sys.Run(client, "--protocol=TCP -h 127.0.0.1 -P " + Cfg.MysqlPort + " -u root -e \"" + sql.Replace("\"", "\\\"") + "\"",
                env, 30000, K.MariaDbBin);
        }

        /// <summary>Change root's password for every root account (localhost, 127.0.0.1, ::1).</summary>
        public void ChangeRootPassword(string oldPw, string newPw)
        {
            if (!IsRunning) throw new Exception("Start MariaDB first.");
            string esc = (newPw ?? "").Replace("\\", "\\\\").Replace("'", "\\'");
            string sql = "ALTER USER IF EXISTS 'root'@'localhost' IDENTIFIED BY '" + esc + "'; " +
                         "ALTER USER IF EXISTS 'root'@'127.0.0.1' IDENTIFIED BY '" + esc + "'; " +
                         "ALTER USER IF EXISTS 'root'@'::1' IDENTIFIED BY '" + esc + "'; FLUSH PRIVILEGES;";
            var r = Sql(sql, oldPw);
            if (r.ExitCode != 0) throw new Exception("Couldn't change the root password:\n" + r.Output.Trim());
        }
    }

    // ====================================================================== Node apps

    public class NodeService : Service
    {
        public readonly NodeApp App;
        public NodeService(NodeApp app) { App = app; }

        public override string Key { get { return "node:" + App.Name; } }
        public override string Title { get { return App.Name; } }
        public override string Detail { get { return "Node " + (Cfg.Node ?? "?") + " · " + App.Command; } }
        public override int Port { get { return App.Port; } }
        public override string LogFile { get { return Path.Combine(K.Logs, "node-" + App.Name + ".log"); } }
        public override string ConfigFile { get { return K.NodeAppsFile; } }
        public override string Url { get { return "http://" + App.Host + (Cfg.HttpPort == 80 ? "" : ":" + Cfg.HttpPort) + "/"; } }
        string PidFile { get { return Path.Combine(K.Pids, "node-" + App.Name + ".pid"); } }

        public override int Pid()
        {
            return Sys.LivePid(PidFile, "cmd");
        }

        public override void Start()
        {
            if (IsRunning) { Log.Info(App.Name + " is already running."); return; }
            if (Cfg.NodeDir == null || !File.Exists(Path.Combine(Cfg.NodeDir, "node.exe")))
                throw new FileNotFoundException("Node.js is not installed. Install it in Settings.");
            if (!Directory.Exists(App.FullDir)) throw new DirectoryNotFoundException("App folder not found: " + App.FullDir);
            EnsurePortFree(App.Port, App.Name);
            Directory.CreateDirectory(K.Logs);
            Directory.CreateDirectory(K.Pids);

            File.AppendAllText(LogFile, Environment.NewLine + "==== " + DateTime.Now + "  " + App.Command + " (PORT=" + App.Port + ") ====" + Environment.NewLine);
            // cmd.exe owns the redirection, so the app keeps running (and logging) after Kiln closes.
            // Parentheses so "npm install && npm run dev" logs both commands.
            var psi = new ProcessStartInfo("cmd.exe", "/d /s /c \"(" + App.Command + ") >> \"" + LogFile + "\" 2>&1\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = App.FullDir
            };
            Services.ApplyEnv(psi);
            psi.EnvironmentVariables["PORT"] = App.Port.ToString();
            psi.EnvironmentVariables["NODE_ENV"] = App.Env.ContainsKey("NODE_ENV") ? App.Env["NODE_ENV"] : "development";
            psi.EnvironmentVariables["BROWSER"] = "none"; // stop dev servers opening tabs on their own
            foreach (var kv in App.Env) psi.EnvironmentVariables[kv.Key] = kv.Value;

            Log.Info("Starting " + App.Name + ": " + App.Command + " on port " + App.Port + "...");
            var p = Process.Start(psi);
            File.WriteAllText(PidFile, p.Id.ToString());
            Thread.Sleep(1500);
            if (p.HasExited)
                throw new Exception(App.Name + " exited right away. Last lines of its log:\n" + Sys.Tail(LogFile, 15));
            if (WaitFor(() => p.HasExited || Sys.IsPortListening(App.Port), 20000) && !p.HasExited)
                Log.Info(App.Name + " is running: " + Url);
            else if (p.HasExited)
                throw new Exception(App.Name + " stopped. Last lines of its log:\n" + Sys.Tail(LogFile, 15));
            else
                Log.Warn(App.Name + " is running but nothing is listening on port " + App.Port + " yet. Make sure the app reads process.env.PORT (or set the port in its command).");
        }

        public override void Stop()
        {
            int pid = Pid();
            if (pid == 0) { Log.Info(App.Name + " is not running."); return; }
            Log.Info("Stopping " + App.Name + "...");
            Sys.KillTree(pid);
            WaitFor(() => Pid() == 0, 5000);
            try { File.Delete(PidFile); } catch { }
            Log.Info(App.Name + " stopped.");
        }
    }
}
