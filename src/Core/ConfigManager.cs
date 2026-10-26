using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace ServNook
{
    /// <summary>Pending changes from the configuration manager. Only what the user changed is set.</summary>
    public class ConfigEdits
    {
        public string Php;
        public int? HttpPort, MysqlPort;
        public bool? Lan;
        /// <summary>ThreadsPerChild, MaxConnectionsPerChild, KeepAlive, KeepAliveTimeout, MaxKeepAliveRequests, LogLevel,
        /// ServerHost, Indexes, Htaccess, Fcgid, ProtectDotfiles ("On"/"Off" for switches).</summary>
        public readonly Dictionary<string, string> Apache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, bool> Modules = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        /// <summary>my.ini values by setting id ("mysqld.max_connections", "client.default_character_set").</summary>
        public readonly Dictionary<string, string> Maria = new Dictionary<string, string>();
        /// <summary>my.ini lines to comment out (options overridden by ServNook's startup arguments).</summary>
        public readonly List<int> CommentOut = new List<int>();
        public bool ResetHttpd;

        public bool IsEmpty
        {
            get { return Php == null && HttpPort == null && MysqlPort == null && Lan == null && Apache.Count == 0 && Modules.Count == 0 && Maria.Count == 0 && CommentOut.Count == 0 && !ResetHttpd; }
        }
    }

    public class ConfigPlan
    {
        public string HttpdText, MyIniText;      // null = unchanged
        public byte[] MyIniBytes;                // my.ini in its original encoding
        public ServNookConfig NewCfg;              // the settings after the change (always set)
        public bool CfgChanged, ApacheAffected, MariaAffected, MariaRestart;
        public readonly List<KeyValuePair<string, string>> Live = new List<KeyValuePair<string, string>>();
        public readonly Dictionary<string, string> Expect = new Dictionary<string, string>();
        public readonly List<string> Changes = new List<string>(), Notes = new List<string>();
    }

    public enum ApplyOutcome { NoChanges, Saved, AppliedLive, Applied, Failed, RollbackFailed }

    public class ApplyResult
    {
        public ApplyOutcome Outcome;
        public string Message = "", Details = "", BackupDir;
        public readonly List<string> Notes = new List<string>();
        public bool Ok { get { return Outcome != ApplyOutcome.Failed && Outcome != ApplyOutcome.RollbackFailed; } }
    }

    public class ConfigException : Exception
    {
        public readonly List<string> Problems;
        public ConfigException(List<string> problems) : base(string.Join("\n", problems)) { Problems = problems; }
    }

    /// <summary>
    /// Turns edits into a validated plan and applies it: validate the candidate files with the real httpd and mariadbd,
    /// back up, write, apply live where MariaDB allows it, restart only the affected service, verify, and roll back on failure.
    /// </summary>
    public static class ConfigManager
    {
        public static string BackupRoot { get { return Path.Combine(K.Config, "backups"); } }
        static string CheckDir { get { return Path.Combine(K.Tmp, "config-check"); } }

        public static readonly string[] LogLevels = { "emerg", "alert", "crit", "error", "warn", "notice", "info", "debug" };

        /// <summary>Modules ServNook's own configuration needs.</summary>
        public static readonly string[] RequiredModules = { "alias_module", "authz_core_module", "authz_host_module", "dir_module", "env_module", "mime_module", "log_config_module" };

        public static readonly Dictionary<string, string[]> ModuleDeps = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "proxy_http_module", new[] { "proxy_module" } }, { "proxy_wstunnel_module", new[] { "proxy_module" } },
            { "proxy_fcgi_module", new[] { "proxy_module" } }, { "proxy_ajp_module", new[] { "proxy_module" } },
            { "proxy_connect_module", new[] { "proxy_module" } }, { "proxy_scgi_module", new[] { "proxy_module" } },
            { "proxy_uwsgi_module", new[] { "proxy_module" } }, { "proxy_http2_module", new[] { "proxy_module", "http2_module" } },
            { "proxy_balancer_module", new[] { "proxy_module", "slotmem_shm_module" } }, { "proxy_hcheck_module", new[] { "proxy_module", "watchdog_module" } },
            { "proxy_html_module", new[] { "proxy_module", "xml2enc_module" } },
            { "lbmethod_byrequests_module", new[] { "proxy_balancer_module" } }, { "lbmethod_bytraffic_module", new[] { "proxy_balancer_module" } },
            { "lbmethod_bybusyness_module", new[] { "proxy_balancer_module" } }, { "lbmethod_heartbeat_module", new[] { "proxy_balancer_module" } },
            { "deflate_module", new[] { "filter_module" } }, { "brotli_module", new[] { "filter_module" } },
            { "auth_basic_module", new[] { "authn_core_module" } }, { "auth_digest_module", new[] { "authn_core_module" } },
            { "authn_file_module", new[] { "authn_core_module" } }, { "authn_dbm_module", new[] { "authn_core_module" } },
            { "authn_anon_module", new[] { "authn_core_module" } }, { "authn_dbd_module", new[] { "authn_core_module", "dbd_module" } },
            { "authz_user_module", new[] { "authz_core_module" } }, { "authz_groupfile_module", new[] { "authz_core_module" } },
            { "authz_dbm_module", new[] { "authz_core_module" } }, { "authz_owner_module", new[] { "authz_core_module" } },
            { "ssl_module", new[] { "socache_shmcb_module" } }, { "session_cookie_module", new[] { "session_module" } },
            { "session_crypto_module", new[] { "session_module" } }, { "cache_disk_module", new[] { "cache_module" } },
            { "cache_socache_module", new[] { "cache_module" } },
        };

        /// <summary>Modules needed because of what else is configured (Node apps use the reverse proxy and RequestHeader).</summary>
        public static List<string> RequiredNow()
        {
            var list = RequiredModules.ToList();
            if (NodeApp.LoadAll().Count > 0) list.AddRange(new[] { "proxy_module", "proxy_http_module", "headers_module" });
            return list;
        }

        // ================================================================== plan

        public static ConfigPlan Plan(ConfigEdits e, ServNookConfig cfg, MariaInfo mi, ApacheInfo ai)
        {
            var p = new ConfigPlan();
            var errs = new List<string>();
            var n = cfg.Clone();
            p.NewCfg = n;

            // ---- ServNook settings that Apache / MariaDB start with
            if (e.Php != null && e.Php != cfg.Php)
            {
                if (!ServNookConfig.InstalledPhp().Contains(e.Php)) errs.Add("PHP " + e.Php + " is not installed.");
                else if (!File.Exists(Path.Combine(K.PhpDir(e.Php), "php8apache2_4.dll"))) errs.Add("PHP " + e.Php + " has no php8apache2_4.dll; Apache needs the x64 Thread Safe build.");
                n.Php = e.Php; p.Changes.Add("PHP version: " + cfg.Php + " → " + e.Php);
            }
            if (e.HttpPort != null && e.HttpPort != cfg.HttpPort) { n.HttpPort = e.HttpPort.Value; p.Changes.Add("HTTP port: " + cfg.HttpPort + " → " + n.HttpPort); }
            if (e.MysqlPort != null && e.MysqlPort != cfg.MysqlPort) { n.MysqlPort = e.MysqlPort.Value; p.Changes.Add("MariaDB port: " + cfg.MysqlPort + " → " + n.MysqlPort); }
            if (e.Lan != null && e.Lan != cfg.LanAccess) { n.LanAccess = e.Lan.Value; p.Changes.Add("Network access: " + (n.LanAccess ? "on" : "off")); }
            CheckPorts(cfg, n, errs);
            p.CfgChanged = n.Php != cfg.Php || n.HttpPort != cfg.HttpPort || n.MysqlPort != cfg.MysqlPort || n.LanAccess != cfg.LanAccess;

            // ---- httpd.conf
            if (e.ResetHttpd || e.Apache.Count > 0 || e.Modules.Count > 0)
            {
                string original = File.Exists(K.HttpdConf) ? File.ReadAllText(K.HttpdConf, new UTF8Encoding(false)) : "";
                var conf = e.ResetHttpd ? ApacheConfFile.Parse(Installer.Template("config/apache/httpd.conf")) : ApacheConfFile.Parse(original);
                if (e.ResetHttpd) p.Changes.Add("httpd.conf replaced with ServNook's template (custom/*.conf untouched)");
                PlanApache(e, conf, ai, p, errs);
                if (conf.Text != original) p.HttpdText = conf.Text;
            }
            p.ApacheAffected = p.HttpdText != null || n.Php != cfg.Php || n.HttpPort != cfg.HttpPort || n.LanAccess != cfg.LanAccess || n.MysqlPort != cfg.MysqlPort;

            // ---- my.ini
            if (e.Maria.Count > 0 || e.CommentOut.Count > 0)
            {
                var ini = OptionFile.Load(K.MyIni);
                string before = ini.Text;
                PlanMaria(e, ini, mi, p, errs);
                if (ini.Text != before) { p.MyIniText = ini.Text; p.MyIniBytes = ini.Bytes(); }
            }
            if (n.MysqlPort != cfg.MysqlPort) { p.MariaAffected = true; p.MariaRestart = true; }
            if (p.MyIniText != null) p.MariaAffected = true;

            if (errs.Count > 0) throw new ConfigException(errs);
            return p;
        }

        static void CheckPorts(ServNookConfig cfg, ServNookConfig n, List<string> errs)
        {
            if (n.HttpPort < 1 || n.HttpPort > 65535 || n.MysqlPort < 1 || n.MysqlPort > 65535) errs.Add("Ports must be between 1 and 65535.");
            if (n.HttpPort == n.MysqlPort) errs.Add("The HTTP and MariaDB ports must differ.");
            var clash = NodeApp.LoadAll().FirstOrDefault(a => a.Port == n.HttpPort || a.Port == n.MysqlPort);
            if (clash != null) errs.Add("Port " + clash.Port + " is used by the Node app '" + clash.Name + "'.");
            if (n.HttpPort != cfg.HttpPort && Sys.IsPortListening(n.HttpPort))
                errs.Add("Port " + n.HttpPort + " is already in use by " + Sys.PortOwner(n.HttpPort) + ".");
            if (n.MysqlPort != cfg.MysqlPort && Sys.IsPortListening(n.MysqlPort))
                errs.Add("Port " + n.MysqlPort + " is already in use by " + Sys.PortOwner(n.MysqlPort) + ".");
        }

        static int? Int(string v, int min, int max, string what, List<string> errs)
        {
            int x;
            if (!int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out x) || x < min || x > max)
            { errs.Add(what + " must be a whole number from " + min + " to " + max + "."); return null; }
            return x;
        }

        static bool OnOff(string v) { return v.Equals("On", StringComparison.OrdinalIgnoreCase) || v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase); }

        static void PlanApache(ConfigEdits e, ApacheConfFile conf, ApacheInfo ai, ConfigPlan p, List<string> errs)
        {
            foreach (var kv in e.Apache)
            {
                string v = (kv.Value ?? "").Trim();
                int? x;
                switch (kv.Key.ToLowerInvariant())
                {
                    case "threadsperchild":
                        if ((x = Int(v, 1, 1920, "ThreadsPerChild", errs)) != null) { conf.SetMpm("ThreadsPerChild", x.ToString()); p.Changes.Add("ThreadsPerChild: " + x); }
                        break;
                    case "maxconnectionsperchild":
                        if ((x = Int(v, 0, 10000000, "MaxConnectionsPerChild", errs)) != null) { conf.SetMpm("MaxConnectionsPerChild", x.ToString()); p.Changes.Add("MaxConnectionsPerChild: " + x); }
                        break;
                    case "keepalive":
                        conf.SetTop("KeepAlive", OnOff(v) ? "On" : "Off"); p.Changes.Add("KeepAlive: " + (OnOff(v) ? "On" : "Off"));
                        break;
                    case "keepalivetimeout":
                        if ((x = Int(v, 1, 300, "KeepAliveTimeout", errs)) != null) { conf.SetTop("KeepAliveTimeout", x.ToString()); p.Changes.Add("KeepAliveTimeout: " + x + " s"); }
                        break;
                    case "maxkeepaliverequests":
                        if ((x = Int(v, 0, 100000, "MaxKeepAliveRequests", errs)) != null) { conf.SetTop("MaxKeepAliveRequests", x.ToString()); p.Changes.Add("MaxKeepAliveRequests: " + x); }
                        break;
                    case "loglevel":
                        if (!LogLevels.Contains(v.ToLowerInvariant())) errs.Add("Unknown log level '" + v + "'.");
                        else { conf.SetTop("LogLevel", v.ToLowerInvariant()); p.Changes.Add("Apache log level: " + v.ToLowerInvariant()); }
                        break;
                    case "serverhost":
                        if (!Regex.IsMatch(v, @"^[A-Za-z0-9]([A-Za-z0-9.-]{0,251}[A-Za-z0-9])?$")) errs.Add("Server name may contain only letters, digits, dots and hyphens.");
                        else { conf.SetServerHost(v); p.Changes.Add("ServerName: " + v); }
                        break;
                    case "indexes":
                        try { conf.SetIndexes(OnOff(v)); p.Changes.Add("Directory listing: " + (OnOff(v) ? "on" : "off")); } catch (Exception ex) { errs.Add(ex.Message); }
                        break;
                    case "htaccess":
                        try { conf.SetHtaccess(OnOff(v)); p.Changes.Add(".htaccess overrides: " + (OnOff(v) ? "allowed (AllowOverride All)" : "ignored (AllowOverride None)")); } catch (Exception ex) { errs.Add(ex.Message); }
                        break;
                    case "fcgid":
                        if (OnOff(v) && ai != null && (!ai.FcgidModule || !ai.PhpCgi))
                            errs.Add("PHP through mod_fcgid needs apache\\modules\\mod_fcgid.so and php-cgi.exe in the PHP folder.");
                        else { conf.SetFcgid(OnOff(v)); p.Changes.Add("PHP mode: " + (OnOff(v) ? "mod_fcgid (FastCGI)" : "mod_php (inside Apache)")); }
                        break;
                    case "protectdotfiles":
                        try { conf.SetProtectDotfiles(OnOff(v)); p.Changes.Add("Block .git/.env files: " + (OnOff(v) ? "on" : "off")); } catch (Exception ex) { errs.Add(ex.Message); }
                        break;
                    default: errs.Add("Unknown Apache setting " + kv.Key + "."); break;
                }
            }

            if (e.Modules.Count == 0) return;
            var required = RequiredNow();
            var elsewhere = ai == null ? new List<string>() : ai.Loaded.Where(m => !conf.Modules().Any(x => x.Name.Equals(m, StringComparison.OrdinalIgnoreCase))).ToList();
            foreach (var kv in e.Modules)
            {
                if (!kv.Value && required.Contains(kv.Key)) { errs.Add(kv.Key + " is required by ServNook's configuration and can't be turned off."); continue; }
                if (kv.Value && ai != null && ai.Available.Count > 0 && !ai.Available.Contains(kv.Key) && !elsewhere.Contains(kv.Key))
                { errs.Add(kv.Key + " isn't in apache\\modules (" + ApacheConfFile.FileFor(kv.Key) + ")."); continue; }
                conf.SetModule(kv.Key, kv.Value);
                p.Changes.Add((kv.Value ? "Load " : "Stop loading ") + kv.Key);
            }
            // Dependencies, on the final state.
            Func<HashSet<string>> active = () => new HashSet<string>(conf.Modules().Where(x => x.Active).Select(x => x.Name).Concat(elsewhere), StringComparer.OrdinalIgnoreCase);
            for (int pass = 0; pass < 4; pass++)
            {
                var on = active();
                bool added = false;
                foreach (var m in on.ToList())
                {
                    string[] deps;
                    if (!ModuleDeps.TryGetValue(m, out deps)) continue;
                    foreach (var d in deps.Where(d => !on.Contains(d)))
                    {
                        bool userOff;
                        if (e.Modules.TryGetValue(d, out userOff) && !userOff) { errs.Add(m + " needs " + d + "; turn " + m + " off first, or keep " + d + "."); continue; }
                        if (ai != null && ai.Available.Count > 0 && !ai.Available.Contains(d)) { errs.Add(m + " needs " + d + ", which isn't in apache\\modules."); continue; }
                        conf.SetModule(d, true); added = true;
                        p.Notes.Add(d + " is loaded too because " + m + " needs it.");
                    }
                }
                if (!added) break;
            }
        }

        static readonly string[] AriaTokens = { "NORMAL", "BACKUP", "FORCE", "QUICK", "OFF" };

        static void PlanMaria(ConfigEdits e, OptionFile ini, MariaInfo mi, ConfigPlan p, List<string> errs)
        {
            var entries = ini.Entries();
            foreach (int line in e.CommentOut.Distinct().OrderByDescending(x => x))
            {
                var en = entries.FirstOrDefault(x => x.Line == line);
                if (en == null || !MariaCatalog.StartupArgs.Contains(en.Key)) { errs.Add("my.ini line " + (line + 1) + " isn't a setting ServNook overrides."); continue; }
                ini.CommentOut(line, "overridden by ServNook's --" + en.Key.Replace('_', '-') + " startup argument");
                p.Changes.Add("my.ini: commented out " + en.RawKey + " (line " + (line + 1) + ")");
            }

            string charset = null;
            string cs; if (e.Maria.TryGetValue("mysqld.character_set_server", out cs)) charset = cs.Trim().ToLowerInvariant();
            if (charset == null) charset = (ini.Get("mysqld", "character_set_server") ?? (mi != null ? mi.LiveValue("character_set_server") : null) ?? "utf8mb4").ToLowerInvariant();

            foreach (var s in MariaCatalog.All)
            {
                string raw;
                if (!e.Maria.TryGetValue(s.Id, out raw)) continue;
                string v = (raw ?? "").Trim();
                SysVar live = null;
                if (mi != null) mi.Live.TryGetValue(s.Key, out live);
                string write = v;
                switch (s.Kind)
                {
                    case "int":
                        long lo = 10, hi = 100000; long t;
                        if (live != null) { long.TryParse(live.Min, out t); if (t > 0) lo = t; if (long.TryParse(live.Max, out t) && t > 0) hi = Math.Min(t, 100000); }
                        long iv;
                        if (!long.TryParse(v, out iv) || iv < lo || iv > hi) { errs.Add(s.Key + " must be a whole number from " + lo + " to " + hi + "."); continue; }
                        write = iv.ToString(CultureInfo.InvariantCulture);
                        break;
                    case "size":
                        var b = MariaCatalog.Bytes(v);
                        long min = s.Key == "max_allowed_packet" ? 1024 : s.Key == "innodb_log_file_size" ? 4L << 20 : 5L << 20;
                        long max = s.Key == "max_allowed_packet" ? 1L << 30 : 1L << 40;
                        long lmin; if (live != null && long.TryParse(live.Min, out lmin) && lmin > 0) min = Math.Max(min, lmin);
                        if (b == null || b < min || b > max) { errs.Add(s.Key + " must be between " + MariaCatalog.Human(min) + " and " + MariaCatalog.Human(max) + "."); continue; }
                        write = MariaCatalog.SizeText(b.Value);
                        break;
                    case "enum":
                        int lv;
                        int top = live != null && int.TryParse(live.Max, out lv) ? lv : 2;
                        if (!int.TryParse(v, out lv) || lv < 0 || lv > top) { errs.Add(s.Key + " must be 0, 1 or 2."); continue; }
                        break;
                    case "bool":
                        string nb = MariaCatalog.Norm("bool", v);
                        if (live != null && live.Enum.Length > 2 && live.Enum.Contains(v.ToUpperInvariant())) { write = v.ToUpperInvariant(); break; }
                        if (nb != "1" && nb != "0") { errs.Add(s.Key + " must be on or off."); continue; }
                        write = nb;
                        break;
                    case "float":
                        double dv;
                        if (!double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out dv) || dv < 0 || dv > 31536000) { errs.Add(s.Key + " must be a number of seconds from 0 to 31536000 (decimals allowed)."); continue; }
                        write = dv.ToString("0.######", CultureInfo.InvariantCulture);
                        break;
                    case "set":
                        var toks = v.ToUpperInvariant().Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                        var allowed = live != null && live.Enum.Length > 0 ? live.Enum.Select(x => x.ToUpperInvariant()).ToArray() : AriaTokens;
                        var bad = toks.Where(x => !allowed.Contains(x)).ToList();
                        if (toks.Count == 0 || bad.Count > 0) { errs.Add(s.Key + ": unknown option(s) " + string.Join(", ", bad) + ". Allowed: " + string.Join(", ", allowed) + "."); continue; }
                        if (toks.Contains("OFF") && toks.Count > 1) { errs.Add(s.Key + ": OFF can't be combined with other options."); continue; }
                        write = string.Join(",", toks);
                        break;
                    case "charset":
                        write = v.ToLowerInvariant();
                        if (!Regex.IsMatch(write, @"^[a-z0-9_]+$") || (mi != null && mi.Collations.Count > 0 && !mi.Collations.ContainsKey(write))) { errs.Add("'" + v + "' isn't a character set this MariaDB supports."); continue; }
                        break;
                    case "collation":
                        write = v.ToLowerInvariant();
                        if (!write.StartsWith(charset + "_")) { errs.Add("Collation " + write + " doesn't belong to character set " + charset + "."); continue; }
                        List<string> cl;
                        if (mi != null && mi.Collations.TryGetValue(charset, out cl) && !cl.Contains(write)) { errs.Add("MariaDB " + mi.Version + " has no collation " + write + "."); continue; }
                        break;
                }
                if (s.Section == "mysqld")
                {
                    var eff = ini.Effective(s.Key);
                    if (eff != null && eff.Section != "mysqld")
                    { errs.Add(s.Key + " is set in the [" + eff.Section + "] section of my.ini (line " + (eff.Line + 1) + "), which overrides [mysqld]. Edit my.ini by hand."); continue; }
                    if (mi != null && mi.Supports(s.Key) == false)
                    { errs.Add(s.Key + " isn't supported by MariaDB " + mi.Version + "."); continue; }
                }
                ini.Set(s.Section, s.Key, write);
                p.Changes.Add(s.Section == "client" ? "[client] " + s.Key + " = " + write : s.Key + " = " + write);
                if (s.Section != "mysqld") continue;
                p.Expect[s.Key] = MariaCatalog.Norm(s.Kind, write);
                if (mi != null && mi.Running && mi.Connected && mi.Dynamic(s.Key) == true)
                    p.Live.Add(new KeyValuePair<string, string>(s.Key, MariaCatalog.SqlLiteral(s.Kind, write)));
                else if (mi != null && mi.Running) p.MariaRestart = true;
            }
        }

        // ================================================================== apply

        static void Report(Action<string> progress, string msg) { if (progress != null) progress(msg); Log.Info(msg); }

        public static ApplyResult Apply(ConfigPlan p, ServNookConfig cfg, MariaInfo mi, Action<string> progress)
        {
            var res = new ApplyResult();
            res.Notes.AddRange(p.Notes);
            if (p.HttpdText == null && p.MyIniText == null && !p.CfgChanged)
            {
                res.Outcome = ApplyOutcome.NoChanges; res.Message = "Nothing to change.";
                return res;
            }
            bool apacheRunning = Services.Apache.IsRunning, mariaRunning = Services.MariaDb.IsRunning;
            if (p.HttpdText != null) p.ApacheAffected = true;
            if (p.MyIniText != null) p.MariaAffected = true;

            // 1. Validate the candidate configuration with the real programs.
            try { Directory.CreateDirectory(CheckDir); } catch { }
            if ((p.ApacheAffected || p.HttpdText != null) && File.Exists(K.Httpd))
            {
                Report(progress, "Testing the Apache configuration (httpd -t)…");
                string conf = K.HttpdConf;
                if (p.HttpdText != null)
                {
                    conf = Path.Combine(CheckDir, "httpd.conf");
                    File.WriteAllText(conf, p.HttpdText, new UTF8Encoding(false));
                }
                var r = Sys.Run(K.Httpd, Services.Apache.Args(p.NewCfg, conf) + " -t", Services.Env(), 30000, Path.Combine(K.Apache, "bin"));
                if (r.ExitCode != 0)
                    return Fail(res, "Apache rejected the new configuration, so nothing was changed.", r.Output.Trim());
            }
            if (p.MyIniText != null && MariaInfo.Server != null)
            {
                Report(progress, "Checking my.ini with mariadbd…");
                string cand = Path.Combine(CheckDir, "my.ini");
                File.WriteAllBytes(cand, p.MyIniBytes);
                var chk = MariaInfo.Check(cand, p.NewCfg);
                if (!chk.Ok) return Fail(res, "MariaDB rejected the new my.ini, so nothing was changed.", string.Join("\n", chk.Errors));
                foreach (var kv in p.Expect)
                {
                    string got;
                    var s = MariaCatalog.Get(kv.Key);
                    if (chk.Vars.TryGetValue(kv.Key, out got) && MariaCatalog.Norm(s.Kind, got) != kv.Value)
                    {
                        if (s.Kind == "size" || s.Kind == "int") res.Notes.Add("MariaDB will use " + kv.Key + " = " + got + " (it adjusts this value).");
                        else return Fail(res, "MariaDB would read " + kv.Key + " as " + got + ", not the value you chose, so nothing was changed.", "Another line in my.ini, an included file or a startup argument overrides it.");
                    }
                }
                foreach (var w in chk.Warnings) res.Notes.Add("MariaDB: " + w);
                Report(progress, "Test-starting MariaDB with the new settings (scratch data folder)…");
                string start = MariaInfo.ScratchStart(cand, p.NewCfg);
                if (start != null) return Fail(res, "MariaDB couldn't start with the new settings, so nothing was changed.", start);
            }

            // 2. Back up the files about to change.
            var files = new List<string>();
            if (p.HttpdText != null) files.Add(K.HttpdConf);
            if (p.MyIniText != null) files.Add(K.MyIni);
            if (p.CfgChanged) files.Add(K.ConfigFile);
            try { res.BackupDir = Backup(files, string.Join("; ", p.Changes)); }
            catch (Exception ex) { return Fail(res, "Couldn't back up the configuration files, so nothing was changed.", ex.Message); }

            // 3. Write.
            var old = cfg.Clone();
            Report(progress, "Saving…");
            try
            {
                if (p.HttpdText != null) WriteFile(K.HttpdConf, new UTF8Encoding(false).GetBytes(p.HttpdText));
                if (p.MyIniText != null) WriteFile(K.MyIni, p.MyIniBytes);
                if (p.CfgChanged) { CopyCfg(p.NewCfg, cfg); cfg.Save(); }
                if (old.MysqlPort != cfg.MysqlPort) new Installer(cfg).UpdatePmaConfig();
            }
            catch (Exception ex)
            {
                RestoreFiles(res.BackupDir, cfg, old);
                return Fail(res, "Couldn't save: " + ex.Message + " Check that the file isn't read-only or open in another program. The previous files were restored.", ex.ToString());
            }

            var restarted = new List<Service>();
            try
            {
                // 4. Settings MariaDB can change while running.
                if (p.Live.Count > 0 && mariaRunning && !p.MariaRestart)
                {
                    Report(progress, "Applying to the running MariaDB…");
                    string err;
                    Services.MariaDb.Query(string.Join("\n", p.Live.Select(kv => "SET GLOBAL " + kv.Key + " = " + kv.Value + ";")), cfg.MysqlRootPassword, out err);
                    if (!string.IsNullOrEmpty(err)) throw new Exception("MariaDB refused the change: " + err);
                    VerifyVars(p, cfg, res, 5000, false);
                }

                // 5. Restart only what is affected.
                if (p.MariaRestart && mariaRunning)
                {
                    var before = mi != null && mi.Databases.Count > 0 ? mi.Databases : Databases(cfg);
                    Report(progress, "Restarting MariaDB…");
                    Services.MariaDb.Busy = true;
                    restarted.Add(Services.MariaDb);
                    Services.MariaDb.Restart();
                    Report(progress, "Checking MariaDB…");
                    var after = Databases(cfg);
                    if (after.Count == 0) throw new Exception("MariaDB restarted but doesn't answer queries.");
                    var missing = before.Where(d => !after.Contains(d)).ToList();
                    if (missing.Count > 0) throw new Exception("These databases are not visible after the restart: " + string.Join(", ", missing));
                    VerifyVars(p, cfg, res, 3000, true);
                    Services.MariaDb.Busy = false;
                }
                if (p.ApacheAffected && apacheRunning)
                {
                    Report(progress, "Restarting Apache…");
                    Services.Apache.Busy = true;
                    restarted.Add(Services.Apache);
                    Services.Apache.Restart();
                    Report(progress, "Checking Apache…");
                    Updates.HttpOk(cfg, "/");
                    Services.Apache.Busy = false;
                }
            }
            catch (Exception ex)
            {
                // 6. Roll back.
                Log.Error("Configuration change failed: " + ex.Message);
                Report(progress, "Restoring the previous configuration…");
                RestoreFiles(res.BackupDir, cfg, old);
                var rollbackErrors = new List<string>();
                if (p.Live.Count > 0 && mariaRunning && !p.MariaRestart && mi != null)
                {
                    string err;
                    var undo = p.Live.Where(kv => mi.LiveValue(kv.Key) != null)
                                     .Select(kv => "SET GLOBAL " + kv.Key + " = " + MariaCatalog.SqlLiteral(MariaCatalog.Get(kv.Key).Kind, mi.LiveValue(kv.Key)) + ";");
                    try { Services.MariaDb.Query(string.Join("\n", undo), cfg.MysqlRootPassword, out err); } catch { }
                }
                foreach (var s in restarted)
                {
                    try { if (s.IsRunning) s.Stop(); s.Start(); }
                    catch (Exception re) { rollbackErrors.Add(s.Title + ": " + re.Message); }
                    s.Busy = false;
                }
                string tail = restarted.Contains(Services.MariaDb) ? "\n\nmariadb.err:\n" + Sys.Tail(Services.MariaDb.LogFile, 12)
                            : restarted.Contains(Services.Apache) ? "\n\napache-error.log:\n" + Sys.Tail(Services.Apache.LogFile, 12) : "";
                if (rollbackErrors.Count > 0)
                {
                    res.Outcome = ApplyOutcome.RollbackFailed;
                    res.Message = "The change failed and the previous configuration was restored, but " + string.Join(", ", restarted.Select(s => s.Title)) +
                                  " didn't start again. The backup is in " + res.BackupDir + ".";
                    res.Details = ex.Message + "\n\n" + string.Join("\n", rollbackErrors) + tail;
                }
                else
                {
                    res.Outcome = ApplyOutcome.Failed;
                    res.Message = "Failed — previous configuration restored. " + FirstLine(ex.Message);
                    res.Details = ex.Message + tail;
                }
                return res;
            }

            if (restarted.Count > 0)
            {
                res.Outcome = ApplyOutcome.Applied;
                res.Message = "Applied successfully. " + string.Join(" and ", restarted.Select(s => s.Title)) + " restarted and " + (restarted.Count == 1 ? "is" : "are") + " running.";
            }
            else if (p.Live.Count > 0 && mariaRunning)
            {
                res.Outcome = ApplyOutcome.AppliedLive;
                res.Message = "Applied immediately to the running MariaDB, and saved to my.ini.";
            }
            else
            {
                res.Outcome = ApplyOutcome.Saved;
                var later = new List<string>();
                if (p.ApacheAffected && !apacheRunning) later.Add("Apache");
                if (p.MariaAffected && !mariaRunning) later.Add("MariaDB");
                res.Message = "Saved and validated." + (later.Count > 0 ? " Takes effect when " + string.Join(" and ", later) + " starts." : "");
            }
            Log.Info("Configuration: " + res.Message + " (" + string.Join("; ", p.Changes) + ")");
            return res;
        }

        static string FirstLine(string s) { return (s ?? "").Split('\n')[0]; }

        static ApplyResult Fail(ApplyResult r, string msg, string details)
        {
            r.Outcome = ApplyOutcome.Failed; r.Message = msg; r.Details = details ?? "";
            Log.Error(msg + (string.IsNullOrEmpty(details) ? "" : " " + FirstLine(details)));
            return r;
        }

        static List<string> Databases(ServNookConfig cfg)
        {
            string err;
            try { return Services.MariaDb.Query("SELECT SCHEMA_NAME FROM information_schema.SCHEMATA;", cfg.MysqlRootPassword, out err).Select(r => r[0]).ToList(); }
            catch { return new List<string>(); }
        }

        /// <summary>Compare live values with what was asked for. Restart: mismatches are errors (the file didn't take effect).</summary>
        static void VerifyVars(ConfigPlan p, ServNookConfig cfg, ApplyResult res, int waitMs, bool strict)
        {
            if (p.Expect.Count == 0) return;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Dictionary<string, string> got = null;
            List<string> wrong = null;
            do
            {
                string err;
                var rows = Services.MariaDb.Query("SELECT LOWER(VARIABLE_NAME), GLOBAL_VALUE FROM information_schema.SYSTEM_VARIABLES WHERE LOWER(VARIABLE_NAME) IN (" +
                                                  string.Join(",", p.Expect.Keys.Select(k => "'" + k + "'")) + ");", cfg.MysqlRootPassword, out err);
                got = rows.Where(r => r.Length >= 2).ToDictionary(r => r[0], r => r[1]);
                wrong = p.Expect.Where(kv => got.ContainsKey(kv.Key) && MariaCatalog.Norm(MariaCatalog.Get(kv.Key).Kind, got[kv.Key]) != kv.Value).Select(kv => kv.Key).ToList();
                if (wrong.Count == 0) return;
                Thread.Sleep(500);
            } while (sw.ElapsedMilliseconds < waitMs);
            foreach (var k in wrong)
            {
                var kind = MariaCatalog.Get(k).Kind;
                string msg = k + " is " + got[k] + " in the running server (asked for " + p.Expect[k] + ").";
                if (kind == "size" || kind == "int") res.Notes.Add(msg + " MariaDB rounds or limits this value.");
                else if (strict) throw new Exception(msg + " Something else overrides my.ini.");
                else res.Notes.Add(msg);
            }
        }

        static void CopyCfg(ServNookConfig from, ServNookConfig to)
        {
            to.Php = from.Php; to.HttpPort = from.HttpPort; to.MysqlPort = from.MysqlPort; to.LanAccess = from.LanAccess;
        }

        static void WriteFile(string path, byte[] bytes)
        {
            // Write next to the file, then copy over it: the original is never missing, even if ServNook stops halfway.
            string tmp = path + ".servnook-new";
            File.WriteAllBytes(tmp, bytes);
            File.Copy(tmp, path, true);
            File.Delete(tmp);
        }

        // ------------------------------------------------------------------ backups of config files

        /// <summary>Copy the given files to config\backups\&lt;time&gt;\ with a note of what changed. Keeps the newest 30.</summary>
        public static string Backup(List<string> files, string reason)
        {
            string dir = Path.Combine(BackupRoot, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(dir);
            var manifest = new StringBuilder();
            manifest.AppendLine("ServNook configuration backup, " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            manifest.AppendLine("Change: " + reason);
            foreach (var f in files)
            {
                string rel = f.Substring(K.Root.Length).TrimStart('\\', '/');
                string dest = Path.Combine(dir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                if (File.Exists(f)) { File.Copy(f, dest, true); manifest.AppendLine("file: " + rel); }
                else manifest.AppendLine("absent: " + rel);
            }
            File.WriteAllText(Path.Combine(dir, "backup.txt"), manifest.ToString());
            try
            {
                foreach (var d in Directory.GetDirectories(BackupRoot).OrderByDescending(x => x).Skip(30)) Directory.Delete(d, true);
            }
            catch { }
            return dir;
        }

        /// <summary>Put backed-up files back and restore the in-memory settings.</summary>
        static void RestoreFiles(string backupDir, ServNookConfig cfg, ServNookConfig old)
        {
            if (backupDir == null) return;
            var manifest = File.ReadAllLines(Path.Combine(backupDir, "backup.txt"));
            foreach (var l in manifest)
            {
                if (!l.StartsWith("file: ")) continue;
                string rel = l.Substring(6);
                if (rel.Equals("servnook.json", StringComparison.OrdinalIgnoreCase)) continue;
                try { File.Copy(Path.Combine(backupDir, rel), Path.Combine(K.Root, rel), true); }
                catch (Exception e) { Log.Error("Couldn't restore " + rel + ": " + e.Message); }
            }
            bool portBack = cfg.MysqlPort != old.MysqlPort;
            CopyCfg(old, cfg);
            try { cfg.Save(); } catch (Exception e) { Log.Error("Couldn't restore servnook.json: " + e.Message); }
            if (portBack) try { new Installer(cfg).UpdatePmaConfig(); } catch { }
        }

        /// <summary>Restore a whole backup set made by <see cref="Backup"/> (Advanced › backups).</summary>
        public static List<string> BackupSets()
        {
            if (!Directory.Exists(BackupRoot)) return new List<string>();
            return Directory.GetDirectories(BackupRoot).OrderByDescending(x => x).ToList();
        }
    }
}
