using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace ServNook
{
    /// <summary>Downloads, verifies, unpacks and configures components.</summary>
    public class Installer
    {
        public ServNookConfig Cfg;
        /// <summary>(message, percent 0-100 or -1 when unknown)</summary>
        public event Action<string, int> Progress;

        public Installer(ServNookConfig cfg) { Cfg = cfg; }

        void Report(string msg, int pct) { var h = Progress; if (h != null) h(msg, pct); }

        // ------------------------------------------------------------------ setup

        public static bool IsInstalled()
        {
            return File.Exists(K.Httpd) && ServNookConfig.InstalledPhp().Count > 0;
        }

        public void CreateFolders()
        {
            foreach (var d in new[] { K.Www, K.Logs, K.Tmp, K.Pids, K.Downloads, K.Ssl, K.Tools, K.Data,
                                      Path.Combine(K.Tmp, "sessions"), Path.Combine(K.Tmp, "uploads"),
                                      Path.Combine(K.ApacheConf, "generated"), Path.Combine(K.ApacheConf, "custom"),
                                      Path.Combine(K.Config, "mariadb") })
                Directory.CreateDirectory(d);
        }

        /// <summary>Install one resolved component end to end.</summary>
        public void Install(Component c, CancellationToken ct)
        {
            if (!c.Ready) throw new InvalidOperationException(c.Name + ": " + (c.Error ?? "not resolved"));
            string file = c.LocalFile ?? Download(c, ct);
            ct.ThrowIfCancellationRequested();

            switch (c.Id)
            {
                case "apache":
                    PlaceZip(file, K.Apache, "Apache24", null);
                    CarryOver(LastBackupDir, K.Apache, "modules", "*.so");   // e.g. mod_fcgid.so added by hand
                    WriteVersion(K.Apache, c.Version);
                    break;
                case "php":
                    {
                        string branch = c.Branch ?? GuessBranch(c.Version, file);
                        string dir = K.PhpDir(branch);
                        PlaceZip(file, dir, null, new[] { "php.ini" });
                        CarryOver(LastBackupDir, dir, "ext", "*.dll");       // e.g. php_xdebug.dll added by hand
                        WriteVersion(dir, c.Version);
                        if (!File.Exists(Path.Combine(dir, "php.ini"))) WritePhpIni(dir);
                        if (string.IsNullOrEmpty(Cfg.Php)) Cfg.Php = branch;
                        break;
                    }
                case "mariadb":
                    PlaceZip(file, K.MariaDb, null, null);
                    WriteVersion(K.MariaDb, c.Version);
                    break;
                case "phpmyadmin":
                    PlaceZip(file, K.Pma, null, new[] { "config.inc.php" });
                    WriteVersion(K.Pma, c.Version);
                    Directory.CreateDirectory(Path.Combine(K.Pma, "tmp"));
                    if (!File.Exists(Path.Combine(K.Pma, "config.inc.php"))) WritePmaConfig();
                    break;
                case "node":
                    {
                        string major = c.Branch ?? (c.Version ?? "0").Split('.')[0];
                        string dir = K.NodeDir(major);
                        PlaceZip(file, dir, null, null);
                        CarryFolder(LastBackupDir, dir, "global");           // globally installed npm packages
                        WriteVersion(dir, c.Version);
                        Directory.CreateDirectory(Path.Combine(dir, "global"));
                        if (string.IsNullOrEmpty(Cfg.Node)) Cfg.Node = major;
                        break;
                    }
                case "composer":
                    Directory.CreateDirectory(K.Tools);
                    File.Copy(file, Path.Combine(K.Tools, "composer.phar"), true);
                    File.WriteAllText(Path.Combine(K.Tools, "composer.bat"),
                        "@echo off\r\nphp \"%~dp0composer.phar\" %*\r\n");
                    break;
                case "cacert":
                    Directory.CreateDirectory(K.Ssl);
                    File.Copy(file, Path.Combine(K.Ssl, "cacert.pem"), true);
                    break;
                default:
                    Engines.Install(this, c, file);   // MySQL, PostgreSQL, MongoDB, mongosh, Adminer
                    break;
            }
            Log.Info("Installed " + c);
        }

        static string GuessBranch(string version, string file)
        {
            var src = version ?? Path.GetFileName(file);
            var m = System.Text.RegularExpressions.Regex.Match(src ?? "", @"(\d+\.\d+)");
            return m.Success ? m.Groups[1].Value : "php";
        }

        static void WriteVersion(string dir, string version)
        {
            try { File.WriteAllText(Path.Combine(dir, ".servnook-version"), version ?? ""); } catch { }
        }

        // ------------------------------------------------------------------ download

        public string Download(Component c, CancellationToken ct)
        {
            Directory.CreateDirectory(K.Downloads);
            string target = Path.Combine(K.Downloads, c.FileName ?? (c.Id + ".zip"));

            if (File.Exists(target) && c.Sha256 != null && Sys.Sha256File(target) == c.Sha256)
            {
                Report(c.Name + ": using cached download", 100);
                return target;
            }

            Sys.EnableModernTls();
            string part = target + ".part";
            Report("Downloading " + c.Name + " " + c.Version + "...", 0);
            Log.Info("Downloading " + c.Url);

            var req = (HttpWebRequest)WebRequest.Create(c.Url);
            req.UserAgent = "ServNook/" + K.AppVersion + " (portable dev stack for Windows)";
            req.Timeout = 60000;
            req.ReadWriteTimeout = 60000;
            req.AllowAutoRedirect = true;
            try
            {
                DownloadTo(req, part, c, ct);
            }
            catch (WebException)
            {
                if (!ct.IsCancellationRequested) throw;
                try { File.Delete(part); } catch { }
                throw new OperationCanceledException(ct);
            }
            FinishDownload(c, part, target);
            return target;
        }

        void DownloadTo(HttpWebRequest req, string part, Component c, CancellationToken ct)
        {
            using (ct.Register(() => { try { req.Abort(); } catch { } }))
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var input = resp.GetResponseStream())
            using (var output = File.Create(part))
            {
                long total = resp.ContentLength, done = 0;
                var buf = new byte[1 << 16];
                int n, lastPct = -1;
                var sw = Stopwatch.StartNew();
                while ((n = input.Read(buf, 0, buf.Length)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    output.Write(buf, 0, n);
                    done += n;
                    int pct = total > 0 ? (int)(done * 100 / total) : -1;
                    if (pct != lastPct && sw.ElapsedMilliseconds > 150)
                    {
                        lastPct = pct;
                        sw.Restart();
                        Report(string.Format("Downloading {0} {1}... {2:0.0} MB{3}", c.Name, c.Version, done / 1048576.0,
                            total > 0 ? string.Format(" of {0:0.0} MB", total / 1048576.0) : ""), pct);
                    }
                }
            }

        }

        void FinishDownload(Component c, string part, string target)
        {
            if (c.Sha256 != null)
            {
                Report("Verifying " + c.Name + " checksum...", -1);
                string actual = Sys.Sha256File(part);
                if (actual != c.Sha256)
                {
                    File.Delete(part);
                    throw new InvalidDataException(c.Name + " download is corrupted or was tampered with (SHA-256 mismatch). Expected " + c.Sha256 + ", got " + actual + ".");
                }
                Log.Info(c.Name + " SHA-256 verified");
            }
            else Log.Warn(c.Name + ": the publisher doesn't provide a machine-readable checksum, so it wasn't verified.");

            if (File.Exists(target)) File.Delete(target);
            File.Move(part, target);
        }

        // ------------------------------------------------------------------ unpack

        /// <summary>
        /// Unzip into target. If the zip wraps everything in one folder (or a folder named innerName),
        /// that folder's contents become target. Files in keep[] are carried over from an existing install.
        /// An existing target is moved to tmp\backup first.
        /// </summary>
        /// <summary>Where PlaceZip moved the previous install (null when there was none). Used to roll back an update.</summary>
        public string LastBackupDir;

        /// <summary>
        /// After an update, bring over what the user added to the old install and the new package lacks:
        /// extra files matching <paramref name="pattern"/> in <paramref name="sub"/> (e.g. mod_fcgid.so, php_xdebug.dll).
        /// Never replaces a file the new package ships.
        /// </summary>
        public static int CarryOver(string oldDir, string newDir, string sub, string pattern)
        {
            if (oldDir == null) return 0;
            string from = Path.Combine(oldDir, sub), to = Path.Combine(newDir, sub);
            if (!Directory.Exists(from)) return 0;
            int n = 0;
            Directory.CreateDirectory(to);
            foreach (var f in Directory.GetFiles(from, pattern))
            {
                var dest = Path.Combine(to, Path.GetFileName(f));
                if (File.Exists(dest)) continue;
                File.Copy(f, dest);
                Log.Info("Kept " + Path.Combine(sub, Path.GetFileName(f)) + " from the previous version.");
                n++;
            }
            return n;
        }

        /// <summary>Move a whole folder of user data (e.g. Node's global npm packages) from the old install into the new one.</summary>
        public static void CarryFolder(string oldDir, string newDir, string sub)
        {
            if (oldDir == null) return;
            string from = Path.Combine(oldDir, sub), to = Path.Combine(newDir, sub);
            if (!Directory.Exists(from)) return;
            if (Directory.Exists(to))
            {
                if (Directory.EnumerateFileSystemEntries(to).Any()) return;
                Directory.Delete(to);
            }
            Directory.Move(from, to);
            Log.Info("Kept " + sub + " from the previous version.");
        }

        public void PlaceZip(string zip, string target, string innerName, string[] keep)
        {
            LastBackupDir = null;
            string work = Path.Combine(K.Tmp, "x");
            if (Directory.Exists(work)) Directory.Delete(work, true);
            Directory.CreateDirectory(work);

            Report("Unpacking " + Path.GetFileName(zip) + "...", -1);
            try { ZipFile.ExtractToDirectory(zip, work); }
            catch (InvalidDataException) { throw new InvalidDataException(Path.GetFileName(zip) + " is not a valid zip file."); }

            string content = work;
            if (innerName != null && Directory.Exists(Path.Combine(work, innerName)))
                content = Path.Combine(work, innerName);
            else
            {
                var dirs = Directory.GetDirectories(work);
                var files = Directory.GetFiles(work);
                if (dirs.Length == 1 && files.Length == 0) content = dirs[0];
            }

            var kept = new Dictionary<string, byte[]>();
            if (Directory.Exists(target))
            {
                if (keep != null)
                    foreach (var k in keep)
                    {
                        var p = Path.Combine(target, k);
                        if (File.Exists(p)) kept[k] = File.ReadAllBytes(p);
                    }
                string backup = Path.Combine(K.Tmp, "backup", Path.GetFileName(target) + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                Directory.CreateDirectory(Path.GetDirectoryName(backup));
                Directory.Move(target, backup);
                LastBackupDir = backup;
                Log.Info("Previous " + Path.GetFileName(target) + " moved to " + backup);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            Directory.Move(content, target);
            foreach (var kv in kept) File.WriteAllBytes(Path.Combine(target, kv.Key), kv.Value);
            try { if (Directory.Exists(work)) Directory.Delete(work, true); } catch { }
        }

        // ------------------------------------------------------------------ config files

        /// <summary>A ServNook template file as shipped (e.g. "config/apache/httpd.conf").</summary>
        public static string Template(string name) { return Resource(name).Replace("\r\n", "\n").Replace("\n", "\r\n"); }

        static string Resource(string name)
        {
            var asm = Assembly.GetExecutingAssembly();
            using (var s = asm.GetManifestResourceStream("overlay/" + name))
            {
                if (s == null) throw new FileNotFoundException("Embedded file missing: overlay/" + name);
                using (var sr = new StreamReader(s, Encoding.UTF8)) return sr.ReadToEnd();
            }
        }

        static void WriteText(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text.Replace("\r\n", "\n").Replace("\n", "\r\n"), new UTF8Encoding(false));
        }

        /// <summary>Write ServNook's config files. Existing files are kept unless overwrite is true.</summary>
        public void WriteConfigFiles(bool overwrite)
        {
            var files = new[]
            {
                "config/apache/httpd.conf", "config/apache/php-mod.conf", "config/apache/php-fcgid.conf",
                "config/apache/default-vhost.conf", "config/apache/custom/README.txt", "config/mariadb/my.ini"
            };
            foreach (var f in files)
            {
                var dest = Path.Combine(K.Root, f.Replace('/', Path.DirectorySeparatorChar));
                if (overwrite || !File.Exists(dest)) WriteText(dest, Resource(f));
            }
            var dashboard = Path.Combine(K.Www, "index.php");
            if (!File.Exists(dashboard) && !File.Exists(Path.Combine(K.Www, "index.html")))
                WriteText(dashboard, Resource("www/index.php"));
            else UpgradeDashboard(dashboard);
        }

        /// <summary>SHA-256 of earlier ServNook dashboards exactly as shipped (line endings normalised).</summary>
        static readonly string[] OldDashboards =
        {
            "56abdca7b9ac08fafd976c1398eeab2be71d94f1600fb273c6124510624bb407",
            "6457cb4247b3e250a57d0220c3fb2bf15cca223a911a7fe9a61065bcc5f36c89",
            "7de87c19cbdf130470714b003235627d6ec897b6e47ff6a7a8ef08abca277dab",
            "da58a6df67af604e7b4a827a852e7dde9f6abda0b047b3091f461b6473dd6876",   // 1.6.3-1.6.4 (old flame logo)
            "8d492b0ecf13a4a0695c872713bd340891d14ccbf209dfc89ac92395837344e1",   // 1.6.5 (WebHut name)
        };

        /// <summary>Replace an unmodified older ServNook dashboard (www\index.php) with the current one. Edited files are left alone.</summary>
        public static bool UpgradeDashboard(string file)
        {
            try
            {
                if (!File.Exists(file)) return false;
                string text = File.ReadAllText(file).Replace("\r\n", "\n").Trim();
                string hash;
                using (var sha = System.Security.Cryptography.SHA256.Create())
                    hash = BitConverter.ToString(sha.ComputeHash(new UTF8Encoding(false).GetBytes(text))).Replace("-", "").ToLowerInvariant();
                if (!OldDashboards.Contains(hash)) return false;
                WriteText(file, Resource("www/index.php"));
                Log.Info("Updated the ServNook dashboard (www\\index.php) to the new design.");
                return true;
            }
            catch (Exception e) { Log.Warn("Couldn't update www\\index.php: " + e.Message); return false; }
        }

        public void WritePhpIni(string phpDir)
        {
            string ext = Path.Combine(phpDir, "ext");
            var wanted = new[] { "bz2", "curl", "exif", "fileinfo", "gd", "gettext", "intl", "mbstring", "mysqli",
                                 "openssl", "pdo_mysql", "pdo_sqlite", "soap", "sockets", "sodium", "sqlite3", "xsl", "zip" };
            var lines = new List<string>();
            // PostgreSQL drivers when PostgreSQL or Adminer is in use (they need libpq.dll from the PHP folder).
            if (Engines.PostgresInstalled || Engines.AdminerInstalled) wanted = wanted.Concat(new[] { "pgsql", "pdo_pgsql" }).ToArray();
            foreach (var e in wanted)
                if (File.Exists(Path.Combine(ext, "php_" + e + ".dll"))) lines.Add("extension = " + e);
            // OPcache is built in from PHP 8.5; older versions ship it as a DLL.
            if (File.Exists(Path.Combine(ext, "php_opcache.dll"))) lines.Add("zend_extension = opcache");

            string ini = Resource("php/php.ini.template")
                .Replace("{{SERVNOOK_ROOT}}", K.RootFwd)
                .Replace("{{PHP_DIR}}", K.Fwd(phpDir))
                .Replace("{{TIMEZONE}}", Sys.IanaTimeZone())
                .Replace("{{EXTENSIONS}}", string.Join("\n", lines));
            WriteText(Path.Combine(phpDir, "php.ini"), ini);
            Log.Info("Wrote " + Path.Combine(phpDir, "php.ini"));
        }

        public void WritePmaConfig()
        {
            string conf = Resource("apps/phpmyadmin/config.inc.php")
                .Replace("{{BLOWFISH_SECRET}}", Sys.RandomToken(32))
                .Replace("{{MYSQL_PORT}}", Cfg.MysqlPort.ToString())
                .Replace("{{ALLOW_NO_PASSWORD}}", string.IsNullOrEmpty(Cfg.MysqlRootPassword) ? "true" : "false");
            WriteText(Path.Combine(K.Pma, "config.inc.php"), ApplyPmaLogin(conf, Cfg));
        }

        const string PmaLoginBegin = "// BEGIN ServNook sign-in", PmaLoginEnd = "// END ServNook sign-in";

        static string PhpString(string s) { return "'" + (s ?? "").Replace("\\", "\\\\").Replace("'", "\\'") + "'"; }

        /// <summary>The block ServNook keeps in phpMyAdmin's config.inc.php: sign in as root automatically
        /// (auth_type config, with the root password saved in ServNook), or show the normal login page.</summary>
        public static string PmaLoginBlock(ServNookConfig cfg)
        {
            var b = new StringBuilder();
            b.AppendLine(PmaLoginBegin + " (written by ServNook; change it in Settings > Services)");
            if (cfg.PmaAutoLogin)
            {
                b.AppendLine("// phpMyAdmin is only reachable from this PC, so it signs in as root without asking.");
                b.AppendLine("$cfg['Servers'][$i]['auth_type'] = 'config';");
                b.AppendLine("$cfg['Servers'][$i]['user']      = 'root';");
                b.AppendLine("$cfg['Servers'][$i]['password']  = " + PhpString(cfg.MysqlRootPassword) + ";");
            }
            else
                b.AppendLine("$cfg['Servers'][$i]['auth_type'] = 'cookie';");
            b.Append(PmaLoginEnd);
            return b.ToString();
        }

        /// <summary>Put the current sign-in block into a config.inc.php text (replacing an older block, or adding one after the server settings).</summary>
        public static string ApplyPmaLogin(string text, ServNookConfig cfg)
        {
            string nl = text.Contains("\r\n") ? "\r\n" : "\n";
            string block = PmaLoginBlock(cfg).Replace("\r\n", "\n").Replace("\n", nl);
            int b = text.IndexOf(PmaLoginBegin, StringComparison.Ordinal);
            int e = b < 0 ? -1 : text.IndexOf(PmaLoginEnd, b, StringComparison.Ordinal);
            if (b >= 0 && e > b) return text.Substring(0, b) + block + text.Substring(e + PmaLoginEnd.Length);
            var m = System.Text.RegularExpressions.Regex.Match(text, @"^.*\['AllowNoPassword'\].*$", System.Text.RegularExpressions.RegexOptions.Multiline);
            if (!m.Success) return text.TrimEnd() + nl + nl + block + nl;
            int at = m.Index + m.Length;
            if (at > 0 && text[at - 1] == '\r') at--;               // insert before the line break, keeping CRLF intact
            return text.Substring(0, at) + nl + block + text.Substring(at);
        }

        /// <summary>Update port, AllowNoPassword and the sign-in block in an existing phpMyAdmin config.</summary>
        public void UpdatePmaConfig()
        {
            var f = Path.Combine(K.Pma, "config.inc.php");
            if (!File.Exists(f)) return;
            var s = File.ReadAllText(f);
            s = System.Text.RegularExpressions.Regex.Replace(s, @"(\['port'\]\s*=\s*')\d*(')", "${1}" + Cfg.MysqlPort + "$2");
            s = System.Text.RegularExpressions.Regex.Replace(s, @"(\['AllowNoPassword'\]\s*=\s*)(true|false)",
                "${1}" + (string.IsNullOrEmpty(Cfg.MysqlRootPassword) ? "true" : "false"));
            s = ApplyPmaLogin(s, Cfg);
            if (s != File.ReadAllText(f)) File.WriteAllText(f, s);
        }

        // ------------------------------------------------------------------ MariaDB

        public static string MariaExe(params string[] names)
        {
            foreach (var n in names)
            {
                var p = Path.Combine(K.MariaDbBin, n);
                if (File.Exists(p)) return p;
            }
            return null;
        }

        public void InitMariaDb()
        {
            if (Directory.Exists(K.MariaDbData) && Directory.EnumerateFileSystemEntries(K.MariaDbData).Any())
            {
                Log.Info("MariaDB data folder already exists - keeping your databases.");
                return;
            }
            string exe = MariaExe("mariadb-install-db.exe", "mysql_install_db.exe");
            if (exe == null) throw new FileNotFoundException("mariadb-install-db.exe not found in " + K.MariaDbBin);
            if (Directory.Exists(K.MariaDbData)) Directory.Delete(K.MariaDbData);
            Directory.CreateDirectory(K.Data);

            Report("Creating MariaDB system tables...", -1);
            string args = "--datadir=\"" + K.MariaDbData + "\" --port=" + Cfg.MysqlPort;
            if (!string.IsNullOrEmpty(Cfg.MysqlRootPassword)) args += " --password=\"" + Cfg.MysqlRootPassword.Replace("\"", "\\\"") + "\"";
            var r = Sys.Run(exe, args, null, 180000, K.MariaDbBin);
            if (r.ExitCode != 0 || !Directory.Exists(K.MariaDbData))
                throw new Exception("MariaDB initialisation failed:\n" + r.Output.Trim());
            Log.Info("MariaDB data folder created at " + K.MariaDbData);
        }

        // ------------------------------------------------------------------ relocation

        /// <summary>If the ServNook folder was moved or copied, fix the absolute root path stored in php.ini files.</summary>
        public void RelocateIfMoved()
        {
            string now = K.RootFwd;
            foreach (var v in ServNookConfig.InstalledPhp())
            {
                var ini = Path.Combine(K.PhpDir(v), "php.ini");
                if (!File.Exists(ini)) continue;
                var text = File.ReadAllText(ini);
                // Prefer the root recorded in servnook.json; fall back to the marker line in php.ini
                // (covers bundles shipped without servnook.json).
                string old = Cfg.LastRoot;
                var marker = System.Text.RegularExpressions.Regex.Match(text, @";\s*(?:servnook|webhut)-root:\s*(\S+)");
                if (marker.Success) old = marker.Groups[1].Value.Trim();
                if (string.IsNullOrEmpty(old) || old.Equals(now, StringComparison.OrdinalIgnoreCase)) continue;
                Log.Info("ServNook folder moved from " + old + " to " + now + " - updating " + ini);
                var fixedText = System.Text.RegularExpressions.Regex.Replace(text,
                    System.Text.RegularExpressions.Regex.Escape(old), now.Replace("$", "$$"),
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (fixedText != text) File.WriteAllText(ini, fixedText);
            }
            if (!now.Equals(Cfg.LastRoot, StringComparison.OrdinalIgnoreCase))
            {
                Cfg.LastRoot = now;
                Cfg.Save();
            }
            // Bundles ship without phpMyAdmin's config so every install gets its own secret.
            if (File.Exists(Path.Combine(K.Pma, "index.php")) && !File.Exists(Path.Combine(K.Pma, "config.inc.php")))
            {
                Directory.CreateDirectory(Path.Combine(K.Pma, "tmp"));
                WritePmaConfig();
                Log.Info("Created phpMyAdmin config with a new secret.");
            }
        }

        // ------------------------------------------------------------------ VC++ runtime

        /// <summary>Installed VC++ 2015-2026 x64 runtime version, or null.</summary>
        public static Version VcRedistVersion()
        {
            Version best = null;
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using (var hk = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                    using (var key = hk.OpenSubKey(@"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64"))
                    {
                        if (key == null || Convert.ToInt32(key.GetValue("Installed", 0)) != 1) continue;
                        var v = new Version(Convert.ToInt32(key.GetValue("Major", 14)), Convert.ToInt32(key.GetValue("Minor", 0)),
                                            Convert.ToInt32(key.GetValue("Bld", 0)));
                        if (best == null || v > best) best = v;
                    }
                }
                catch { }
            }
            return best;
        }

        /// <summary>Apache Lounge VS18 builds need the 2017-2026 runtime (14.50 or newer).</summary>
        public static bool VcRedistOk()
        {
            var v = VcRedistVersion();
            return v != null && v >= new Version(14, 50);
        }

        public void InstallVcRedist(CancellationToken ct)
        {
            var c = new Component
            {
                Id = "vcredist", Name = "Visual C++ Redistributable", Version = "x64", FileName = "vc_redist.x64.exe",
                Url = "https://aka.ms/vc14/vc_redist.x64.exe"
            };
            string exe;
            try { exe = Download(c, ct); }
            catch (Exception e)
            {
                Log.Warn("Primary VC++ download link failed (" + e.Message + "), trying the VS 2022 link.");
                c.Url = "https://aka.ms/vs/17/release/vc_redist.x64.exe";
                exe = Download(c, ct);
            }
            Report("Installing Visual C++ Redistributable (Windows will ask for permission)...", -1);
            var p = Process.Start(new ProcessStartInfo(exe, "/install /passive /norestart") { UseShellExecute = true, Verb = "runas" });
            p.WaitForExit();
            // 0 ok, 3010 ok (reboot later), 1638 a newer version is already installed
            if (p.ExitCode == 1618)
                throw new Exception("Another installation is already running (Windows Update or another installer). Wait for it to finish and try again.");
            if (p.ExitCode == 1602 || p.ExitCode == 1223)
                throw new OperationCanceledException("The Visual C++ installation was cancelled.");
            if (p.ExitCode != 0 && p.ExitCode != 3010 && p.ExitCode != 1638)
                throw new Exception("Visual C++ Redistributable installer exited with code " + p.ExitCode + ".");
            Log.Info("Visual C++ Redistributable installed (exit code " + p.ExitCode + ").");
        }
    }
}
