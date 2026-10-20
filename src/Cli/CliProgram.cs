using System;
using System.Linq;
using System.Threading;

namespace Kiln
{
    /// <summary>kilnctl.exe - command-line control for scripts and terminals.</summary>
    public static class CliProgram
    {
        const string Help = @"kilnctl - control your Kiln stack from the command line

Usage:
  kilnctl status                      Show every service
  kilnctl start   [all|apache|mariadb|<node-app>]
  kilnctl stop    [all|apache|mariadb|<node-app>]
  kilnctl restart [all|apache|mariadb|<node-app>]
  kilnctl sites                       Regenerate vhosts for folders in www\ and Node apps
  kilnctl php [<branch>]              Show or switch the active PHP version (e.g. 8.4)
  kilnctl setup                       Download and install everything (first run)
  kilnctl shell                       Open a console with php, node, npm and composer on PATH
  kilnctl version

Default target is 'all'. Node apps are managed in Kiln.exe or node-apps.json.";

        public static int Main(string[] args)
        {
            Native.DontInheritStdHandles();
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
                        Console.WriteLine("Vhosts regenerated. Restart Apache to apply: kilnctl restart apache");
                        return 0;
                    case "php": return Php(args.Length > 1 ? args[1] : null);
                    case "setup": return RunSetup();
                    case "shell": Services.OpenShell(); return 0;
                    case "version": Console.WriteLine("Kiln " + K.AppVersion); return 0;
                    default: Console.WriteLine(Help); return cmd == "help" || cmd == "-h" || cmd == "--help" ? 0 : 1;
                }
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("Error: " + e.Message);
                return 1;
            }
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

        static int Php(string branch)
        {
            var installed = KilnConfig.InstalledPhp();
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
                Console.Error.WriteLine("then use the setup wizard in Kiln.exe ('Use a zip I downloaded').");
                return 1;
            }
            if (!Installer.VcRedistOk())
                Console.WriteLine("WARNING: Visual C++ Redistributable 2017-2026 (x64) is missing or old. Apache needs it: https://aka.ms/vc14/vc_redist.x64.exe");
            var inst = new Installer(Services.Cfg);
            int last = -10;
            inst.Progress += (m, p) => { if (p < 0 || p >= last + 10 || p == 100) { last = p; Console.WriteLine("  " + m); } };
            Setup.Run(inst, comps, CancellationToken.None);
            Console.WriteLine("Done. Start everything with: kilnctl start");
            return 0;
        }
    }
}
