// Configuration-manager integration test: drives WebHut's real ConfigManager against a real MariaDB and Apache
// (Linux binaries standing in for the Windows .exe files). Run through tests/config-integration.sh.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using WebHut;

static class ConfigInt
{
    static int _pass, _fail;
    static void Check(bool c, string name, string detail = null)
    {
        if (c) { _pass++; Console.WriteLine("  ok   " + name); }
        else { _fail++; Console.WriteLine("  FAIL " + name + (detail != null ? "\n       " + detail.Replace("\n", "\n       ") : "")); }
    }

    static WebHutConfig Cfg { get { return Services.Cfg; } }

    static string Q(string sql)
    {
        string err;
        var rows = Services.MariaDb.Query(sql, Cfg.MysqlRootPassword, out err);
        return rows.Count == 0 ? (err.Length > 0 ? "ERR " + err : "") : string.Join("|", rows[0]);
    }

    static ApplyResult Run(ConfigEdits e)
    {
        var mi = MariaInfo.Probe(Cfg);
        var ai = ApacheInfo.Probe(Cfg);
        var plan = ConfigManager.Plan(e, Cfg, mi, ai);
        return ConfigManager.Apply(plan, Cfg, mi, m => Console.WriteLine("       … " + m));
    }

    static string Get(string host, string path)
    {
        try
        {
            var req = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + Cfg.HttpPort + path);
            req.Host = host; req.Proxy = null; req.Timeout = 5000;
            using (var r = (HttpWebResponse)req.GetResponse()) using (var sr = new StreamReader(r.GetResponseStream())) return sr.ReadToEnd();
        }
        catch (WebException ex) { var r = ex.Response as HttpWebResponse; return r == null ? "ERR " + ex.Message : "HTTP " + (int)r.StatusCode; }
    }

    static int Main(string[] args)
    {
        Log.ToConsole = false;
        bool doApache = args.Contains("apache"), doMaria = args.Contains("mariadb");

        if (doMaria)
        {
            Console.WriteLine("== MariaDB");
            Services.MariaDb.Start();
            Q("CREATE DATABASE IF NOT EXISTS shop; CREATE TABLE IF NOT EXISTS shop.orders (id INT PRIMARY KEY) ENGINE=InnoDB; USE shop; INSERT IGNORE INTO orders SELECT seq FROM seq_1_to_25;");
            var mi = MariaInfo.Probe(Cfg);
            Check(mi.Running && mi.Connected && mi.Live.ContainsKey("max_connections"), "probe reads live variables from the running server", mi.ConnectError);
            Check(mi.Dynamic("max_connections") == true && mi.Dynamic("innodb_doublewrite") == false, "dynamic vs restart-only comes from the server (" + mi.Version + ")");
            Check(mi.NextStart != null && mi.NextStart.Ok && mi.NextStart.Vars["port"] == Cfg.MysqlPort.ToString() && mi.NextStart.Vars["bind_address"] == "127.0.0.1",
                  "next-start values include WebHut's startup arguments", mi.NextStart == null ? "" : string.Join("; ", mi.NextStart.Errors));
            Check(mi.Collations.ContainsKey("utf8mb4") && mi.Collations["utf8mb4"].Contains("utf8mb4_unicode_ci"), "collations listed from the server");
            string pidBefore = File.ReadAllText(Path.Combine(K.Pids, "mariadb.pid")).Trim();

            var e = new ConfigEdits();
            e.Maria["mysqld.max_connections"] = "150";
            e.Maria["mysqld.slow_query_log"] = "1";
            e.Maria["mysqld.long_query_time"] = "0.5";
            var r = Run(e);
            Check(r.Outcome == ApplyOutcome.AppliedLive, "dynamic settings applied immediately: " + r.Message, r.Details);
            Check(Q("SELECT @@max_connections, @@slow_query_log, @@long_query_time") == "150|1|0.500000", "running server uses them", Q("SELECT @@max_connections, @@slow_query_log, @@long_query_time"));
            Check(File.ReadAllText(Path.Combine(K.Pids, "mariadb.pid")).Trim() == pidBefore, "no restart for dynamic settings");
            Check(File.ReadAllText(K.MyIni).Contains("max_connections          = 150") && r.BackupDir != null && File.Exists(Path.Combine(r.BackupDir, "config", "mariadb", "my.ini")), "saved to my.ini, previous my.ini backed up");

            e = new ConfigEdits();
            e.Maria["mysqld.innodb_doublewrite"] = "0";
            e.Maria["mysqld.innodb_buffer_pool_size"] = "128M";
            r = Run(e);
            Check(r.Outcome == ApplyOutcome.Applied, "restart-only setting: " + r.Message, r.Details);
            Check(Q("SELECT @@innodb_doublewrite, @@innodb_buffer_pool_size") == "0|134217728", "values active after the restart", Q("SELECT @@innodb_doublewrite, @@innodb_buffer_pool_size"));
            Check(Q("SELECT COUNT(*) FROM shop.orders") == "25", "existing databases still accessible", Q("SELECT COUNT(*) FROM shop.orders"));

            // Invalid values never reach the file.
            string iniBefore = File.ReadAllText(K.MyIni);
            e = new ConfigEdits();
            e.Maria["mysqld.aria_recover_options"] = "BACKUP,BOGUS";
            bool rejected = false;
            try { Run(e); } catch (ConfigException) { rejected = true; }
            Check(rejected && File.ReadAllText(K.MyIni) == iniBefore, "invalid option value rejected before writing");

            // An option mariadbd doesn't know: caught by mariadbd --help --verbose on the candidate.
            e = new ConfigEdits();
            e.Maria["mysqld.max_connections"] = "160";
            var plan = ConfigManager.Plan(e, Cfg, MariaInfo.Probe(Cfg), null);
            plan.MyIniText = plan.MyIniText.Replace("max_connections          = 160", "max_connections          = 160\r\nbogus_option_x = 1");
            plan.MyIniBytes = System.Text.Encoding.UTF8.GetBytes(plan.MyIniText);
            r = ConfigManager.Apply(plan, Cfg, MariaInfo.Probe(Cfg), null);
            Check(r.Outcome == ApplyOutcome.Failed && r.Details.Contains("bogus_option_x") && File.ReadAllText(K.MyIni) == iniBefore, "unknown option rejected by mariadbd validation; nothing changed", r.Message + " / " + r.Details);

            // A buffer pool that can't be allocated: caught by the scratch start, real server untouched.
            e = new ConfigEdits();
            e.Maria["mysqld.innodb_buffer_pool_size"] = "64T";
            try { r = Run(e); } catch (ConfigException ex) { r = new ApplyResult { Outcome = ApplyOutcome.Failed, Message = ex.Message }; }
            Check(r.Outcome == ApplyOutcome.Failed && File.ReadAllText(K.MyIni) == iniBefore && Q("SELECT 1") == "1", "impossible setting refused; live server untouched", r.Message);

            // Port change: restart on the new port, my.ini not touched, phpMyAdmin/Apache follow webhut.json.
            int oldPort = Cfg.MysqlPort, newPort = oldPort + 2;
            e = new ConfigEdits { MysqlPort = newPort };
            r = Run(e);
            Check(r.Outcome == ApplyOutcome.Applied && Services.MariaDb.ActivePort == newPort && Q("SELECT @@port") == newPort.ToString(), "port change restarts MariaDB on the new port", r.Message + " " + r.Details);
            Check(File.ReadAllText(K.MyIni) == iniBefore && WebHutConfig.Load().MysqlPort == newPort, "port saved in WebHut settings, not my.ini");

            // Simulated failed restart: the new port gets taken between validation and restart -> rollback.
            int busyPort = newPort + 2;
            var plan2 = ConfigManager.Plan(new ConfigEdits { MysqlPort = busyPort }, Cfg, MariaInfo.Probe(Cfg), null);
            var blocker = new TcpListener(IPAddress.Loopback, busyPort); blocker.Start();
            r = ConfigManager.Apply(plan2, Cfg, MariaInfo.Probe(Cfg), null);
            blocker.Stop();
            Check(r.Outcome == ApplyOutcome.Failed && r.Message.StartsWith("Failed — previous configuration restored"), "failed restart reported: " + r.Message, r.Details);
            Check(Cfg.MysqlPort == newPort && WebHutConfig.Load().MysqlPort == newPort && Services.MariaDb.IsRunning && Q("SELECT @@port") == newPort.ToString(), "rolled back: previous port restored and MariaDB running again");
            Check(Q("SELECT COUNT(*) FROM shop.orders") == "25", "databases intact after the rollback");

            // Persistence: a plain stop/start keeps everything.
            Services.MariaDb.Stop(false);
            Services.MariaDb.Start();
            Check(Q("SELECT @@max_connections, @@innodb_doublewrite, @@slow_query_log") == "150|0|1", "settings persist across a normal restart");

            // Overridden my.ini option detected and resolvable.
            File.AppendAllText(K.MyIni, "[mysqld]\r\nport = 9999\r\n");
            var ini = OptionFile.Load(K.MyIni);
            var portLine = ini.Entries().First(x => x.Key == "port");
            e = new ConfigEdits(); e.CommentOut.Add(portLine.Line);
            plan = ConfigManager.Plan(e, Cfg, MariaInfo.Probe(Cfg), null);
            Check(plan.MyIniText.Contains("# port = 9999") && !plan.MariaRestart, "port line in my.ini can be commented out without a restart");
            r = ConfigManager.Apply(plan, Cfg, MariaInfo.Probe(Cfg), null);
            Check(r.Ok && OptionFile.Load(K.MyIni).Get("mysqld", "port") == null, "applied: " + r.Message, r.Details);

            // Restore defaults through the same path.
            e = new ConfigEdits();
            foreach (var s in MariaCatalog.All) e.Maria[s.Id] = s.Default;
            r = Run(e);
            Check(r.Ok && Q("SELECT @@max_connections, @@innodb_doublewrite, @@slow_query_log, @@innodb_buffer_pool_size") == "100|1|0|268435456", "WebHut defaults restored: " + r.Message, r.Details);
            Services.MariaDb.Stop(false);
        }

        if (doApache)
        {
            Console.WriteLine("== Apache");
            Services.Apache.Start();
            string custom = Path.Combine(K.ApacheConf, "custom", "mine.conf");
            string customText = File.ReadAllText(custom);
            Check(Get("blog.localhost", "/").Contains("blog-public"), "generated vhost works before changes");
            var ai = ApacheInfo.Probe(Cfg);
            Check(ai.Loaded.Contains("rewrite_module") && ai.Loaded.Contains("expires_module") && ai.Version.Length > 0, "loaded modules read from httpd -M (Apache " + ai.Version + ")", ai.LoadedError);

            var e = new ConfigEdits();
            e.Apache["LogLevel"] = "info";
            e.Apache["KeepAliveTimeout"] = "3";
            e.Apache["Indexes"] = "Off";
            e.Modules["expires_module"] = false;
            var r = Run(e);
            Check(r.Outcome == ApplyOutcome.Applied, "Apache settings applied with a restart: " + r.Message, r.Details);
            ai = ApacheInfo.Probe(Cfg);
            Check(!ai.Loaded.Contains("expires_module") && ai.Loaded.Contains("rewrite_module"), "module unloaded, others kept");
            Check(Get("localhost", "/emptydir/") == "HTTP 403" && Get("blog.localhost", "/").Contains("blog-public"), "directory listing off; generated vhosts still work", Get("localhost", "/emptydir/"));
            Check(File.ReadAllText(custom) == customText && Get("mine.localhost", "/").Contains("mine-ok"), "custom vhost untouched and working");

            string conf = File.ReadAllText(K.HttpdConf);
            var bad = ConfigManager.Plan(new ConfigEdits(), Cfg, null, ai);
            var e2 = new ConfigEdits(); e2.Apache["LogLevel"] = "notice";
            bad = ConfigManager.Plan(e2, Cfg, null, ai);
            bad.HttpdText += "\r\nBogusDirective on\r\n";
            r = ConfigManager.Apply(bad, Cfg, null, null);
            Check(r.Outcome == ApplyOutcome.Failed && r.Details.Contains("BogusDirective") && File.ReadAllText(K.HttpdConf) == conf, "invalid httpd.conf rejected by httpd -t; nothing changed", r.Details);

            int oldPort = Cfg.HttpPort;
            r = Run(new ConfigEdits { HttpPort = oldPort + 1 });
            Check(r.Outcome == ApplyOutcome.Applied && Get("blog.localhost", "/").Contains("blog-public"), "HTTP port change: Apache answers on " + Cfg.HttpPort, r.Details);

            r = Run(new ConfigEdits { Php = "8.4" });
            Check(r.Outcome == ApplyOutcome.Applied && Get("localhost", "/v.php").Contains("PHP_DIR=") && Get("localhost", "/v.php").Contains("/php/8.4"), "PHP version switch restarts Apache with the new PHP_DIR", Get("localhost", "/v.php"));

            // Failed restart: port taken between validation and restart -> rollback to the previous port.
            var plan3 = ConfigManager.Plan(new ConfigEdits { HttpPort = Cfg.HttpPort + 1 }, Cfg, null, ai);
            var blocker = new TcpListener(IPAddress.Loopback, Cfg.HttpPort + 1); blocker.Start();
            int before = Cfg.HttpPort;
            r = ConfigManager.Apply(plan3, Cfg, null, null);
            blocker.Stop();
            Check(r.Outcome == ApplyOutcome.Failed && Cfg.HttpPort == before && Services.Apache.IsRunning && Get("blog.localhost", "/").Contains("blog-public"), "failed Apache restart rolled back and running again: " + r.Message, r.Details);

            e = new ConfigEdits();
            e.Apache["LogLevel"] = "warn"; e.Apache["Indexes"] = "On"; e.Modules["expires_module"] = true;
            r = Run(e);
            Check(r.Ok && ApacheInfo.Probe(Cfg).Loaded.Contains("expires_module"), "module turned back on: " + r.Message, r.Details);
            Services.Apache.Stop();
        }

        Console.WriteLine();
        Console.WriteLine(_pass + " passed, " + _fail + " failed");
        return _fail == 0 ? 0 : 1;
    }
}
