using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace WebHut
{
    /// <summary>webhut.json - user settings.</summary>
    public class WebHutConfig
    {
        public string Php = "";
        public string Node = "";
        public int HttpPort = 80;
        public int MysqlPort = 3306;
        public string MysqlRootPassword = "";
        public bool StartOnLaunch = false;
        public bool MinimizeToTray = true;
        public string LastRoot = "";
        public string ManifestUrl = "";
        public bool LanAccess = false;       // let phones/PCs on the local network open the sites
        public bool AutoBackup = true;       // back up databases on stop and every BackupEveryHours
        public int BackupEveryHours = 2;
        public int BackupKeep = 10;
        public string Theme = "system";      // system | light | dark
        public string LastUpdateCheck = "";  // ISO time of the last component update check

        public static WebHutConfig Load()
        {
            var c = new WebHutConfig();
            try
            {
                if (File.Exists(K.ConfigFile))
                {
                    var o = Json.Parse(File.ReadAllText(K.ConfigFile));
                    c.Php = Json.Str(o, "php") ?? "";
                    c.Node = Json.Str(o, "node") ?? "";
                    c.HttpPort = ToInt(Json.Get(o, "httpPort"), 80);
                    c.MysqlPort = ToInt(Json.Get(o, "mysqlPort"), 3306);
                    c.MysqlRootPassword = Json.Str(o, "mysqlRootPassword") ?? "";
                    c.StartOnLaunch = Json.Get(o, "startOnLaunch") as bool? ?? false;
                    c.MinimizeToTray = Json.Get(o, "minimizeToTray") as bool? ?? true;
                    c.LastRoot = Json.Str(o, "lastRoot") ?? "";
                    c.ManifestUrl = Json.Str(o, "manifestUrl") ?? "";
                    c.AutoBackup = Json.Get(o, "autoBackup") as bool? ?? true;
                    c.LanAccess = Json.Get(o, "lanAccess") as bool? ?? false;
                    c.BackupEveryHours = Math.Max(0, ToInt(Json.Get(o, "backupEveryHours"), 2));
                    c.BackupKeep = Math.Max(1, ToInt(Json.Get(o, "backupKeep"), 10));
                    c.Theme = NormTheme(Json.Str(o, "theme"));
                    c.LastUpdateCheck = Json.Str(o, "lastUpdateCheck") ?? "";
                }
            }
            catch (Exception e) { Log.Error("Couldn't read webhut.json, using defaults: " + e.Message); }

            // Fall back to whatever is actually installed.
            var phps = InstalledPhp();
            if (!phps.Contains(c.Php) && phps.Count > 0) c.Php = phps[0];
            var nodes = InstalledNode();
            if (!nodes.Contains(c.Node) && nodes.Count > 0) c.Node = nodes[0];
            return c;
        }

        public void Save()
        {
            var d = new Dictionary<string, object>
            {
                {"php", Php}, {"node", Node}, {"httpPort", HttpPort}, {"mysqlPort", MysqlPort},
                {"mysqlRootPassword", MysqlRootPassword}, {"startOnLaunch", StartOnLaunch},
                {"minimizeToTray", MinimizeToTray}, {"lastRoot", LastRoot}, {"manifestUrl", ManifestUrl},
                {"lanAccess", LanAccess}, {"autoBackup", AutoBackup}, {"backupEveryHours", BackupEveryHours}, {"backupKeep", BackupKeep},
                {"theme", Theme}, {"lastUpdateCheck", LastUpdateCheck}
            };
            File.WriteAllText(K.ConfigFile, Json.Pretty(d));
        }

        public string PhpDir { get { return K.PhpDir(Php); } }
        public string NodeDir { get { return string.IsNullOrEmpty(Node) ? null : K.NodeDir(Node); } }

        public static string NormTheme(string t)
        {
            t = (t ?? "").Trim().ToLowerInvariant();
            return t == "light" || t == "dark" ? t : "system";
        }

        static int ToInt(object v, int dflt)
        {
            try { return v == null ? dflt : Convert.ToInt32(v); } catch { return dflt; }
        }

        /// <summary>PHP branches installed under php\, newest first, e.g. ["8.5","8.4"].</summary>
        public static List<string> InstalledPhp()
        {
            if (!Directory.Exists(K.PhpRoot)) return new List<string>();
            return Directory.GetDirectories(K.PhpRoot)
                .Where(d => File.Exists(Path.Combine(d, "php.exe")))
                .Select(Path.GetFileName)
                .OrderByDescending(VersionKey).ToList();
        }

        public static List<string> InstalledNode()
        {
            if (!Directory.Exists(K.NodeRoot)) return new List<string>();
            return Directory.GetDirectories(K.NodeRoot)
                .Where(d => File.Exists(Path.Combine(d, "node.exe")))
                .Select(Path.GetFileName)
                .OrderByDescending(VersionKey).ToList();
        }

        public static Version VersionKey(string s)
        {
            Version v;
            var m = Regex.Match(s ?? "", @"\d+(\.\d+){0,3}");
            return m.Success && Version.TryParse(m.Value.Contains(".") ? m.Value : m.Value + ".0", out v) ? v : new Version(0, 0);
        }

        /// <summary>Full version recorded at install time (e.g. 8.5.11), or the folder name.</summary>
        public static string FullVersion(string dir)
        {
            try
            {
                var f = Path.Combine(dir, ".webhut-version");
                if (File.Exists(f)) return File.ReadAllText(f).Trim();
            }
            catch { }
            return Path.GetFileName(dir);
        }
    }

    /// <summary>One Node.js app managed by WebHut (node-apps.json).</summary>
    public class NodeApp
    {
        public string Name = "";
        public string Dir = "";
        public string Command = "npm run dev";
        public int Port = 3000;
        public bool AutoStart = false;
        public Dictionary<string, string> Env = new Dictionary<string, string>();

        public static readonly Regex NameRule = new Regex("^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?$");

        /// <summary>Dir may be relative to the WebHut root ("www/api") or absolute.</summary>
        public string FullDir
        {
            get
            {
                if (string.IsNullOrEmpty(Dir)) return K.Www;
                return Path.IsPathRooted(Dir) ? Dir : Path.GetFullPath(Path.Combine(K.Root, Dir));
            }
        }

        public string Host { get { return Name + ".localhost"; } }

        public static List<NodeApp> LoadAll()
        {
            var list = new List<NodeApp>();
            try
            {
                if (!File.Exists(K.NodeAppsFile)) return list;
                var o = Json.Parse(File.ReadAllText(K.NodeAppsFile));
                foreach (var a in Json.Arr(Json.Get(o, "apps")))
                {
                    var app = new NodeApp
                    {
                        Name = (Json.Str(a, "name") ?? "").Trim().ToLowerInvariant(),
                        Dir = Json.Str(a, "dir") ?? "",
                        Command = Json.Str(a, "command") ?? "npm run dev",
                        AutoStart = Json.Get(a, "autostart") as bool? ?? false
                    };
                    int port;
                    if (int.TryParse(Json.Str(a, "port"), out port)) app.Port = port;
                    var env = Json.Obj(Json.Get(a, "env"));
                    if (env != null) foreach (var kv in env) app.Env[kv.Key] = Convert.ToString(kv.Value);
                    if (NameRule.IsMatch(app.Name)) list.Add(app);
                    else Log.Warn("Skipping Node app with invalid name '" + app.Name + "' in node-apps.json");
                }
            }
            catch (Exception e) { Log.Error("Couldn't read node-apps.json: " + e.Message); }
            return list;
        }

        public static void SaveAll(List<NodeApp> apps)
        {
            var arr = apps.Select(a => (object)new Dictionary<string, object>
            {
                {"name", a.Name}, {"dir", a.Dir}, {"command", a.Command}, {"port", a.Port},
                {"autostart", a.AutoStart}, {"env", a.Env.ToDictionary(kv => kv.Key, kv => (object)kv.Value)}
            }).ToList();
            File.WriteAllText(K.NodeAppsFile, Json.Pretty(new Dictionary<string, object> { { "apps", arr } }));
        }
    }
}
