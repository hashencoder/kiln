using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ServNook
{
    /// <summary>First-run setup shared by the wizard and snook.</summary>
    public static class Setup
    {
        /// <summary>Resolve the core components in parallel (network calls).</summary>
        public static List<Component> ResolveAll(ServNookConfig cfg) { return ResolveAll(cfg, false); }

        /// <summary>Same, plus the optional engines (MySQL, PostgreSQL, MongoDB, mongosh, Adminer) when asked.</summary>
        public static List<Component> ResolveAll(ServNookConfig cfg, bool withOptional)
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
            if (withOptional) jobs.AddRange(Engines.Resolvers(cfg));   // shown unticked in the setup window
            var tasks = jobs.Select(j => Task.Run(() =>
            {
                try { return j(); }
                catch (Exception e) { return new Component { Name = "?", Error = e.Message }; }
            })).ToArray();
            Task.WaitAll(tasks);
            var list = tasks.Select(t => t.Result).ToList();
            foreach (var c in list)
                if (c.Error != null) Log.Warn("Setup lookup - " + c.Name + ": " + c.Error);
                else Log.Info("Setup lookup - " + c.Name + " " + c.Version + " (" + c.Url + ")");
            return list;
        }

        /// <summary>Install the selected components, write config, initialise MariaDB.</summary>
        public static void Run(Installer inst, List<Component> components, CancellationToken ct)
        {
            Run(inst, components, ct, null);
        }

        /// <summary>Same, reporting each component's stage ("start", "done", "failed") and the final "configure" step (component null).</summary>
        public static void Run(Installer inst, List<Component> components, CancellationToken ct, Action<Component, string> stage)
        {
            Action<Component, string> report = (c, st) => { if (stage != null) stage(c, st); };
            var cfg = inst.Cfg;
            inst.CreateFolders();
            // CA bundle first: php.ini points at it. PHP before Adminer/PostgreSQL, which enable drivers in php.ini.
            foreach (var c in components.OrderBy(c => c.Id == "cacert" ? 0 : Engines.IsOptional(c.Id) ? 2 : 1))
            {
                ct.ThrowIfCancellationRequested();
                report(c, "start");
                try { inst.Install(c, ct); }
                catch { report(c, "failed"); throw; }
                report(c, "done");
            }
            report(null, "configure");
            inst.WriteConfigFiles(false);
            if (Installer.MariaExe("mariadbd.exe", "mysqld.exe") != null) inst.InitMariaDb();
            cfg.LastRoot = K.RootFwd;
            cfg.Save();
            if (!File.Exists(K.NodeAppsFile)) NodeApp.SaveAll(new List<NodeApp>());
            Vhosts.Write(cfg);
            Services.Cfg = cfg;
            Log.Info("Setup complete. ServNook lives in " + K.Root);
        }
    }
}
