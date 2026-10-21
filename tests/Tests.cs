// Logic tests for WebHut's platform-independent parts. Run with Mono:
//   ./tests/run.sh
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using WebHut;

static class Tests
{
    static int _pass, _fail;

    static void Check(bool cond, string name, string detail = null)
    {
        if (cond) { _pass++; Console.WriteLine("  ok   " + name); }
        else { _fail++; Console.WriteLine("  FAIL " + name + (detail != null ? "\n       " + detail : "")); }
    }

    static int Main(string[] args)
    {
        string port = args.Length > 0 ? args[0] : null;
        Console.WriteLine("Root: " + K.Root);

        // ---------------------------------------------------------------- JSON + config
        Console.WriteLine("JSON / config");
        var cfg = new WebHutConfig { Php = "8.5", Node = "24", HttpPort = 8080, MysqlPort = 3307, MysqlRootPassword = "p\"w", StartOnLaunch = true };
        cfg.Save();
        var loaded = WebHutConfig.Load();
        Check(loaded.HttpPort == 8080 && loaded.MysqlPort == 3307, "config ports round-trip");
        Check(loaded.MysqlRootPassword == "p\"w", "config password with quote round-trips");
        Check(loaded.StartOnLaunch && loaded.MinimizeToTray, "config booleans round-trip");
        Check(File.ReadAllText(K.ConfigFile).Contains("\n  \"httpPort\": 8080"), "webhut.json is pretty-printed");

        var apps = new List<NodeApp>
        {
            new NodeApp { Name = "api", Dir = "www/api", Command = "npm run dev", Port = 3000, AutoStart = true, Env = new Dictionary<string,string>{{"DEBUG","1"}} },
            new NodeApp { Name = "web", Dir = "www/web", Command = "npx vite", Port = 5173 }
        };
        NodeApp.SaveAll(apps);
        var apps2 = NodeApp.LoadAll();
        Check(apps2.Count == 2 && apps2[0].Env["DEBUG"] == "1" && apps2[0].AutoStart && apps2[1].Port == 5173, "node-apps.json round-trip");
        Check(apps2[0].FullDir == Path.Combine(K.Root, "www", "api"), "relative app folder resolves under root", apps2[0].FullDir);
        Check(!NodeApp.NameRule.IsMatch("My App") && !NodeApp.NameRule.IsMatch("-x") && NodeApp.NameRule.IsMatch("my-app2"), "app name rule");

        // ---------------------------------------------------------------- versions
        Check(WebHutConfig.VersionKey("2.4.68 (VS18)") == new Version(2, 4, 68), "VersionKey parses Apache label");
        Check(WebHutConfig.VersionKey("8.5.11") > WebHutConfig.VersionKey("8.5.9"), "VersionKey orders patch numbers numerically");
        Check(WebHutConfig.VersionKey("24") == new Version(24, 0), "VersionKey handles a bare major");

        // ---------------------------------------------------------------- config files
        Console.WriteLine("Config files");
        var inst = new Installer(loaded);
        inst.CreateFolders();
        inst.WriteConfigFiles(true);
        string httpd = File.ReadAllText(K.HttpdConf);
        Check(httpd.Contains("Listen 127.0.0.1:${WEBHUT_HTTP_PORT}"), "httpd.conf written from embedded overlay");
        Check(httpd.Contains("\r\n"), "config files use Windows line endings");
        Check(File.Exists(K.MyIni) && File.Exists(Path.Combine(K.ApacheConf, "php-mod.conf")), "my.ini and php-mod.conf written");
        Check(File.Exists(Path.Combine(K.Www, "index.php")), "dashboard written to empty www");
        File.WriteAllText(K.HttpdConf, "user edit");
        inst.WriteConfigFiles(false);
        Check(File.ReadAllText(K.HttpdConf) == "user edit", "WriteConfigFiles(false) keeps user edits");
        inst.WriteConfigFiles(true);

        string phpDir = K.PhpDir("8.5");
        Directory.CreateDirectory(Path.Combine(phpDir, "ext"));
        foreach (var e in new[] { "curl", "mbstring", "openssl", "pdo_mysql", "intl" })
            File.WriteAllText(Path.Combine(phpDir, "ext", "php_" + e + ".dll"), "");
        inst.WritePhpIni(phpDir);
        string ini = File.ReadAllText(Path.Combine(phpDir, "php.ini"));
        Check(ini.Contains("extension = curl") && ini.Contains("extension = intl"), "php.ini enables extensions that exist");
        Check(!ini.Contains("extension = gd") && !ini.Contains("zend_extension = opcache"), "php.ini skips missing extensions (and built-in opcache)");
        Check(ini.Contains("extension_dir = \"" + K.Fwd(phpDir) + "/ext\""), "php.ini extension_dir is absolute with forward slashes");
        Check(!ini.Contains("{{"), "php.ini has no leftover placeholders");
        File.WriteAllText(Path.Combine(phpDir, "ext", "php_opcache.dll"), "");
        inst.WritePhpIni(phpDir);
        Check(File.ReadAllText(Path.Combine(phpDir, "php.ini")).Contains("zend_extension = opcache"), "opcache loaded as zend_extension when the DLL exists (PHP <= 8.4)");

        Directory.CreateDirectory(K.Pma);
        loaded.MysqlRootPassword = "";
        loaded.MysqlPort = 3307;
        inst.WritePmaConfig();
        string pma = File.ReadAllText(Path.Combine(K.Pma, "config.inc.php"));
        var secret = System.Text.RegularExpressions.Regex.Match(pma, @"blowfish_secret'\] = '([A-Za-z0-9]+)'");
        Check(secret.Success && secret.Groups[1].Value.Length == 32, "phpMyAdmin secret is 32 random chars");
        Check(pma.Contains("['port']            = '3307'") && pma.Contains("AllowNoPassword'] = true"), "phpMyAdmin port and AllowNoPassword");
        loaded.MysqlRootPassword = "x"; loaded.MysqlPort = 3310;
        inst.UpdatePmaConfig();
        pma = File.ReadAllText(Path.Combine(K.Pma, "config.inc.php"));
        Check(pma.Contains("'3310'") && pma.Contains("AllowNoPassword'] = false"), "UpdatePmaConfig rewrites port and AllowNoPassword");

        // ---------------------------------------------------------------- vhosts
        Console.WriteLine("Virtual hosts");
        Directory.CreateDirectory(Path.Combine(K.Www, "blog", "public"));
        Directory.CreateDirectory(Path.Combine(K.Www, "Shop"));
        Directory.CreateDirectory(Path.Combine(K.Www, "my site"));      // invalid hostname: skipped
        Directory.CreateDirectory(Path.Combine(K.Www, "api"));          // same name as a Node app: skipped
        Directory.CreateDirectory(Path.Combine(K.Www, "legacy"));
        File.WriteAllText(Path.Combine(K.ApacheConf, "custom", "legacy.conf"), "# custom");
        Vhosts.Write(loaded);
        string sites = File.ReadAllText(Path.Combine(K.ApacheConf, "generated", "sites.conf"));
        Check(sites.Contains("ServerName blog.localhost") && sites.Contains("${WEBHUT_ROOT}/www/blog/public\""), "site with public/ uses it as DocumentRoot");
        Check(sites.Contains("ServerName shop.localhost") && sites.Contains("${WEBHUT_ROOT}/www/Shop\""), "uppercase folder -> lowercase host, real path kept");
        Check(!sites.Contains("my site"), "folder with a space is skipped");
        Check(!sites.Contains("ServerName api.localhost"), "folder named like a Node app is left to the Node vhost");
        Check(!sites.Contains("legacy.localhost"), "folder with custom/<name>.conf is not generated");
        string nodeConf = File.ReadAllText(Path.Combine(K.ApacheConf, "generated", "node-apps.conf"));
        Check(nodeConf.Contains("ProxyPass / http://localhost:3000/ upgrade=websocket") && nodeConf.Contains("ServerName web.localhost"), "Node reverse-proxy vhosts with websocket upgrade");

        // ---------------------------------------------------------------- apache args
        Services.Cfg = loaded;
        loaded.Php = "8.5"; loaded.HttpPort = 8080;
        string a = Services.Apache.Args();
        Check(a.Contains("-C \"Define WEBHUT_ROOT " + K.RootFwd + "\"") && a.Contains("Define PHP_DIR " + K.Fwd(phpDir)) && a.Contains("Define WEBHUT_HTTP_PORT 8080"),
              "Apache command line defines root, PHP dir and port", a);
        Check(!a.Contains("WEBHUT_LAN"), "network access is off by default");
        loaded.LanAccess = true;
        string lanArgs = Services.Apache.Args();
        Check(lanArgs.Contains("-C \"Define WEBHUT_LAN 1\"") && lanArgs.Contains("Define WEBHUT_LAN_IPS "), "network access passes WEBHUT_LAN and the PC's addresses", lanArgs);
        loaded.LanAccess = false;
        Check(nodeConf.Contains("ServerAlias api.test api.*.nip.io") && nodeConf.Contains("<IfDefine WEBHUT_LAN>") && nodeConf.Contains("Require local"),
              "vhosts carry nip.io aliases and both access rules");
        var env = Services.Env();
        Check(env["WEBHUT_ROOT"] == K.RootFwd && env["PATH"].StartsWith(phpDir), "child environment puts PHP first on PATH");

        // ---------------------------------------------------------------- zips
        Console.WriteLine("Unpacking");
        string zdir = Path.Combine(K.Tmp, "ztest");
        Directory.CreateDirectory(zdir);
        string flat = MakeZip(zdir, "flat.zip", new[] { "php.exe", "ext/php_curl.dll" });
        string nested = MakeZip(zdir, "nested.zip", new[] { "node-v24/node.exe", "node-v24/npm.cmd" });
        string apache = MakeZip(zdir, "apache.zip", new[] { "Apache24/bin/httpd.exe", "ReadMe.txt", "-- Win64 VS18 --" });

        string t1 = Path.Combine(K.Root, "ztarget", "php");
        inst.PlaceZip(flat, t1, null, null);
        Check(File.Exists(Path.Combine(t1, "php.exe")) && File.Exists(Path.Combine(t1, "ext", "php_curl.dll")), "flat zip unpacks in place");
        File.WriteAllText(Path.Combine(t1, "php.ini"), "mine");
        inst.PlaceZip(flat, t1, null, new[] { "php.ini" });
        Check(File.ReadAllText(Path.Combine(t1, "php.ini")) == "mine", "upgrade keeps php.ini");
        Check(Directory.GetDirectories(Path.Combine(K.Tmp, "backup")).Any(d => Path.GetFileName(d).StartsWith("php-")), "upgrade backs up the previous folder");
        string t2 = Path.Combine(K.Root, "ztarget", "node");
        inst.PlaceZip(nested, t2, null, null);
        Check(File.Exists(Path.Combine(t2, "node.exe")), "single wrapper folder is flattened");
        string t3 = Path.Combine(K.Root, "ztarget", "apache");
        inst.PlaceZip(apache, t3, "Apache24", null);
        Check(File.Exists(Path.Combine(t3, "bin", "httpd.exe")), "Apache24 folder picked out of the Apache Lounge zip");

        // ---------------------------------------------------------------- relocation
        loaded.LastRoot = "D:/old/webhut";
        File.WriteAllText(Path.Combine(phpDir, "php.ini"), ";  webhut-root: d:/OLD/webhut\nerror_log = \"D:/old/webhut/logs/php_errors.log\"");
        File.WriteAllText(Path.Combine(phpDir, "php.exe"), "");
        inst.RelocateIfMoved();
        string moved = File.ReadAllText(Path.Combine(phpDir, "php.ini"));
        Check(moved.Contains(K.RootFwd + "/logs/php_errors.log") && !moved.ToLowerInvariant().Contains("d:/old"), "moving the folder rewrites php.ini paths (case-insensitive)", moved);
        Check(WebHutConfig.Load().LastRoot == K.RootFwd, "new root remembered");
        // Bundle case: no lastRoot in webhut.json, only the php.ini marker
        loaded.LastRoot = "";
        File.WriteAllText(Path.Combine(phpDir, "php.ini"), ";  webhut-root: E:/bundle/webhut\nextension_dir = \"E:/bundle/webhut/php/8.5/ext\"");
        inst.RelocateIfMoved();
        Check(File.ReadAllText(Path.Combine(phpDir, "php.ini")).Contains(K.RootFwd + "/php/8.5/ext"), "relocation works from the php.ini marker alone");

        // ---------------------------------------------------------------- parsers
        Console.WriteLine("Download-page parsers");
        var ap = new Component { Homepage = "https://www.apachelounge.com/download/" };
        Resolver.ParseApache(@"<a href=""/download/VS17/binaries/httpd-2.4.62-240904-win64-VS17.zip"">x</a>
            <a href=""/download/VS18/binaries/httpd-2.4.68-260610-Win64-VS18.zip"">Apache 2.4.68</a>
            <a href=""/download/VS18/binaries/httpd-2.4.68-260610-Win32-VS18.zip"">32 bit</a>", ap);
        Check(ap.Url == "https://www.apachelounge.com/download/VS18/binaries/httpd-2.4.68-260610-Win64-VS18.zip" && ap.Version == "2.4.68 (VS18)",
              "Apache Lounge: newest Win64 build, relative link resolved", ap.Url);

        string releases = @"{
          ""8.4"": { ""version"": ""8.4.26"",
            ""nts-vs17-x64"": { ""zip"": { ""path"": ""php-8.4.26-nts-Win32-vs17-x64.zip"", ""sha256"": ""aa"" } },
            ""ts-vs17-x64"":  { ""zip"": { ""path"": ""php-8.4.26-Win32-vs17-x64.zip"", ""sha256"": """ + new string('b', 64) + @""" } } },
          ""8.5"": { ""version"": ""8.5.11"",
            ""ts-vs17-x86"":  { ""zip"": { ""path"": ""php-8.5.11-Win32-vs17-x86.zip"" } },
            ""ts-vs17-x64"":  { ""zip"": { ""path"": ""php-8.5.11-Win32-vs17-x64.zip"", ""sha256"": """ + new string('C', 64) + @""" } },
            ""nts-vs17-x64"": { ""zip"": { ""path"": ""php-8.5.11-nts-Win32-vs17-x64.zip"" } } } }";
        var p1 = new Component();
        Check(Resolver.ParsePhpReleases(releases, p1) && p1.Branch == "8.5" && p1.FileName == "php-8.5.11-Win32-vs17-x64.zip" && p1.Sha256 == new string('c', 64),
              "PHP releases.json: newest branch, x64 Thread Safe, sha256 lower-cased", p1.FileName);
        var p2 = new Component { Branch = "8.4" };
        Check(Resolver.ParsePhpReleases(releases, p2) && p2.Version == "8.4.26" && p2.Url.EndsWith("/php-8.4.26-Win32-vs17-x64.zip"), "PHP releases.json: specific branch");
        var p3 = new Component { Branch = "8.5" };
        Resolver.ParsePhpDownloadPage(@"php-8.5.11-nts-Win32-vs17-x64.zip php-8.5.9-Win32-vs17-x64.zip php-8.5.11-Win32-vs17-x64.zip php-8.5.11-Win32-vs17-x86.zip", p3);
        Check(p3.FileName == "php-8.5.11-Win32-vs17-x64.zip", "PHP download page fallback skips NTS and x86", p3.FileName);

        string majors = @"{""major_releases"":[
            {""release_id"":""13.0"",""release_status"":""RC"",""release_support_type"":""Long Term Support""},
            {""release_id"":""12.3"",""release_status"":""Stable"",""release_support_type"":""Long Term Support""},
            {""release_id"":""12.2"",""release_status"":""Stable"",""release_support_type"":""Short Term Support""},
            {""release_id"":""11.8"",""release_status"":""Stable"",""release_support_type"":""Long Term Support""}]}";
        Check(Resolver.ParseMariaSeries(majors) == "12.3", "MariaDB: newest stable LTS series");
        string files = @"{""releases"":{""12.3.3"":{""release_id"":""12.3.3"",""files"":[
            {""file_name"":""mariadb-12.3.3.tar.gz"",""file_download_url"":""http://x/a""},
            {""file_name"":""mariadb-12.3.3-winx64.msi"",""file_download_url"":""http://x/b""},
            {""file_name"":""mariadb-12.3.3-winx64.zip"",""file_download_url"":""http://downloads.mariadb.org/rest-api/mariadb/12.3.3/mariadb-12.3.3-winx64.zip"",
             ""checksum"":{""sha256sum"":""4b5706b47148b47c8f36963c8352d1ddee37a1287b4690428f631a74b09b85a0""}}]}}}";
        var m = new Component();
        Resolver.ParseMariaFiles(files, "12.3", m);
        Check(m.Version == "12.3.3" && m.Url.StartsWith("https://downloads.mariadb.org/") && m.Sha256.StartsWith("4b5706"), "MariaDB: winx64 zip, https, sha256");

        string index = @"[
            {""version"":""v26.1.0"",""lts"":false,""files"":[""win-x64-zip"",""linux-x64""]},
            {""version"":""v24.11.1"",""lts"":""Krypton"",""files"":[""win-x64-msi"",""win-x64-zip""]},
            {""version"":""v24.11.0"",""lts"":""Krypton"",""files"":[""win-x64-zip""]},
            {""version"":""v22.21.0"",""lts"":""Jod"",""files"":[""win-x64-zip""]}]";
        var n1 = new Component();
        Check(Resolver.ParseNodeIndex(index, null, n1) && n1.Version == "24.11.1" && n1.Branch == "24" && n1.Url == "https://nodejs.org/dist/v24.11.1/node-v24.11.1-win-x64.zip",
              "Node: newest LTS (skips Current)", n1.Url);
        var n2 = new Component();
        Check(Resolver.ParseNodeIndex(index, "22", n2) && n2.Version == "22.21.0", "Node: specific major");
        Check(Resolver.ShaFromText("abc\n" + new string('d', 64) + "  node-v24.11.1-win-x64.zip\n" + new string('e', 64) + "  other.zip", "node-v24.11.1-win-x64.zip") == new string('d', 64),
              "SHASUMS256.txt: picks the matching file's hash");
        Check(Resolver.ShaFromText(new string('F', 64) + "\n", null) == new string('f', 64), ".sha256 with a bare hash");

        // ---------------------------------------------------------------- process output capture
        var echo = Sys.Run("/bin/sh", "-c \"echo out-line; echo err-line 1>&2; exit 3\"", null, 10000);
        Check(echo.ExitCode == 3 && echo.Output.Contains("out-line") && echo.Output.Contains("err-line"), "Sys.Run captures stdout, stderr and exit code", echo.Output);

        // ---------------------------------------------------------------- backups
        Console.WriteLine("Backups");
        var dbsParsed = Backup.UserDatabases("information_schema\nmysql\nperformance_schema\nsys\nshop\nblog_db\r\n\n");
        Check(dbsParsed.SequenceEqual(new[] { "shop", "blog_db" }), "SHOW DATABASES parsing drops system schemas", string.Join(",", dbsParsed));
        Directory.CreateDirectory(K.Backups);
        for (int i = 0; i < 5; i++)
            File.WriteAllText(Path.Combine(K.Backups, "mariadb-2026010" + (i + 1) + "-120000.sql.gz"), "x");
        Check(Backup.List().Count == 5 && Backup.List()[0].Time == new DateTime(2026, 1, 5, 12, 0, 0), "backups listed newest first, time read from file name");
        Check(Backup.Prune(3) == 2 && Backup.List().Count == 3 && Backup.List().Last().Time.Day == 3, "pruning keeps the newest N");
        string plain = Path.Combine(K.Tmp, "gz-test.sql"), packed = plain + ".gz", back = plain + ".out";
        File.WriteAllText(plain, string.Join("\n", Enumerable.Range(0, 2000).Select(i => "INSERT INTO t VALUES (" + i + ");")));
        Backup.Gzip(plain, packed); Backup.Gunzip(packed, back);
        Check(File.ReadAllText(back) == File.ReadAllText(plain) && new FileInfo(packed).Length < new FileInfo(plain).Length / 3, "gzip round-trip and compression");
        var cfgB = WebHutConfig.Load();
        Check(cfgB.AutoBackup && cfgB.BackupEveryHours == 2 && cfgB.BackupKeep == 10, "backup settings default on (2 h, keep 10)");
        cfgB.AutoBackup = false; cfgB.BackupEveryHours = 6; cfgB.Save();
        var cfgB2 = WebHutConfig.Load();
        Check(!cfgB2.AutoBackup && cfgB2.BackupEveryHours == 6, "backup settings round-trip");
        cfgB2.AutoBackup = true; cfgB2.BackupEveryHours = 2; cfgB2.Save();
        string myIni = File.ReadAllText(K.MyIni);
        Check(myIni.Contains("innodb_flush_log_at_trx_commit = 1") && myIni.Contains("innodb_doublewrite       = 1"), "my.ini is crash-safe (durable commits, doublewrite)");

        // ---------------------------------------------------------------- XAMPP import (file side)
        Console.WriteLine("XAMPP import");
        string xampp = Path.Combine(K.Root, "fake-xampp");
        Check(!XamppImport.IsXampp(xampp), "non-XAMPP folder rejected");
        Directory.CreateDirectory(Path.Combine(xampp, "mysql", "data"));
        Directory.CreateDirectory(Path.Combine(xampp, "mysql", "bin"));
        File.WriteAllText(Path.Combine(xampp, "mysql", "bin", "mysqld.exe"), "");
        foreach (var d in new[] { "dashboard", "img", "webalizer", "xampp", "myshop", "blog", "legacy-app" })
            Directory.CreateDirectory(Path.Combine(xampp, "htdocs", d));
        File.WriteAllText(Path.Combine(xampp, "htdocs", "myshop", "index.php"), "<?php echo 1;");
        Directory.CreateDirectory(Path.Combine(xampp, "htdocs", "myshop", "assets", "css"));
        File.WriteAllText(Path.Combine(xampp, "htdocs", "myshop", "assets", "css", "a.css"), "body{}");
        Check(XamppImport.IsXampp(xampp), "XAMPP folder detected");
        var xi = new XamppImport(xampp);
        Check(xi.HtdocsProjects().SequenceEqual(new[] { "blog", "legacy-app", "myshop" }), "htdocs projects exclude XAMPP's demo folders", string.Join(",", xi.HtdocsProjects()));
        List<string> skippedP;
        var copiedP = xi.CopyProjects(new[] { "myshop", "blog" }, out skippedP);   // www/blog already exists from the vhost tests
        Check(copiedP.SequenceEqual(new[] { "myshop" }) && skippedP.SequenceEqual(new[] { "blog" }), "existing www folders are skipped, not overwritten");
        Check(File.ReadAllText(Path.Combine(K.Www, "myshop", "assets", "css", "a.css")) == "body{}", "project copied recursively");
        Check(File.Exists(Path.Combine(xampp, "htdocs", "myshop", "index.php")), "XAMPP originals untouched");


        if (port != null)
        {
            Console.WriteLine("Downloads");
            string body = File.ReadAllText(Path.Combine(K.Root, "served", "payload.zip.sha"));
            var good = new Component { Name = "payload", Version = "1", Url = "http://127.0.0.1:" + port + "/payload.zip", FileName = "payload.zip", Sha256 = body.Trim() };
            string got = inst.Download(good, CancellationToken.None);
            Check(File.Exists(got) && Sys.Sha256File(got) == good.Sha256, "download verified against SHA-256");
            var cached = inst.Download(good, CancellationToken.None);
            Check(cached == got, "matching cached download is reused");
            var bad = new Component { Name = "payload2", Version = "1", Url = good.Url, FileName = "payload2.zip", Sha256 = new string('0', 64) };
            bool threw = false;
            try { inst.Download(bad, CancellationToken.None); } catch (InvalidDataException) { threw = true; }
            Check(threw && !File.Exists(Path.Combine(K.Downloads, "payload2.zip")) && !File.Exists(Path.Combine(K.Downloads, "payload2.zip.part")),
                  "checksum mismatch is rejected and the file deleted");
            var c2 = new Component { Id = "php", Name = "PHP", Branch = "8.3", Version = "8.3.30", Url = good.Url, FileName = "payload.zip", Sha256 = good.Sha256 };
            inst.Install(c2, CancellationToken.None);
            Check(File.Exists(Path.Combine(K.PhpDir("8.3"), "php.exe")) && File.Exists(Path.Combine(K.PhpDir("8.3"), "php.ini")) &&
                  WebHutConfig.FullVersion(K.PhpDir("8.3")) == "8.3.30", "Install(php) end to end: unpack, php.ini, version file");
        }

        Console.WriteLine();
        Console.WriteLine(_pass + " passed, " + _fail + " failed");
        return _fail == 0 ? 0 : 1;
    }

    static string MakeZip(string dir, string name, string[] entries)
    {
        string path = Path.Combine(dir, name);
        if (File.Exists(path)) File.Delete(path);
        using (var z = ZipFile.Open(path, ZipArchiveMode.Create))
            foreach (var e in entries)
                using (var w = new StreamWriter(z.CreateEntry(e).Open())) w.Write("x");
        return path;
    }
}
