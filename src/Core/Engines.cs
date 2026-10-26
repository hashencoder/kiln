using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace ServNook
{
    /// <summary>
    /// Optional database engines next to MariaDB: MySQL (8.4 LTS), PostgreSQL and MongoDB (with mongosh),
    /// plus Adminer (a one-file web UI for MySQL and PostgreSQL). None of them is installed unless the user picks it.
    /// Every server listens on 127.0.0.1 only, keeps its data under data\&lt;engine&gt;, and is started and stopped like
    /// Apache and MariaDB.
    /// </summary>
    public static class Engines
    {
        /// <summary>Component ids that are offered but never installed by default.</summary>
        public static readonly string[] OptionalIds = { "mysql", "postgresql", "mongodb", "mongosh", "adminer" };

        public static bool IsOptional(string id) { return OptionalIds.Contains(id); }

        // MySQL 8.4 is the Long Term Support series (supported into 2032). Patch releases are probed upward from here.
        public const string MySqlSeries = "8.4";
        public const int MySqlBaselinePatch = 6;

        public const string PgAdminUrl = "https://www.pgadmin.org/download/pgadmin-4-windows/";
        public const string CompassUrl = "https://www.mongodb.com/try/download/compass";

        public static bool MySqlInstalled { get { return File.Exists(Path.Combine(K.MySqlBin, "mysqld.exe")); } }
        public static bool PostgresInstalled { get { return File.Exists(Path.Combine(K.PgBin, "pg_ctl.exe")); } }
        public static bool MongoInstalled { get { return File.Exists(Path.Combine(K.MongoBin, "mongod.exe")); } }
        public static bool MongoshInstalled { get { return File.Exists(Path.Combine(K.MongoshBin, "mongosh.exe")); } }
        public static bool AdminerInstalled { get { return File.Exists(Path.Combine(K.Adminer, "adminer.php")); } }

        public static bool Installed(string id)
        {
            switch (id)
            {
                case "mysql": return MySqlInstalled;
                case "postgresql": return PostgresInstalled;
                case "mongodb": return MongoInstalled;
                case "mongosh": return MongoshInstalled;
                case "adminer": return AdminerInstalled;
            }
            return false;
        }

        /// <summary>Resolve the optional components in the order they are shown.</summary>
        public static List<Func<Component>> Resolvers(ServNookConfig cfg)
        {
            return new List<Func<Component>>
            {
                () => MySql(cfg), () => Postgres(cfg), () => Mongo(cfg), () => Mongosh(cfg), () => AdminerComponent(cfg)
            };
        }

        public static Component Resolve(ServNookConfig cfg, string id)
        {
            switch (id)
            {
                case "mysql": return MySql(cfg);
                case "postgresql": return Postgres(cfg);
                case "mongodb": return Mongo(cfg);
                case "mongosh": return Mongosh(cfg);
                case "adminer": return AdminerComponent(cfg);
            }
            throw new ArgumentException("Unknown component: " + id);
        }

        // ================================================================== resolving

        static Component Try(Component c, Action<Component> live)
        {
            try
            {
                live(c);
                if (c.Url == null && c.Error == null) c.Error = "Couldn't find a download.";
            }
            catch (Exception e) { c.Error = e.Message; }
            if (c.Error != null) Log.Warn(c.Name + ": " + c.Error);
            return c;
        }

        /// <summary>True when the URL exists: a HEAD request, or (for hosts that refuse HEAD) a one-byte ranged GET.</summary>
        public static Func<string, bool> UrlExists = url => Probe(url, "HEAD") || Probe(url, "GET");

        static bool Probe(string url, string method)
        {
            try
            {
                Sys.EnableModernTls();
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = method;
                if (method == "GET") req.AddRange(0, 0);
                req.UserAgent = "ServNook/" + K.AppVersion + " (portable dev stack for Windows)";
                req.Timeout = 15000;
                req.AllowAutoRedirect = true;
                using (var resp = (HttpWebResponse)req.GetResponse())
                {
                    int code = (int)resp.StatusCode;
                    // A redirect to an HTML page (login or error page) doesn't count as the file.
                    bool html = (resp.ContentType ?? "").StartsWith("text/html", StringComparison.OrdinalIgnoreCase);
                    return (code == 200 || code == 206) && !html;
                }
            }
            catch { return false; }
        }

        /// <summary>Oracle's CDN: the newest release of a series sits in /Downloads, older ones in /archives.</summary>
        public static string[] MySqlUrls(string version)
        {
            return new[]
            {
                "https://cdn.mysql.com/Downloads/MySQL-" + MySqlSeries + "/mysql-" + version + "-winx64.zip",
                "https://cdn.mysql.com/archives/mysql-" + MySqlSeries + "/mysql-" + version + "-winx64.zip",
                "https://dev.mysql.com/get/Downloads/MySQL-" + MySqlSeries + "/mysql-" + version + "-winx64.zip"
            };
        }

        public static string MySqlUrl(string version) { return MySqlUrls(version)[0]; }

        public static Component MySql(ServNookConfig cfg)
        {
            var c = new Component { Id = "mysql", Name = "MySQL", Branch = MySqlSeries, Homepage = "https://dev.mysql.com/downloads/mysql/" };
            return Try(c, x => FindMySql(x, UrlExists));
        }

        /// <summary>
        /// Newest 8.4.x Windows zip: probes patch numbers upward from the baseline (Oracle publishes no machine-readable index;
        /// the newest release always sits in /Downloads, older ones move to the archive). Oracle publishes only MD5 sums, so
        /// the download is checked against the package contents instead.
        /// </summary>
        public static void FindMySql(Component x, Func<string, bool> exists)
        {
            int best = -1;
            string bestUrl = null;
            for (int p = MySqlBaselinePatch; p <= MySqlBaselinePatch + 24; p++)
            {
                string found = MySqlUrls(MySqlSeries + "." + p).FirstOrDefault(exists);
                if (found != null) { best = p; bestUrl = found; }
                else if (best >= 0 && p > best + 2) break;     // two missing in a row after a hit: past the newest
            }
            if (best < 0) { x.Error = "Couldn't find a MySQL " + MySqlSeries + " Windows download at dev.mysql.com."; return; }
            x.Version = MySqlSeries + "." + best;
            x.Url = bestUrl;
            x.FileName = "mysql-" + x.Version + "-winx64.zip";
            x.Sha256 = null;
        }

        public static Component Postgres(ServNookConfig cfg)
        {
            var c = new Component { Id = "postgresql", Name = "PostgreSQL", Homepage = "https://www.enterprisedb.com/download-postgresql-binaries" };
            return Try(c, x =>
            {
                var majors = ParsePgVersions(Resolver.Get("https://www.postgresql.org/versions.json"));
                foreach (var v in majors)
                    for (int build = 1; build <= 3; build++)
                    {
                        string url = PgUrl(v, build);
                        if (!UrlExists(url)) continue;
                        x.Version = v; x.Branch = v.Split('.')[0]; x.Url = url; x.FileName = Path.GetFileName(url); x.Sha256 = null;
                        return;
                    }
                x.Error = "Couldn't find PostgreSQL Windows binaries at get.enterprisedb.com.";
            });
        }

        public static string PgUrl(string version, int build)
        {
            return "https://get.enterprisedb.com/postgresql/postgresql-" + version + "-" + build + "-windows-x64-binaries.zip";
        }

        /// <summary>postgresql.org/versions.json -> "major.minor" of supported releases, newest major first.</summary>
        public static List<string> ParsePgVersions(string json)
        {
            var list = new List<string>();
            foreach (var r in Json.Arr(Json.Parse(json)))
            {
                var supported = Json.Get(r, "supported");
                if (supported is bool && !(bool)supported) continue;
                string major = Convert.ToString(Json.Get(r, "major"));
                string minor = Convert.ToString(Json.Get(r, "latestMinor"));
                if (string.IsNullOrEmpty(major) || string.IsNullOrEmpty(minor) || !Regex.IsMatch(major, @"^\d+$")) continue;
                list.Add(major + "." + minor);
            }
            return list.OrderByDescending(ServNookConfig.VersionKey).ToList();
        }

        public static Component Mongo(ServNookConfig cfg)
        {
            var c = new Component { Id = "mongodb", Name = "MongoDB", Homepage = "https://www.mongodb.com/try/download/community" };
            return Try(c, x => ParseMongoCurrent(Resolver.Get("https://downloads.mongodb.org/current.json"), x));
        }

        /// <summary>downloads.mongodb.org/current.json -> newest production release, Windows x86_64 zip with its SHA-256.</summary>
        public static void ParseMongoCurrent(string json, Component x)
        {
            var root = Json.Parse(json);
            var versions = Json.Arr(Json.Get(root, "versions"))
                .Where(v => !(Json.Get(v, "production_release") is bool) || (bool)Json.Get(v, "production_release"))
                .OrderByDescending(v => ServNookConfig.VersionKey(Json.Str(v, "version")));
            foreach (var v in versions)
                foreach (var d in Json.Arr(Json.Get(v, "downloads")))
                {
                    if ((Json.Str(d, "target") ?? "") != "windows" || (Json.Str(d, "arch") ?? "") != "x86_64") continue;
                    string ed = Json.Str(d, "edition") ?? "base";
                    if (ed != "base" && ed != "targeted") continue;
                    var archive = Json.Get(d, "archive");
                    string url = Json.Str(archive, "url");
                    if (url == null || !url.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;
                    x.Version = Json.Str(v, "version");
                    x.Branch = string.Join(".", (x.Version ?? "0.0").Split('.').Take(2));
                    x.Url = url;
                    x.FileName = Path.GetFileName(new Uri(url).AbsolutePath);
                    string sha = (Json.Str(archive, "sha256") ?? "").ToLowerInvariant();
                    x.Sha256 = sha.Length == 64 ? sha : null;
                    return;
                }
            x.Error = "No MongoDB Windows x86_64 zip in the release list.";
        }

        public static Component Mongosh(ServNookConfig cfg)
        {
            var c = new Component { Id = "mongosh", Name = "MongoDB Shell (mongosh)", Homepage = "https://www.mongodb.com/try/download/shell" };
            return Try(c, x => ParseGitHubRelease(Resolver.Get("https://api.github.com/repos/mongodb-js/mongosh/releases/latest"),
                                                  @"^mongosh-[\d.]+-win32-x64\.zip$", x));
        }

        public static Component AdminerComponent(ServNookConfig cfg)
        {
            var c = new Component { Id = "adminer", Name = "Adminer", Homepage = "https://www.adminer.org/" };
            return Try(c, x => ParseGitHubRelease(Resolver.Get("https://api.github.com/repos/vrana/adminer/releases/latest"),
                                                  @"^adminer-[\d.]+\.php$", x));
        }

        /// <summary>A GitHub "latest release" JSON -> the asset whose name matches, with its SHA-256 digest when GitHub has one.</summary>
        public static void ParseGitHubRelease(string json, string assetPattern, Component x)
        {
            var rel = Json.Parse(json);
            var asset = Json.Arr(Json.Get(rel, "assets")).FirstOrDefault(a => Regex.IsMatch(Json.Str(a, "name") ?? "", assetPattern));
            if (asset == null) { x.Error = "The latest " + x.Name + " release has no matching download."; return; }
            x.FileName = Json.Str(asset, "name");
            x.Url = Json.Str(asset, "browser_download_url");
            x.Version = (Json.Str(rel, "tag_name") ?? "").TrimStart('v');
            string digest = Json.Str(asset, "digest") ?? "";
            x.Sha256 = digest.StartsWith("sha256:") && digest.Length == 71 ? digest.Substring(7).ToLowerInvariant() : null;
        }

        // ================================================================== installing

        /// <summary>Unpack and configure an optional component. Data folders are never touched.</summary>
        public static void Install(Installer inst, Component c, string file)
        {
            switch (c.Id)
            {
                case "mysql":
                    inst.PlaceZip(file, K.MySql, null, null);
                    Check(K.MySqlBin, "mysqld.exe", "mysqladmin.exe", "mysql.exe");
                    break;
                case "postgresql":
                    inst.PlaceZip(file, K.PgSql, "pgsql", null);
                    Check(K.PgBin, "pg_ctl.exe", "initdb.exe", "postgres.exe", "psql.exe");
                    EnablePgsqlInPhp();
                    break;
                case "mongodb":
                    inst.PlaceZip(file, K.Mongo, null, null);
                    Check(K.MongoBin, "mongod.exe");
                    break;
                case "mongosh":
                    inst.PlaceZip(file, K.Mongosh, null, null);
                    Check(K.MongoshBin, "mongosh.exe");
                    break;
                case "adminer":
                    Directory.CreateDirectory(K.Adminer);
                    File.Copy(file, Path.Combine(K.Adminer, "adminer.php"), true);
                    File.WriteAllText(Path.Combine(K.Adminer, "index.php"), AdminerIndex, new UTF8Encoding(false));
                    PatchDefaultVhost();
                    EnablePgsqlInPhp();
                    break;
                default:
                    throw new ArgumentException("Unknown component: " + c.Id);
            }
            string dir = c.Id == "mysql" ? K.MySql : c.Id == "postgresql" ? K.PgSql : c.Id == "mongodb" ? K.Mongo : c.Id == "mongosh" ? K.Mongosh : K.Adminer;
            try { File.WriteAllText(Path.Combine(dir, ".servnook-version"), c.Version ?? ""); } catch { }
        }

        /// <summary>Refuse a package that doesn't contain the programs ServNook needs (a wrong or broken download).</summary>
        static void Check(string bin, params string[] files)
        {
            var missing = files.Where(f => !File.Exists(Path.Combine(bin, f))).ToList();
            if (missing.Count > 0)
                throw new InvalidDataException("The package doesn't contain " + string.Join(", ", missing) + " in " + bin + ". It may be the wrong download.");
        }

        /// <summary>Adminer's entry page: lets it log in without a password on this PC (every server listens on 127.0.0.1 only,
        /// and Apache serves /adminer to this PC only).</summary>
        public const string AdminerIndex =
            "<?php\n" +
            "// Written by ServNook. Adminer refuses empty passwords by default; ServNook's database servers only accept\n" +
            "// connections from this PC, and Apache serves /adminer to this PC only, so allow them here.\n" +
            "function adminer_object() {\n" +
            "    if (class_exists('Adminer\\\\Adminer')) {\n" +
            "        eval('class ServNookAdminer extends \\\\Adminer\\\\Adminer { function login($login, $password) { return true; } }');\n" +
            "    } else {\n" +
            "        eval('class ServNookAdminer extends \\\\Adminer { function login($login, $password) { return true; } }');\n" +
            "    }\n" +
            "    return new ServNookAdminer;\n" +
            "}\n" +
            "include __DIR__ . '/adminer.php';\n";

        public const string AdminerBlock =
            "\n    # Adminer (MySQL, PostgreSQL, MariaDB) on http://localhost/adminer, this PC only\n" +
            "    <IfFile \"${SERVNOOK_ROOT}/apps/adminer/index.php\">\n" +
            "        Alias /adminer \"${SERVNOOK_ROOT}/apps/adminer\"\n" +
            "        <Directory \"${SERVNOOK_ROOT}/apps/adminer\">\n" +
            "            Options FollowSymLinks ExecCGI\n" +
            "            AllowOverride None\n" +
            "            DirectoryIndex index.php\n" +
            "            Require local\n" +
            "        </Directory>\n" +
            "    </IfFile>\n";

        /// <summary>Add the /adminer alias to an existing default-vhost.conf (new installs get it from the template).
        /// Only adds a missing block; nothing else in the file changes.</summary>
        public static bool PatchDefaultVhost()
        {
            var f = Path.Combine(K.ApacheConf, "default-vhost.conf");
            if (!File.Exists(f)) return false;
            var text = File.ReadAllText(f);
            string patched = AddAdminerBlock(text);
            if (patched == text) return false;
            File.WriteAllText(f, patched, new UTF8Encoding(false));
            Log.Info("Added http://localhost/adminer to config\\apache\\default-vhost.conf.");
            return true;
        }

        public static string AddAdminerBlock(string text)
        {
            if (text.IndexOf("/adminer", StringComparison.OrdinalIgnoreCase) >= 0) return text;
            int end = text.IndexOf("</VirtualHost>", StringComparison.OrdinalIgnoreCase);
            if (end < 0) return text;
            string nl = text.Contains("\r\n") ? "\r\n" : "\n";
            return text.Substring(0, end).TrimEnd(' ', '\t') + AdminerBlock.Replace("\n", nl) + text.Substring(end);
        }

        /// <summary>Turn on PHP's PostgreSQL drivers (pgsql, pdo_pgsql) in every installed PHP, when the DLLs are there.</summary>
        public static void EnablePgsqlInPhp()
        {
            foreach (var v in ServNookConfig.InstalledPhp())
            {
                string dir = K.PhpDir(v), ini = Path.Combine(dir, "php.ini");
                if (!File.Exists(ini)) continue;
                var text = File.ReadAllText(ini);
                var fixedText = EnableExtensions(text, dir, "pgsql", "pdo_pgsql");
                if (fixedText != text) { File.WriteAllText(ini, fixedText); Log.Info("Enabled PostgreSQL drivers in PHP " + v + "."); }
            }
        }

        public static string EnableExtensions(string ini, string phpDir, params string[] names)
        {
            string nl = ini.Contains("\r\n") ? "\r\n" : "\n";
            foreach (var n in names)
            {
                if (!File.Exists(Path.Combine(phpDir, "ext", "php_" + n + ".dll"))) continue;
                var on = new Regex(@"^[ \t]*extension[ \t]*=[ \t]*""?(php_)?" + Regex.Escape(n) + @"(\.dll)?""?[ \t]*(?=\r?$)", RegexOptions.Multiline | RegexOptions.IgnoreCase);
                if (on.IsMatch(ini)) continue;
                var off = new Regex(@"^[ \t]*;[ \t]*extension[ \t]*=[ \t]*""?(php_)?" + Regex.Escape(n) + @"(\.dll)?""?[ \t]*(?=\r?$)", RegexOptions.Multiline | RegexOptions.IgnoreCase);
                if (off.IsMatch(ini)) ini = off.Replace(ini, "extension = " + n, 1);
                else
                {
                    var anchor = new Regex(@"^[ \t]*extension[ \t]*=[^\r\n]*", RegexOptions.Multiline);
                    var last = anchor.Matches(ini).Cast<Match>().LastOrDefault();
                    if (last != null) ini = ini.Insert(last.Index + last.Length, nl + "extension = " + n);
                    else ini = ini.TrimEnd() + nl + "extension = " + n + nl;
                }
            }
            return ini;
        }

        // ================================================================== services

        public static IEnumerable<Service> InstalledServices()
        {
            if (MySqlInstalled) yield return Services.MySql;
            if (PostgresInstalled) yield return Services.Postgres;
            if (MongoInstalled) yield return Services.Mongo;
        }
    }

    /// <summary>Shared start/stop plumbing for the optional database servers.</summary>
    public abstract class DbServerService : Service
    {
        protected abstract string Exe { get; }
        protected abstract string ImageName { get; }
        protected abstract string InstallDir { get; }
        public abstract string DataDir { get; }
        protected string PidFile { get { return Path.Combine(K.Pids, Key + ".pid"); } }
        protected string PortFile { get { return Path.Combine(K.Pids, Key + ".port"); } }

        public override string Detail { get { return ServNookConfig.FullVersion(InstallDir); } }
        public override string ConfigFile { get { return K.ConfigFile; } }

        /// <summary>The port the running server was started on (may differ from settings after a port change).</summary>
        protected int RunningPort
        {
            get
            {
                int port;
                try { if (File.Exists(PortFile) && int.TryParse(File.ReadAllText(PortFile).Trim(), out port)) return port; } catch { }
                return Port;
            }
        }

        public override int Pid()
        {
            int pid = Sys.LivePid(PidFile, ImageName);
            if (pid > 0) return pid;
            var orphan = Sys.ProcessesUnder(ImageName, InstallDir).FirstOrDefault();
            return orphan == null ? 0 : orphan.Id;
        }

        protected bool DataEmpty { get { return !Directory.Exists(DataDir) || !Directory.EnumerateFileSystemEntries(DataDir).Any(); } }

        protected void Prepare(string what)
        {
            if (!File.Exists(Exe)) throw new FileNotFoundException(Title + " is not installed (missing " + Exe + "). Add it on the Databases page or with: snook install " + Key);
            Directory.CreateDirectory(K.Logs);
            Directory.CreateDirectory(K.Pids);
            Directory.CreateDirectory(K.Data);
            EnsurePortFree(Port, what);
        }

        /// <summary>Start the server process detached (no redirected output, so nothing waits on its pipes).</summary>
        protected Process Launch(string exe, string args, string workDir)
        {
            var psi = new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = workDir };
            Services.ApplyEnv(psi);
            return Native.StartDetached(psi);
        }

        protected static bool Gone(Process p) { try { return p == null || p.HasExited; } catch { return true; } }

        protected void WaitUp(Process p, int timeoutMs)
        {
            bool up = WaitFor(() => Gone(p) || Sys.IsPortListening(Port), timeoutMs);
            if (up && !Gone(p)) WaitFor(() => Gone(p), 1500);
            if (Gone(p) || !up)
                throw new Exception(Title + " didn't start. Last lines of " + Path.GetFileName(LogFile) + ":\n" + Sys.Tail(LogFile, 15));
            File.WriteAllText(PidFile, p.Id.ToString());
            File.WriteAllText(PortFile, Port.ToString());
            Log.Info(Title + " is running on 127.0.0.1:" + Port);
        }

        protected void Finish(int pid, string kind)
        {
            if (!WaitFor(() => Pid() == 0, 20000))
            {
                Log.Warn(Title + " didn't stop cleanly; ending its process (" + kind + ").");
                Sys.KillTree(pid);
                WaitFor(() => Pid() == 0, 5000);
            }
            try { File.Delete(PidFile); File.Delete(PortFile); } catch { }
            Log.Info(Title + " stopped.");
        }
    }

    // ====================================================================== MySQL

    public class MySqlService : DbServerService
    {
        public override string Key { get { return "mysql"; } }
        public override string Title { get { return "MySQL"; } }
        public override int Port { get { return Cfg.MySqlPort; } }
        public override string LogFile { get { return Path.Combine(K.Logs, "mysql.err"); } }
        public override string Url { get { return "http://localhost" + (Cfg.HttpPort == 80 ? "" : ":" + Cfg.HttpPort) + "/adminer/?server=127.0.0.1:" + Port + "&username=root"; } }
        protected override string Exe { get { return Path.Combine(K.MySqlBin, "mysqld.exe"); } }
        protected override string ImageName { get { return "mysqld"; } }
        protected override string InstallDir { get { return K.MySql; } }
        public override string DataDir { get { return K.MySqlData; } }

        public void Init()
        {
            if (!DataEmpty) return;
            if (Directory.Exists(DataDir)) Directory.Delete(DataDir);
            Log.Info("Creating the MySQL data folder (root has no password; set one with ALTER USER)...");
            var r = Sys.Run(Exe, "--no-defaults --initialize-insecure --basedir=\"" + K.Fwd(K.MySql) + "\" --datadir=\"" + K.Fwd(DataDir) + "\" --log-error=\"" + K.Fwd(LogFile) + "\"",
                            null, 300000, K.MySqlBin);
            if (r.ExitCode != 0 || DataEmpty) throw new Exception("MySQL initialisation failed:\n" + (r.Output.Trim().Length > 0 ? r.Output.Trim() : Sys.Tail(LogFile, 15)));
        }

        public override void Start()
        {
            if (IsRunning) { Log.Info("MySQL is already running."); return; }
            Prepare("MySQL");
            Init();
            Log.Info("Starting MySQL on port " + Port + "...");
            string args = "--no-defaults --basedir=\"" + K.Fwd(K.MySql) + "\" --datadir=\"" + K.Fwd(DataDir) + "\"" +
                          " --port=" + Port + " --bind-address=127.0.0.1 --mysqlx=OFF" +
                          " --log-error=\"" + K.Fwd(LogFile) + "\" --pid-file=\"" + K.Fwd(PidFile) + "\"";
            WaitUp(Launch(Exe, args, K.MySqlBin), 60000);
        }

        public override void Stop()
        {
            int pid = Pid();
            if (pid == 0) { Log.Info("MySQL is not running."); return; }
            Log.Info("Stopping MySQL...");
            var admin = Path.Combine(K.MySqlBin, "mysqladmin.exe");
            if (File.Exists(admin))
            {
                var r = Sys.Run(admin, "--no-defaults --protocol=TCP -h 127.0.0.1 -P " + RunningPort + " -u root shutdown", Services.Env(), 30000, K.MySqlBin);
                if (r.ExitCode != 0) Log.Warn("mysqladmin shutdown: " + r.Output.Trim() + " (if you set a root password, MySQL is stopped by ending its process; InnoDB recovers on the next start)");
            }
            Finish(pid, "InnoDB recovers on the next start");
        }

        /// <summary>Run SQL as root (no password) with MySQL's own client; used by tests and the CLI.</summary>
        public Sys.RunResult Sql(string sql)
        {
            return Sys.Run(Path.Combine(K.MySqlBin, "mysql.exe"), "--no-defaults --protocol=TCP -h 127.0.0.1 -P " + RunningPort + " -u root -N -B",
                           Services.Env(), 30000, K.MySqlBin, sql);
        }
    }

    // ====================================================================== PostgreSQL

    public class PostgresService : DbServerService
    {
        public override string Key { get { return "postgresql"; } }
        public override string Title { get { return "PostgreSQL"; } }
        public override int Port { get { return Cfg.PgPort; } }
        public override string LogFile { get { return Path.Combine(K.Logs, "postgresql.log"); } }
        public override string Url { get { return "http://localhost" + (Cfg.HttpPort == 80 ? "" : ":" + Cfg.HttpPort) + "/adminer/?pgsql=127.0.0.1:" + Port + "&username=postgres"; } }
        protected override string Exe { get { return Path.Combine(K.PgBin, "pg_ctl.exe"); } }
        protected override string ImageName { get { return "postgres"; } }
        protected override string InstallDir { get { return K.PgSql; } }
        public override string DataDir { get { return K.PgData; } }

        /// <summary>The postmaster's pid, from the first line of postmaster.pid.</summary>
        public override int Pid()
        {
            try
            {
                var f = Path.Combine(DataDir, "postmaster.pid");
                if (File.Exists(f))
                {
                    int pid;
                    string first;
                    using (var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var sr = new StreamReader(fs)) first = sr.ReadLine();
                    if (int.TryParse((first ?? "").Trim(), out pid) && Sys.IsAlive(pid, "postgres")) return pid;
                }
            }
            catch { }
            var orphan = Sys.ProcessesUnder("postgres", K.PgSql).OrderBy(p => { try { return p.StartTime; } catch { return DateTime.MaxValue; } }).FirstOrDefault();
            return orphan == null ? 0 : orphan.Id;
        }

        public void Init()
        {
            if (!DataEmpty) return;
            if (Directory.Exists(DataDir)) Directory.Delete(DataDir);
            Log.Info("Creating the PostgreSQL data folder (user postgres, no password, this PC only)...");
            var r = Sys.Run(Path.Combine(K.PgBin, "initdb.exe"), "-D \"" + DataDir + "\" -U postgres -A trust -E UTF8 --locale=C",
                            null, 300000, K.PgBin);
            if (r.ExitCode != 0 || DataEmpty) throw new Exception("PostgreSQL initialisation failed:\n" + r.Output.Trim());
        }

        public override void Start()
        {
            if (IsRunning) { Log.Info("PostgreSQL is already running."); return; }
            Prepare("PostgreSQL");
            Init();
            Log.Info("Starting PostgreSQL on port " + Port + "...");
            // pg_ctl starts the server in the background and (-w) returns once it accepts connections.
            var p = Launch(Exe, "start -D \"" + DataDir + "\" -l \"" + LogFile + "\" -w -t 60 -o \"-p " + Port + " -c listen_addresses=127.0.0.1\"", K.PgBin);
            if (p != null && !p.WaitForExit(90000)) { try { p.Kill(); } catch { } }
            if (!WaitFor(() => Sys.IsPortListening(Port), 10000) || Pid() == 0)
                throw new Exception("PostgreSQL didn't start. Last lines of postgresql.log:\n" + Sys.Tail(LogFile, 15));
            File.WriteAllText(PortFile, Port.ToString());
            Log.Info("PostgreSQL is running on 127.0.0.1:" + Port);
        }

        public override void Stop()
        {
            int pid = Pid();
            if (pid == 0) { Log.Info("PostgreSQL is not running."); return; }
            Log.Info("Stopping PostgreSQL...");
            var r = Sys.Run(Exe, "stop -D \"" + DataDir + "\" -m fast -w -t 30", Services.Env(), 60000, K.PgBin);
            if (r.ExitCode != 0) Log.Warn("pg_ctl stop: " + r.Output.Trim());
            Finish(pid, "PostgreSQL recovers from its write-ahead log on the next start");
        }

        public Sys.RunResult Sql(string sql)
        {
            return Sys.Run(Path.Combine(K.PgBin, "psql.exe"), "-h 127.0.0.1 -p " + RunningPort + " -U postgres -d postgres -At -v ON_ERROR_STOP=1",
                           Services.Env(), 30000, K.PgBin, sql);
        }
    }

    // ====================================================================== MongoDB

    public class MongoService : DbServerService
    {
        public override string Key { get { return "mongodb"; } }
        public override string Title { get { return "MongoDB"; } }
        public override int Port { get { return Cfg.MongoPort; } }
        public override string LogFile { get { return Path.Combine(K.Logs, "mongodb.log"); } }
        public override string Url { get { return Engines.CompassUrl; } }
        protected override string Exe { get { return Path.Combine(K.MongoBin, "mongod.exe"); } }
        protected override string ImageName { get { return "mongod"; } }
        protected override string InstallDir { get { return K.Mongo; } }
        public override string DataDir { get { return K.MongoData; } }

        public override void Start()
        {
            if (IsRunning) { Log.Info("MongoDB is already running."); return; }
            Prepare("MongoDB");
            Directory.CreateDirectory(DataDir);
            Log.Info("Starting MongoDB on port " + Port + "...");
            string args = "--dbpath \"" + DataDir + "\" --port " + Port + " --bind_ip 127.0.0.1 --logpath \"" + LogFile + "\" --logappend";
            WaitUp(Launch(Exe, args, K.MongoBin), 60000);
        }

        public override void Stop()
        {
            int pid = Pid();
            if (pid == 0) { Log.Info("MongoDB is not running."); return; }
            Log.Info("Stopping MongoDB...");
            if (Engines.MongoshInstalled)
            {
                // The connection drops as the server shuts down, so a non-zero exit here is normal.
                Sys.Run(Path.Combine(K.MongoshBin, "mongosh.exe"), "--quiet --port " + RunningPort + " --eval \"db.getSiblingDB('admin').shutdownServer()\"",
                        Services.Env(), 30000, K.MongoshBin);
            }
            else Log.Warn("mongosh isn't installed, so MongoDB is stopped by ending its process (its journal makes this safe). Add mongosh for a clean shutdown.");
            Finish(pid, "MongoDB replays its journal on the next start");
        }

        public Sys.RunResult Eval(string js)
        {
            return Sys.Run(Path.Combine(K.MongoshBin, "mongosh.exe"), "--quiet --port " + RunningPort + " --eval \"" + js.Replace("\"", "\\\"") + "\"",
                           Services.Env(), 30000, K.MongoshBin);
        }
    }
}
