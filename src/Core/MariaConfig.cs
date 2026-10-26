using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading;

namespace ServNook
{
    /// <summary>A my.ini setting the configuration manager edits.</summary>
    public class MariaSetting
    {
        public string Key, Section = "mysqld", Kind, Default;
        public string Id { get { return Section + "." + Key; } }
    }

    /// <summary>A server variable as the running server reports it (information_schema.SYSTEM_VARIABLES).</summary>
    public class SysVar
    {
        public string Name, Value, Type, Min, Max;
        public bool ReadOnly;
        public string[] Enum = new string[0];
    }

    /// <summary>Result of asking mariadbd how it would read an option file, without starting a server.</summary>
    public class MariaCheck
    {
        public bool Ok;
        public int ExitCode;
        public List<string> Errors = new List<string>(), Warnings = new List<string>();
        public Dictionary<string, string> Vars = new Dictionary<string, string>();
        public string Raw = "";
    }

    public static class MariaCatalog
    {
        public static readonly List<MariaSetting> All = new List<MariaSetting>
        {
            new MariaSetting { Key = "character_set_server", Kind = "charset", Default = "utf8mb4" },
            new MariaSetting { Key = "collation_server", Kind = "collation", Default = "utf8mb4_unicode_ci" },
            new MariaSetting { Key = "default_character_set", Section = "client", Kind = "charset", Default = "utf8mb4" },
            new MariaSetting { Key = "max_connections", Kind = "int", Default = "100" },
            new MariaSetting { Key = "max_allowed_packet", Kind = "size", Default = "64M" },
            new MariaSetting { Key = "innodb_buffer_pool_size", Kind = "size", Default = "256M" },
            new MariaSetting { Key = "innodb_log_file_size", Kind = "size", Default = "64M" },
            new MariaSetting { Key = "innodb_flush_log_at_trx_commit", Kind = "enum", Default = "1" },
            new MariaSetting { Key = "innodb_doublewrite", Kind = "bool", Default = "1" },
            new MariaSetting { Key = "innodb_file_per_table", Kind = "bool", Default = "1" },
            new MariaSetting { Key = "aria_recover_options", Kind = "set", Default = "BACKUP,QUICK" },
            new MariaSetting { Key = "slow_query_log", Kind = "bool", Default = "0" },
            new MariaSetting { Key = "long_query_time", Kind = "float", Default = "1" },
        };

        public static MariaSetting Get(string id) { return All.FirstOrDefault(s => s.Id == id || s.Key == id); }

        /// <summary>Options ServNook passes on mariadbd's command line; they override my.ini.</summary>
        public static readonly string[] StartupArgs = { "port", "bind_address", "datadir", "log_error", "pid_file" };

        public static string StartupOwner(string key)
        {
            switch (key)
            {
                case "port": return "MariaDB port is managed by ServNook and passed when the server starts. Changing it in my.ini doesn't affect the port in use; change it in the General section.";
                case "bind_address": return "ServNook always starts MariaDB with --bind-address=127.0.0.1 so the database stays private to this PC. A bind-address in my.ini has no effect.";
                case "datadir": return "The data folder is passed by ServNook (--datadir). A datadir line in my.ini has no effect.";
                case "log_error": return "The error log path is passed by ServNook (--log-error=logs/mariadb.err). A log-error line in my.ini has no effect.";
                case "pid_file": return "The PID file is managed by ServNook (--pid-file). A pid-file line in my.ini has no effect.";
            }
            return null;
        }

        // ------------------------------------------------------------------ values

        /// <summary>"64M" / "67108864" -> bytes. Null when not a size.</summary>
        public static long? Bytes(string v)
        {
            if (string.IsNullOrWhiteSpace(v)) return null;
            var m = Regex.Match(v.Trim(), @"^(\d+(?:\.\d+)?)\s*([KMGTkmgt]?)[Bb]?$");
            if (!m.Success) return null;
            double n = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            switch (m.Groups[2].Value.ToUpperInvariant())
            {
                case "K": n *= 1024; break;
                case "M": n *= 1024 * 1024; break;
                case "G": n *= 1024L * 1024 * 1024; break;
                case "T": n *= 1024L * 1024 * 1024 * 1024; break;
            }
            return (long)n;
        }

        public static string Human(long bytes)
        {
            if (bytes >= 1L << 30 && bytes % (1L << 30) == 0) return (bytes >> 30) + " GB";
            if (bytes >= 1L << 20) return (bytes % (1L << 20) == 0 ? (bytes >> 20).ToString() : (bytes / 1048576.0).ToString("0.#", CultureInfo.InvariantCulture)) + " MB";
            if (bytes >= 1024) return (bytes / 1024) + " KB";
            return bytes + " bytes";
        }

        /// <summary>"64M" for whole megabytes, else bytes.</summary>
        public static string SizeText(long bytes)
        {
            if (bytes % (1L << 30) == 0 && bytes >= 1L << 30) return (bytes >> 30) + "G";
            if (bytes % (1L << 20) == 0) return (bytes >> 20) + "M";
            return bytes.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>A value in a form that compares equal however it was written (64M == 67108864, ON == 1 == TRUE).</summary>
        public static string Norm(string kind, string v)
        {
            if (v == null) return null;
            v = v.Trim();
            switch (kind)
            {
                case "size": var b = Bytes(v); return b.HasValue ? b.Value.ToString(CultureInfo.InvariantCulture) : v;
                case "int": long n; return long.TryParse(v, out n) ? n.ToString(CultureInfo.InvariantCulture) : v;
                case "bool":
                    var u = v.ToUpperInvariant();
                    if (u == "1" || u == "ON" || u == "TRUE" || u == "YES") return "1";
                    if (u == "0" || u == "OFF" || u == "FALSE" || u == "NO") return "0";
                    return u;
                case "float": double d; return double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d.ToString("0.######", CultureInfo.InvariantCulture) : v;
                case "set": return string.Join(",", v.ToUpperInvariant().Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).OrderBy(x => x));
                case "enum": return v.ToUpperInvariant();
                default: return v.ToLowerInvariant();
            }
        }

        /// <summary>SET GLOBAL literal for a value.</summary>
        public static string SqlLiteral(string kind, string v)
        {
            switch (kind)
            {
                case "size": return (Bytes(v) ?? 0).ToString(CultureInfo.InvariantCulture);
                case "int": case "enum": case "float": return Norm(kind, v);
                case "bool": return Norm("bool", v) == "1" ? "ON" : "OFF";
                default: return "'" + v.Replace("\\", "\\\\").Replace("'", "''") + "'";
            }
        }
    }

    /// <summary>Everything the configuration manager knows about MariaDB: saved, next-start and live values.</summary>
    public class MariaInfo
    {
        public bool Installed, Running, Connected;
        public string Version = "", ConnectError;
        public MariaCheck NextStart;                                 // my.ini + ServNook's startup arguments, as mariadbd reads them
        public Dictionary<string, SysVar> Live = new Dictionary<string, SysVar>();
        public Dictionary<string, string> Status = new Dictionary<string, string>();
        public SortedDictionary<string, List<string>> Collations = new SortedDictionary<string, List<string>>();
        public Dictionary<string, string> DefaultCollation = new Dictionary<string, string>();
        public List<string> Databases = new List<string>(), Anonymous = new List<string>(), RootAccounts = new List<string>();

        public static string Server { get { return Installer.MariaExe("mariadbd.exe", "mysqld.exe"); } }

        public static MariaInfo Probe(ServNookConfig cfg)
        {
            var m = new MariaInfo();
            string exe = Server;
            m.Installed = exe != null;
            if (!m.Installed) return m;
            try
            {
                var v = Sys.Run(exe, "--version", null, 15000, K.MariaDbBin);
                var mm = Regex.Match(v.Output, @"Ver\s+(\d+(?:\.\d+)+)");
                m.Version = mm.Success ? mm.Groups[1].Value : v.Output.Trim();
            }
            catch { }
            if (File.Exists(K.MyIni)) m.NextStart = Check(K.MyIni, cfg);
            m.Running = Services.MariaDb.IsRunning;
            if (m.Running) m.LoadLive(cfg);
            return m;
        }

        public void LoadLive(ServNookConfig cfg)
        {
            string err;
            List<string[]> rows;
            try
            {
                rows = Services.MariaDb.Query(
                    "SELECT 'V', VARIABLE_NAME, READ_ONLY, IFNULL(GLOBAL_VALUE,''), VARIABLE_TYPE, IFNULL(NUMERIC_MIN_VALUE,''), IFNULL(NUMERIC_MAX_VALUE,''), IFNULL(ENUM_VALUE_LIST,'') FROM information_schema.SYSTEM_VARIABLES;\n" +
                    "SELECT 'S', VARIABLE_NAME, VARIABLE_VALUE FROM information_schema.GLOBAL_STATUS WHERE VARIABLE_NAME IN ('UPTIME','THREADS_CONNECTED','MAX_USED_CONNECTIONS','QUESTIONS','SLOW_QUERIES','CONNECTIONS');\n" +
                    "SELECT 'C', CHARACTER_SET_NAME, COLLATION_NAME, IS_DEFAULT FROM information_schema.COLLATIONS WHERE CHARACTER_SET_NAME IS NOT NULL;\n" +
                    "SELECT 'D', SCHEMA_NAME FROM information_schema.SCHEMATA;\n" +
                    "SELECT 'A', User, Host FROM mysql.user WHERE User='';\n" +
                    "SELECT 'R', User, Host, plugin FROM mysql.user WHERE User='root';\n", cfg.MysqlRootPassword, out err);
            }
            catch (Exception e) { ConnectError = e.Message; return; }
            if (rows.Count == 0) { ConnectError = string.IsNullOrEmpty(err) ? "No answer from MariaDB." : err; return; }
            Connected = true;
            if (!string.IsNullOrEmpty(err)) ConnectError = err;   // partial (e.g. no access to mysql.user)
            foreach (var r in rows)
            {
                switch (r[0])
                {
                    case "V":
                        if (r.Length < 8) break;
                        var sv = new SysVar { Name = r[1].ToLowerInvariant(), ReadOnly = r[2] == "YES", Value = r[3], Type = r[4], Min = r[5], Max = r[6],
                                              Enum = r[7].Length == 0 ? new string[0] : r[7].Split(',') };
                        Live[sv.Name] = sv;
                        break;
                    case "S": if (r.Length >= 3) Status[r[1].ToLowerInvariant()] = r[2]; break;
                    case "C":
                        if (r.Length < 4) break;
                        List<string> list;
                        if (!Collations.TryGetValue(r[1], out list)) Collations[r[1]] = list = new List<string>();
                        list.Add(r[2]);
                        if (r[3] == "Yes") DefaultCollation[r[1]] = r[2];
                        break;
                    case "D": if (r.Length >= 2) Databases.Add(r[1]); break;
                    case "A": if (r.Length >= 3) Anonymous.Add("''@'" + r[2] + "'"); break;
                    case "R": if (r.Length >= 4) RootAccounts.Add("root@" + r[2] + " (" + (r[3].Length == 0 ? "native password" : r[3]) + ")"); break;
                }
            }
            foreach (var l in Collations.Values) l.Sort(StringComparer.Ordinal);
        }

        public string LiveValue(string key) { SysVar v; return Live.TryGetValue(key, out v) ? v.Value : null; }

        /// <summary>Is the setting known to this MariaDB version? (From the running server, or from mariadbd --help.)</summary>
        public bool? Supports(string key)
        {
            if (Live.Count > 0) return Live.ContainsKey(key);
            if (NextStart != null && NextStart.Vars.Count > 0) return NextStart.Vars.ContainsKey(key);
            return null;
        }

        /// <summary>Can it be changed without a restart? Null when unknown (server not running).</summary>
        public bool? Dynamic(string key)
        {
            SysVar v;
            if (Live.TryGetValue(key, out v)) return !v.ReadOnly;
            return null;
        }

        // ------------------------------------------------------------------ validation (no server started)

        static string FreeTemp(string prefix)
        {
            string d = Path.Combine(K.Tmp, "config-check", prefix + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(d);
            return d;
        }

        static void TryDelete(string dir)
        {
            for (int i = 0; i < 10; i++)
            {
                try { if (Directory.Exists(dir)) Directory.Delete(dir, true); return; }
                catch { Thread.Sleep(300); }
            }
        }

        static readonly Regex Noise = new Regex(@"max_open_files|mysql\.plugin|Some options may be missing|Could not increase number of", RegexOptions.IgnoreCase);

        /// <summary>
        /// Ask mariadbd how it reads <paramref name="ini"/> with ServNook's startup arguments (--help --verbose). It
        /// prints every variable and rejects unknown options or bad values. It runs against an empty scratch data
        /// folder, so the real data folder is never opened.
        /// </summary>
        public static MariaCheck Check(string ini, ServNookConfig cfg)
        {
            var res = new MariaCheck();
            string exe = Server;
            if (exe == null) { res.Errors.Add("mariadbd.exe was not found in " + K.MariaDbBin + "."); return res; }
            string scratch = FreeTemp("help");
            try
            {
                string args = "--defaults-file=\"" + K.Fwd(ini) + "\" --datadir=\"" + K.Fwd(scratch) + "\" --port=" + cfg.MysqlPort +
                              " --bind-address=127.0.0.1 --help --verbose";
                var r = Sys.Run(exe, args, null, 60000, K.MariaDbBin);
                res.ExitCode = r.ExitCode;
                res.Raw = r.Output;
                bool inVars = false;
                foreach (var raw in r.Output.Replace("\r\n", "\n").Split('\n'))
                {
                    string line = raw.TrimEnd();
                    var em = Regex.Match(line, @"\[(ERROR|Warning)\]\s*(.*)$");
                    if (em.Success)
                    {
                        if (Noise.IsMatch(line)) continue;
                        (em.Groups[1].Value == "ERROR" ? res.Errors : res.Warnings).Add(em.Groups[2].Value.Trim());
                        continue;
                    }
                    if (line.StartsWith("Variables (--variable-name=value)")) { inVars = true; continue; }
                    if (!inVars || line.StartsWith("----") || line.StartsWith("and boolean options")) continue;
                    if (line.Length == 0) { if (res.Vars.Count > 0) inVars = false; continue; }
                    var vm = Regex.Match(line, @"^(\S+)\s+(.*)$");
                    if (vm.Success) res.Vars[OptionFile.Norm(vm.Groups[1].Value)] = vm.Groups[2].Value.Trim();
                    else if (Regex.IsMatch(line, @"^\S+$")) res.Vars[OptionFile.Norm(line)] = "";
                }
                res.Ok = r.ExitCode == 0 && res.Errors.Count == 0 && res.Vars.Count > 0;
                if (!res.Ok && res.Errors.Count == 0)
                    res.Errors.Add(r.ExitCode == 0 ? "mariadbd printed no configuration (unexpected output)." : "mariadbd exited with code " + r.ExitCode + ".");
            }
            catch (Exception e) { res.Errors.Add(e.Message); }
            finally { TryDelete(scratch); }
            return res;
        }

        /// <summary>
        /// Start a throw-away MariaDB with the candidate option file on an empty scratch data folder and a free port,
        /// wait until it is ready, then stop it. Catches what option parsing can't: memory that can't be allocated,
        /// InnoDB settings that fail at startup. Never touches the real data folder. Returns null on success.
        /// </summary>
        public static string ScratchStart(string ini, ServNookConfig cfg)
        {
            string exe = Server;
            if (exe == null) return "mariadbd.exe was not found.";
            string dir = FreeTemp("start");
            string data = Path.Combine(dir, "data"), log = Path.Combine(dir, "scratch.err");
            Directory.CreateDirectory(data);
            int port;
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start(); port = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop();
            string args = "--defaults-file=\"" + K.Fwd(ini) + "\" --datadir=\"" + K.Fwd(data) + "\" --port=" + port +
                          " --bind-address=127.0.0.1 --skip-grant-tables --log-error=\"" + K.Fwd(log) + "\" --pid-file=\"" + K.Fwd(Path.Combine(dir, "s.pid")) +
                          "\" --socket=\"" + (Sys.IsWindows ? "servnook-check-" + port : "/tmp/servnook-check-" + port + ".sock") + "\"" +
                          " --slow-query-log-file=\"" + K.Fwd(Path.Combine(dir, "slow.log")) + "\" --general-log-file=\"" + K.Fwd(Path.Combine(dir, "general.log")) + "\"";
            Process p = null;
            try
            {
                var psi = new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = K.MariaDbBin,
                                                           RedirectStandardOutput = true, RedirectStandardError = true };
                p = Process.Start(psi);
                p.OutputDataReceived += (s, e) => { };
                p.ErrorDataReceived += (s, e) => { };
                p.BeginOutputReadLine(); p.BeginErrorReadLine();
                var sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < 60000)
                {
                    if (p.HasExited) break;
                    if (ReadLog(log).Contains("ready for connections")) return null;
                    Thread.Sleep(250);
                }
                string text = ReadLog(log);
                var errs = text.Replace("\r\n", "\n").Split('\n').Where(x => x.Contains("[ERROR]") && !Noise.IsMatch(x))
                               .Select(x => Regex.Replace(x, @"^.*?\[ERROR\]\s*", "")).Distinct().Take(8).ToList();
                if (!p.HasExited) return "MariaDB didn't become ready within 60 seconds with this configuration." + (errs.Count > 0 ? "\n" + string.Join("\n", errs) : "");
                return "MariaDB would not start with this configuration:\n" + (errs.Count > 0 ? string.Join("\n", errs) : text.Trim());
            }
            catch (Exception e) { return "Couldn't run the startup check: " + e.Message; }
            finally
            {
                try { if (p != null && !p.HasExited) { p.Kill(); p.WaitForExit(10000); } } catch { }
                TryDelete(dir);
            }
        }

        static string ReadLog(string file)
        {
            try
            {
                if (!File.Exists(file)) return "";
                using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var sr = new StreamReader(fs)) return sr.ReadToEnd();
            }
            catch { return ""; }
        }
    }
}
