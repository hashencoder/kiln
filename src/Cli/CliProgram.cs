using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace WebHut
{
    /// <summary>whut.exe - command-line control for scripts and terminals.</summary>
    public static class CliProgram
    {
        const string Help = @"whut - control your WebHut stack from the command line

Usage:
  whut status                      Show every service
  whut start   [all|apache|mariadb|mysql|postgresql|mongodb|<node-app>]
  whut stop    [all|apache|mariadb|mysql|postgresql|mongodb|<node-app>]
  whut restart [all|apache|mariadb|mysql|postgresql|mongodb|<node-app>]
  whut install <mysql|postgresql|mongodb|mongosh|adminer> ...
                                   Add optional database engines and tools
                                      (MySQL 8.4 on port 3307, PostgreSQL on 5432,
                                      MongoDB on 27017, Adminer at /adminer)
  whut sites                       Regenerate vhosts for folders in www\ and Node apps
  whut php [<branch>]              Show or switch the active PHP version (e.g. 8.4)
  whut setup                       Download and install the core stack (first run)
  whut backup                      Back up all databases now (data\backups)
  whut backups                     List backups
  whut restore [<file>|latest]     Load a backup into the running MariaDB
  whut rebuild-db [<file>|latest]  MariaDB won't start after a crash: set the damaged
                                      data folder aside and rebuild it from a backup
  whut import-xampp [<folder>] [--recover] [--projects]
                                      Copy databases (and with --projects, htdocs folders)
                                      from XAMPP. Stop XAMPP's MySQL first.
  whut shell                       Open a console with php, node, npm and composer on PATH
  whut version

Default target is 'all'. 'mysql' means MariaDB until MySQL is installed.
Node apps are managed in WebHut.exe or node-apps.json.";

        public static int Main(string[] args)
        {
            Native.DontInheritStdHandles();
            try { if (Services.Cfg.NeedsPasswordMigration) Services.Cfg.Save(); } catch { }
            // Double-clicked in Explorer (no arguments, console window created just for us):
            // people expect the app, so open the control panel instead of flashing the help text.
            if (args.Length == 0 && Native.OwnsConsoleAlone())
            {
                var panel = System.IO.Path.Combine(K.Root, "WebHut.exe");
                if (System.IO.File.Exists(panel))
                {
                    try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(panel) { UseShellExecute = true, WorkingDirectory = K.Root }); return 0; }
                    catch { }
                }
            }
            Log.ToConsole = true;
            Sys.EnableModernTls();
            string cmd = args.Length > 0 ? args[0].ToLowerInvariant() : "help";
            string target = args.Length > 1 ? args[1] : "all";
            try
            {
                var problem = K.RootProblem();
                if (problem != null && cmd != "help" && cmd != "version") { Console.Error.WriteLine(problem); return 2; }
                new Installer(Services.Cfg).RelocateIfMoved();

                switch (cmd)
                {
                    case "status": Status(); return 0;
                    case "start": return Each(target, s => s.Start(), false);
                    case "stop": return Each(target, s => s.Stop(), true);
                    case "restart": return Each(target, s => s.Restart(), false);
                    case "sites":
                        Vhosts.Write(Services.Cfg);
                        Console.WriteLine("Vhosts regenerated. Restart Apache to apply: whut restart apache");
                        return 0;
                    case "php": return Php(args.Length > 1 ? args[1] : null);
                    case "setup": return RunSetup();
                    case "install": return InstallExtras(args.Skip(1).ToList());
                    case "check-db": return CheckDb(target);
                    case "shell": Services.OpenShell(); return 0;
                    case "backup":
                        if (!Services.MariaDb.IsRunning) Services.MariaDb.Start();
                        Console.WriteLine(Backup.Create(Services.Cfg, "manual") ?? "Nothing to back up yet.");
                        return 0;
                    case "backups":
                        foreach (var b in Backup.List()) Console.WriteLine(b.Label + "   " + b.Path);
                        return 0;
                    case "restore":
                        if (!Services.MariaDb.IsRunning) Services.MariaDb.Start();
                        Backup.Restore(Services.Cfg, PickBackup(args));
                        return 0;
                    case "rebuild-db":
                        {
                            var aside = Backup.Rebuild(Services.Cfg, PickBackup(args));
                            if (aside != null) Console.WriteLine("Damaged data kept in " + aside);
                            return 0;
                        }
                    case "import-xampp": return ImportXampp(args);
                    case "version": Console.WriteLine("WebHut " + K.AppVersion); return 0;
                    default: Console.WriteLine(Help); return cmd == "help" || cmd == "-h" || cmd == "--help" ? 0 : 1;
                }
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("Error: " + e.Message);
                return 1;
            }
        }

        /// <summary>whut install mysql postgresql mongodb mongosh adminer</summary>
        static int InstallExtras(List<string> ids)
        {
            ids = ids.Select(i => i.ToLowerInvariant()).Select(i => i == "postgres" || i == "pg" ? "postgresql" : i == "mongo" ? "mongodb" : i).Distinct().ToList();
            if (ids.Count == 0 || ids.Any(i => !Engines.IsOptional(i)))
            {
                Console.Error.WriteLine("Usage: whut install <" + string.Join("|", Engines.OptionalIds) + "> ...");
                return 1;
            }
            if (ids.Contains("mongodb") && !ids.Contains("mongosh") && !Engines.MongoshInstalled) ids.Add("mongosh");
            var inst = new Installer(Services.Cfg);
            int last = -100;
            inst.Progress += (msg, pct) =>
            {
                if (pct >= 0 && pct != 100 && pct != 0 && pct < last + 10) return;
                last = pct;
                Console.WriteLine("  " + msg);
            };
            inst.CreateFolders();
            int failures = 0;
            foreach (var id in ids)
            {
                var c = Engines.Resolve(Services.Cfg, id);
                if (!c.Ready) { failures++; Log.Error(c.Name + ": " + c.Error + " Download it by hand from " + c.Homepage); continue; }
                Console.WriteLine(c.Name + " " + c.Version + "  " + c.Url + (c.Sha256 != null ? "  (sha256 " + c.Sha256 + ")" : ""));
                try { inst.Install(c, CancellationToken.None); }
                catch (Exception e) { failures++; Log.Error(c.Name + ": " + e.Message); }
            }
            Services.Cfg.Save();
            if (ids.Contains("adminer") && Services.Apache.IsRunning) Console.WriteLine("Restart Apache to open http://localhost/adminer: whut restart apache");
            return failures == 0 ? 0 : 1;
        }

        /// <summary>Run a trivial query against a running engine (used by the Windows integration test).</summary>
        static int CheckDb(string target)
        {
            Sys.RunResult r;
            switch ((target ?? "").ToLowerInvariant())
            {
                case "mysql": r = Services.MySql.Sql("SELECT CONCAT('mysql-ok ', VERSION());"); break;
                case "postgresql": r = Services.Postgres.Sql("SELECT 'postgresql-ok ' || version();"); break;
                case "mongodb": r = Services.Mongo.Eval("print('mongodb-ok ' + db.version())"); break;
                default: Console.Error.WriteLine("check-db takes mysql, postgresql or mongodb"); return 1;
            }
            Console.WriteLine(r.Output.Trim());
            return r.ExitCode == 0 && r.Output.Contains("-ok ") ? 0 : 1;
        }

        static void Status()
        {
            Console.WriteLine("{0,-16} {1,-9} {2,-7} {3,-7} {4}", "SERVICE", "STATE", "PID", "PORT", "DETAIL");
            foreach (var s in Services.All())
            {
                int pid = s.Pid();
                Console.WriteLine("{0,-16} {1,-9} {2,-7} {3,-7} {4}", s.Title, pid > 0 ? "running" : "stopped",
                    pid > 0 ? pid.ToString() : "-", s.Port, s.Detail);
            }
        }

        static int Each(string target, Action<Service> act, bool reverse)
        {
            var list = target.Equals("all", StringComparison.OrdinalIgnoreCase)
                ? Services.All().Where(s => !(s is NodeService) || ((NodeService)s).App.AutoStart || reverse).ToList()
                : new[] { Services.Find(target) }.Where(s => s != null).ToList();
            if (list.Count == 0) { Console.Error.WriteLine("Unknown service: " + target); return 1; }
            if (reverse) list.Reverse();
            int failures = 0;
            foreach (var s in list)
            {
                try { act(s); }
                catch (Exception e) { failures++; Log.Error(s.Title + ": " + e.Message); }
            }
            return failures == 0 ? 0 : 1;
        }

        static string PickBackup(string[] args)
        {
            string arg = args.Length > 1 ? args[1] : "latest";
            if (!arg.Equals("latest", StringComparison.OrdinalIgnoreCase)) return System.IO.Path.GetFullPath(arg);
            var latest = Backup.List().FirstOrDefault();
            if (latest == null) throw new Exception("No backups in " + K.Backups);
            Console.WriteLine("Using " + latest.Path);
            return latest.Path;
        }

        static int ImportXampp(string[] args)
        {
            var rest = args.Skip(1).ToList();
            bool recover = rest.Remove("--recover");
            bool projects = rest.Remove("--projects");
            string root = rest.FirstOrDefault() ?? XamppImport.Find();
            if (root == null || !XamppImport.IsXampp(root)) { Console.Error.WriteLine("XAMPP not found. Pass its folder: whut import-xampp C:\\xampp"); return 1; }
            using (var x = new XamppImport(root))
            {
                var level = x.StartServerWithRecovery(recover);
                List<string> dbs;
                try { dbs = x.Databases(); }
                finally { x.StopServer(); }
                if (level > 0) Console.WriteLine("Note: XAMPP's MySQL only started in recovery mode " + level + ".");
                Console.WriteLine("Databases: " + (dbs.Count == 0 ? "(none)" : string.Join(", ", dbs)));
                var proj = projects ? x.HtdocsProjects() : new List<string>();
                if (projects) Console.WriteLine("Projects:  " + (proj.Count == 0 ? "(none)" : string.Join(", ", proj)));
                x.Run(Services.Cfg, dbs, recover, proj);
            }
            Console.WriteLine("Import complete.");
            return 0;
        }

        static int Php(string branch)
        {
            var installed = WebHutConfig.InstalledPhp();
            if (branch == null)
            {
                Console.WriteLine("Active PHP: " + Services.Cfg.Php);
                Console.WriteLine("Installed: " + string.Join(", ", installed));
                return 0;
            }
            if (!installed.Contains(branch))
            {
                Console.WriteLine("PHP " + branch + " is not installed. Downloading...");
                var c = Resolver.Php(Services.Cfg, branch);
                new Installer(Services.Cfg).Install(c, CancellationToken.None);
            }
            Services.Cfg.Php = branch;
            Services.Cfg.Save();
            Console.WriteLine("Active PHP is now " + branch + ".");
            if (Services.Apache.IsRunning) Services.Apache.Restart();
            return 0;
        }

        static int RunSetup()
        {
            Console.WriteLine("Finding the latest versions...");
            var comps = Setup.ResolveAll(Services.Cfg);
            foreach (var c in comps)
                Console.WriteLine("  {0,-18} {1}", c.Name, c.Error == null ? c.Version : "ERROR: " + c.Error);
            var bad = comps.Where(c => !c.Ready).ToList();
            if (bad.Count > 0)
            {
                Console.Error.WriteLine("Some components couldn't be resolved. Download them by hand from:");
                foreach (var c in bad) Console.Error.WriteLine("  " + c.Name + ": " + c.Homepage);
                Console.Error.WriteLine("then use the setup wizard in WebHut.exe ('Use a zip I downloaded').");
                return 1;
            }
            if (!Installer.VcRedistOk())
                Console.WriteLine("WARNING: Visual C++ Redistributable 2017-2026 (x64) is missing or old. Apache needs it: https://aka.ms/vc14/vc_redist.x64.exe");
            var inst = new Installer(Services.Cfg);
            int last = -10;
            inst.Progress += (m, p) => { if (p < 0 || p >= last + 10 || p == 100) { last = p; Console.WriteLine("  " + m); } };
            Setup.Run(inst, comps, CancellationToken.None);
            Console.WriteLine("Done. Start everything with: whut start");
            return 0;
        }
    }
}
