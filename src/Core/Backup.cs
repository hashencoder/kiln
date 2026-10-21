using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;

namespace WebHut
{
    /// <summary>
    /// Automatic and manual database backups (data\backups\mariadb-YYYYMMDD-HHmmss.sql.gz),
    /// restore, and "rebuild from backup" for a data folder damaged by a crash.
    /// Backups contain your databases only, never MariaDB's own user tables, so restoring
    /// can't change the root password.
    /// </summary>
    public static class Backup
    {
        static readonly object Gate = new object();
        static readonly string[] SystemDbs = { "information_schema", "performance_schema", "mysql", "sys" };
        public const string Prefix = "mariadb-";

        public class Info
        {
            public string Path;
            public DateTime Time;
            public long Size;
            public string Label { get { return Time.ToString("ddd d MMM yyyy, HH:mm") + "  (" + (Size / 1024.0 < 1024 ? (Size / 1024.0).ToString("0") + " KB" : (Size / 1048576.0).ToString("0.0") + " MB") + ")"; } }
        }

        public static List<Info> List()
        {
            if (!Directory.Exists(K.Backups)) return new List<Info>();
            return Directory.GetFiles(K.Backups, Prefix + "*.sql.gz")
                .Select(f => new Info { Path = f, Time = TimeFromName(f) ?? File.GetLastWriteTime(f), Size = new FileInfo(f).Length })
                .OrderByDescending(i => i.Time).ToList();
        }

        public static DateTime? TimeFromName(string path)
        {
            var m = Regex.Match(System.IO.Path.GetFileName(path), @"(\d{8})-(\d{6})");
            DateTime t;
            if (m.Success && DateTime.TryParseExact(m.Groups[1].Value + m.Groups[2].Value, "yyyyMMddHHmmss",
                System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out t)) return t;
            return null;
        }

        public static DateTime? LastBackupTime()
        {
            var l = List();
            return l.Count == 0 ? (DateTime?)null : l[0].Time;
        }

        /// <summary>Database names from "SHOW DATABASES" output, without MariaDB's own schemas.</summary>
        public static List<string> UserDatabases(string showDatabasesOutput)
        {
            return (showDatabasesOutput ?? "").Split('\n')
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.StartsWith("Database") && !l.StartsWith("ERROR") && !l.Contains(" "))
                .Where(l => !SystemDbs.Contains(l, StringComparer.OrdinalIgnoreCase))
                .ToList();
        }

        /// <summary>Delete the oldest backups so at most `keep` remain.</summary>
        public static int Prune(int keep)
        {
            int n = 0;
            foreach (var old in List().Skip(Math.Max(1, keep)))
            {
                try { File.Delete(old.Path); n++; } catch { }
            }
            return n;
        }

        static Sys.RunResult Client(WebHutConfig cfg, string exeNames, string args, int timeoutMs)
        {
            var names = exeNames.Split('|');
            var exe = Installer.MariaExe(names);
            if (exe == null) throw new FileNotFoundException(names[0] + " not found in " + K.MariaDbBin);
            var env = Services.Env();
            env["MYSQL_PWD"] = cfg.MysqlRootPassword ?? "";
            return Sys.Run(exe, "--protocol=TCP -h 127.0.0.1 -P " + cfg.MysqlPort + " -u root " + args, env, timeoutMs, K.MariaDbBin);
        }

        public static List<string> Databases(WebHutConfig cfg)
        {
            var r = Client(cfg, "mariadb.exe|mysql.exe", "-N -e \"SHOW DATABASES\"", 30000);
            if (r.ExitCode != 0) throw new Exception("Couldn't list databases:\n" + r.Output.Trim());
            return UserDatabases(r.Output);
        }

        /// <summary>Dump every user database to a new .sql.gz. Returns its path, or null if there was nothing to back up.</summary>
        public static string Create(WebHutConfig cfg, string reason)
        {
            lock (Gate)
            {
                if (!Services.MariaDb.IsRunning) throw new Exception("MariaDB isn't running.");
                var dbs = Databases(cfg);
                if (dbs.Count == 0) { Log.Info("Backup skipped: no databases yet."); return null; }

                Directory.CreateDirectory(K.Backups);
                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                string sql = System.IO.Path.Combine(K.Backups, Prefix + stamp + ".sql");
                string gz = sql + ".gz";
                Log.Info("Backing up " + dbs.Count + " database(s)" + (reason != null ? " (" + reason + ")" : "") + "...");
                var r = Client(cfg, "mariadb-dump.exe|mysqldump.exe",
                    "--single-transaction --routines --events --triggers --hex-blob --default-character-set=utf8mb4 " +
                    "--result-file=\"" + sql + "\" --databases " + string.Join(" ", dbs.Select(d => "\"" + d + "\"")),
                    60 * 60 * 1000);
                if (r.ExitCode != 0 || !File.Exists(sql))
                {
                    try { File.Delete(sql); } catch { }
                    throw new Exception("Backup failed:\n" + r.Output.Trim());
                }
                Gzip(sql, gz);
                File.Delete(sql);
                int pruned = Prune(cfg.BackupKeep);
                Log.Info("Backup saved: " + System.IO.Path.GetFileName(gz) + (pruned > 0 ? " (removed " + pruned + " old)" : ""));
                return gz;
            }
        }

        /// <summary>Run a backup if automatic backups are on and the last one is older than `minAge`.</summary>
        public static void CreateIfDue(WebHutConfig cfg, TimeSpan minAge, string reason)
        {
            if (!cfg.AutoBackup) return;
            var last = LastBackupTime();
            if (last.HasValue && DateTime.Now - last.Value < minAge) return;
            try { Create(cfg, reason); }
            catch (Exception e) { Log.Warn("Automatic backup failed: " + e.Message); }
        }

        /// <summary>Load a backup into the running server. Tables in the backup replace current ones of the same name.</summary>
        public static void Restore(WebHutConfig cfg, string gzFile)
        {
            lock (Gate)
            {
                if (!Services.MariaDb.IsRunning) throw new Exception("MariaDB isn't running.");
                string sql = System.IO.Path.Combine(K.Tmp, "restore-" + Guid.NewGuid().ToString("N") + ".sql");
                Directory.CreateDirectory(K.Tmp);
                try
                {
                    Gunzip(gzFile, sql);
                    Log.Info("Restoring " + System.IO.Path.GetFileName(gzFile) + "...");
                    var r = Client(cfg, "mariadb.exe|mysql.exe", "--default-character-set=utf8mb4 -e \"source " + K.Fwd(sql) + "\"", 60 * 60 * 1000);
                    if (r.ExitCode != 0) throw new Exception("Restore failed:\n" + r.Output.Trim());
                    Log.Info("Restore complete.");
                }
                finally { try { File.Delete(sql); } catch { } }
            }
        }

        /// <summary>
        /// For a data folder MariaDB can no longer start from: keep it aside as data\mariadb-damaged-*,
        /// create a fresh one, start, and load the newest backup. Nothing is deleted.
        /// </summary>
        public static string Rebuild(WebHutConfig cfg, string gzFile)
        {
            if (gzFile == null)
            {
                var latest = List().FirstOrDefault();
                if (latest == null) throw new Exception("There are no backups to rebuild from.");
                gzFile = latest.Path;
            }
            if (Services.MariaDb.IsRunning) Services.MariaDb.Stop(false);
            string aside = null;
            if (Directory.Exists(K.MariaDbData))
            {
                aside = K.MariaDbData + "-damaged-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                Directory.Move(K.MariaDbData, aside);
                Log.Info("Damaged data folder kept as " + aside);
            }
            new Installer(cfg).InitMariaDb();
            Services.MariaDb.Start();
            Restore(cfg, gzFile);
            Log.Info("Database rebuilt from " + System.IO.Path.GetFileName(gzFile) + ".");
            return aside;
        }

        public static void Gzip(string src, string dest)
        {
            using (var input = File.OpenRead(src))
            using (var output = File.Create(dest))
            using (var gz = new GZipStream(output, CompressionLevel.Optimal))
                input.CopyTo(gz);
        }

        public static void Gunzip(string src, string dest)
        {
            using (var input = File.OpenRead(src))
            using (var gz = new GZipStream(input, CompressionMode.Decompress))
            using (var output = File.Create(dest))
                gz.CopyTo(output);
        }
    }
}
