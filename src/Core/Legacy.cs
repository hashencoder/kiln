using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace ServNook
{
    /// <summary>
    /// ServNook was called WebHut up to 1.6.5. This moves a WebHut folder's own files to the new names, once,
    /// the first time ServNook (panel or CLI) runs there. Nothing is deleted: changed config files keep a
    /// *.webhut.bak copy, and sites, databases and settings stay where they are.
    /// </summary>
    public static class Legacy
    {
        static bool _done;

        static readonly Regex ApacheNames = new Regex(@"\bWEBHUT_(ROOT|HTTP_PORT|MYSQL_PORT|LAN_IPS|LAN|PHP_FCGID)\b");
        static readonly Regex DotfileMarkers = new Regex(@"^(\s*# (?:BEGIN|END)) WebHut(: protect dotfiles)", RegexOptions.Multiline);

        public static void Migrate()
        {
            if (_done) return;
            _done = true;
            RunAll();
        }

        /// <summary>The migration itself, without the once-per-process guard (tests call it directly).</summary>
        public static void RunAll()
        {
            try { MigrateLog(); } catch { }
            try { MigrateSettings(); } catch (Exception e) { Log.Warn("Couldn't move webhut.json to servnook.json: " + e.Message); }
            try { MigrateApache(); } catch (Exception e) { Log.Warn("Couldn't update the Apache configuration from WebHut to ServNook names: " + e.Message); }
            try { MigrateComments(); } catch (Exception e) { Log.Warn("Couldn't update WebHut comments in config files: " + e.Message); }
        }

        static void MigrateSettings()
        {
            foreach (var pair in new[] { new[] { "webhut.json", "servnook.json" } })
            {
                string oldF = Path.Combine(K.Root, pair[0]), newF = Path.Combine(K.Root, pair[1]);
                if (File.Exists(oldF) && !File.Exists(newF))
                {
                    File.Move(oldF, newF);
                    Log.Info("WebHut is now ServNook: settings moved from " + pair[0] + " to " + pair[1] + ".");
                }
            }
        }

        static void MigrateApache()
        {
            string dir = Path.Combine(K.Root, "config", "apache");
            if (!Directory.Exists(dir)) return;
            foreach (var f in Directory.GetFiles(dir, "*.conf", SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(f);
                string fixedText = MigrateApacheText(text);
                if (fixedText == text) continue;
                if (!File.Exists(f + ".webhut.bak")) File.Copy(f, f + ".webhut.bak", false);
                File.WriteAllText(f, fixedText, new UTF8Encoding(false));
                Log.Info("Updated " + f.Substring(K.Root.Length).TrimStart('\\', '/') + " to ServNook names (copy kept as .webhut.bak).");
            }
        }

        /// <summary>WEBHUT_ROOT and the other Apache defines become SERVNOOK_*, the dotfile block markers and comments say
        /// ServNook, and the example paths and log format name follow (C:/webhut, "webhut" LogFormat nickname).</summary>
        public static string MigrateApacheText(string text)
        {
            text = ApacheNames.Replace(text, "SERVNOOK_$1");
            text = DotfileMarkers.Replace(text, "$1 ServNook$2");
            text = LogNick.Replace(text, "$1servnook");                                 // LogFormat/CustomLog nickname
            return MigrateCommentText(text);
        }

        static readonly Regex LogNick = new Regex(@"^(\s*(?:LogFormat|CustomLog)\b.*\s)webhut(?=\s*$)", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        static readonly Regex ProductName = new Regex(@"\bWebHut(\.exe)?\b");
        // Comment lines (#, ;, //, * in a doc comment) and Apache Error "..." messages: text only, never a real path or setting.
        static readonly Regex TextLine = new Regex(@"^(\s*(?:#|;|//|/?\*)|\s*Error\s+"").*$", RegexOptions.Multiline);

        /// <summary>Comment and marker text written by WebHut into files ServNook doesn't regenerate. Only comment lines
        /// and Apache Error messages change, so a setting that really points at a C:\WebHut folder is left alone.</summary>
        public static string MigrateCommentText(string text)
        {
            text = TextLine.Replace(text, m =>
            {
                string l = ProductName.Replace(m.Value, x => x.Groups[1].Success ? "ServNook.exe" : "ServNook");
                l = Regex.Replace(l, @"(;\s*)webhut-root:", "${1}servnook-root:");   // php.ini relocation marker (both are read)
                return Regex.Replace(l, @"C:/webhut\b", "C:/servnook");
            });
            return text;
        }

        /// <summary>my.ini, every php.ini, phpMyAdmin's config.inc.php and the custom-vhost README were written from
        /// WebHut templates; only their comments mention WebHut, so they are updated in place without a backup copy
        /// (a backup inside apps\phpmyadmin would be served by Apache).</summary>
        static void MigrateComments()
        {
            var files = new List<string>
            {
                Path.Combine(K.Root, "config", "mariadb", "my.ini"),
                Path.Combine(K.Root, "apps", "phpmyadmin", "config.inc.php"),
                Path.Combine(K.Root, "config", "apache", "custom", "README.txt"),
            };
            string php = Path.Combine(K.Root, "php");
            if (Directory.Exists(php))
                foreach (var d in Directory.GetDirectories(php)) files.Add(Path.Combine(d, "php.ini"));
            foreach (var f in files)
            {
                if (!File.Exists(f)) continue;
                string text = File.ReadAllText(f);
                string fixedText = f.EndsWith("README.txt", StringComparison.OrdinalIgnoreCase) ? MigrateApacheText(text) : MigrateCommentText(text);
                if (fixedText == text) continue;
                File.WriteAllText(f, fixedText, new UTF8Encoding(false));
                Log.Info("Updated WebHut comments in " + f.Substring(K.Root.Length).TrimStart('\\', '/') + ".");
            }
        }

        static void MigrateLog()
        {
            string oldF = Path.Combine(K.Logs, "webhut.log"), newF = Path.Combine(K.Logs, "servnook.log");
            if (File.Exists(oldF) && !File.Exists(newF)) File.Move(oldF, newF);
        }
    }
}
