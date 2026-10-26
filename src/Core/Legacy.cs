using System;
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
                File.Copy(f, f + ".webhut.bak", true);
                File.WriteAllText(f, fixedText, new UTF8Encoding(false));
                Log.Info("Updated " + f.Substring(K.Root.Length).TrimStart('\\', '/') + " to ServNook names (copy kept as .webhut.bak).");
            }
        }

        /// <summary>WEBHUT_ROOT and the other Apache defines become SERVNOOK_*, and the dotfile block markers say ServNook.</summary>
        public static string MigrateApacheText(string text)
        {
            text = ApacheNames.Replace(text, "SERVNOOK_$1");
            return DotfileMarkers.Replace(text, "$1 ServNook$2");
        }

        static void MigrateLog()
        {
            string oldF = Path.Combine(K.Logs, "webhut.log"), newF = Path.Combine(K.Logs, "servnook.log");
            if (File.Exists(oldF) && !File.Exists(newF)) File.Move(oldF, newF);
        }
    }
}
