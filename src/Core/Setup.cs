using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WebHut
{
    /// <summary>First-run setup shared by the wizard and webhut.</summary>
    public static class Setup
    {
        /// <summary>Resolve every component in parallel (network calls).</summary>
        public static List<Component> ResolveAll(WebHutConfig cfg)
        {
            var jobs = new List<Func<Component>>
            {
                () => Resolver.Apache(cfg),
                () => Resolver.Php(cfg, null),
                () => Resolver.MariaDb(cfg),
                () => Resolver.PhpMyAdmin(cfg),
                () => Resolver.Node(cfg, null),
                () => Resolver.Composer(cfg),
                () => Resolver.CaCert(cfg)
            };
            var tasks = jobs.Select(j => Task.Run(() =>
            {
                try { return j(); }
                catch (Exception e) { return new Component { Name = "?", Error = e.Message }; }
            })).ToArray();
            Task.WaitAll(tasks);
            return tasks.Select(t => t.Result).ToList();
        }

        /// <summary>Install the selected components, write config, initialise MariaDB.</summary>
        public static void Run(Installer inst, List<Component> components, CancellationToken ct)
        {
            var cfg = inst.Cfg;
            inst.CreateFolders();
            // CA bundle first: php.ini points at it.
            foreach (var c in components.OrderBy(c => c.Id == "cacert" ? 0 : 1))
            {
                ct.ThrowIfCancellationRequested();
                inst.Install(c, ct);
            }
            inst.WriteConfigFiles(false);
            if (Installer.MariaExe("mariadbd.exe", "mysqld.exe") != null) inst.InitMariaDb();
            cfg.LastRoot = K.RootFwd;
            cfg.Save();
            if (!File.Exists(K.NodeAppsFile)) NodeApp.SaveAll(new List<NodeApp>());
            Vhosts.Write(cfg);
            Services.Cfg = cfg;
            Log.Info("Setup complete. WebHut lives in " + K.Root);
        }
    }
}
