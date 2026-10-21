using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace WebHut
{
    /// <summary>A downloadable component resolved to an exact file.</summary>
    public class Component
    {
        public string Id;          // apache, php, mariadb, phpmyadmin, node, composer, cacert
        public string Name;        // display name
        public string Branch;      // php "8.5", node "24"
        public string Version;     // exact version, e.g. 8.5.11
        public string Url;
        public string Sha256;      // lower-case hex, or null when the publisher gives none
        public string FileName;
        public string LocalFile;   // a zip the user picked by hand instead of downloading
        public string Error;
        public string Homepage;    // where to download by hand if resolving fails

        public bool Ready { get { return LocalFile != null || (Url != null && Error == null); } }

        public override string ToString() { return Name + " " + (Version ?? "?"); }
    }

    /// <summary>
    /// Finds the latest official Windows build of each component.
    /// Order: maintainer manifest (versions.json) -> publisher's live data -> manual zip.
    /// </summary>
    public static class Resolver
    {
        const string UA = "WebHut/" + K.AppVersion + " (portable dev stack for Windows)";

        public static string Get(string url)
        {
            Sys.EnableModernTls();
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = UA;
            req.Timeout = 30000;
            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                return sr.ReadToEnd();
        }

        static string TryGet(string url)
        {
            try { return Get(url); } catch (Exception e) { Log.Warn("GET " + url + " failed: " + e.Message); return null; }
        }

        /// <summary>First 64-hex token of a .sha256 / SHASUMS file line that mentions fileName (or the first hash at all).</summary>
        public static string ShaFromText(string text, string fileName)
        {
            if (string.IsNullOrEmpty(text)) return null;
            foreach (var line in text.Split('\n'))
            {
                var m = Regex.Match(line, @"\b([a-fA-F0-9]{64})\b");
                if (!m.Success) continue;
                if (fileName == null || line.Contains(fileName)) return m.Groups[1].Value.ToLowerInvariant();
            }
            if (fileName != null)
            {
                var m = Regex.Match(text.Trim(), @"^([a-fA-F0-9]{64})");
                if (m.Success && text.Trim().Split('\n').Length == 1) return m.Groups[1].Value.ToLowerInvariant();
            }
            return null;
        }

        // ---------------- Manifest (maintainer-pinned versions) ----------------

        static object _manifest;
        static bool _manifestLoaded;
        static readonly object ManifestLock = new object();

        static object Manifest(WebHutConfig cfg)
        {
            lock (ManifestLock) // resolvers run in parallel; all must see the same manifest
            {
                if (_manifestLoaded) return _manifest;
                string url = !string.IsNullOrEmpty(cfg.ManifestUrl) ? cfg.ManifestUrl : K.DefaultManifestUrl;
                if (!string.IsNullOrEmpty(url))
                {
                    var text = TryGet(url);
                    if (text != null)
                    {
                        try { _manifest = Json.Parse(text); Log.Info("Using version manifest from " + url); }
                        catch (Exception e) { Log.Warn("Version manifest is not valid JSON: " + e.Message); }
                    }
                }
                _manifestLoaded = true;
                return _manifest;
            }
        }

        /// <summary>versions.json: { "components": { "apache": { "version", "url", "sha256" }, "php-8.5": {...} } }</summary>
        static Component FromManifest(WebHutConfig cfg, string key, Component c)
        {
            var entry = Json.Get(Json.Get(Manifest(cfg), "components"), key);
            if (entry == null || string.IsNullOrEmpty(Json.Str(entry, "url"))) return null;
            c.Version = Json.Str(entry, "version");
            c.Url = Json.Str(entry, "url");
            var sha = Json.Str(entry, "sha256");
            c.Sha256 = string.IsNullOrEmpty(sha) ? null : sha.ToLowerInvariant();
            c.FileName = Path.GetFileName(new Uri(c.Url).AbsolutePath);
            return c;
        }

        static Component Resolve(WebHutConfig cfg, string key, Component c, Action<Component> live)
        {
            try
            {
                if (FromManifest(cfg, key, c) != null) return c;
                live(c);
                if (c.Url == null && c.Error == null) c.Error = "Couldn't find a download.";
            }
            catch (Exception e) { c.Error = e.Message; }
            if (c.Error != null) Log.Warn(c.Name + ": " + c.Error);
            return c;
        }

        // ---------------- Apache (Apache Lounge) ----------------

        public static Component Apache(WebHutConfig cfg)
        {
            var c = new Component { Id = "apache", Name = "Apache httpd", Homepage = "https://www.apachelounge.com/download/" };
            return Resolve(cfg, "apache", c, x =>
            {
                ParseApache(Get(x.Homepage), x);
            });
        }

        public static void ParseApache(string html, Component x)
        {
            var best = Regex.Matches(html, @"href=[""']([^""']*?httpd-(2\.4\.\d+)-(\d+)-[Ww]in64-VS(\d+)\.zip)[""']")
                .Cast<Match>()
                .OrderByDescending(m => WebHutConfig.VersionKey(m.Groups[2].Value))
                .ThenByDescending(m => int.Parse(m.Groups[4].Value))
                .ThenByDescending(m => m.Groups[3].Value)
                .FirstOrDefault();
            if (best == null) { x.Error = "No Win64 Apache build found on Apache Lounge."; return; }
            x.Url = new Uri(new Uri(x.Homepage), best.Groups[1].Value).ToString();
            x.Version = best.Groups[2].Value + " (VS" + best.Groups[4].Value + ")";
            x.FileName = Path.GetFileName(new Uri(x.Url).AbsolutePath);
            // Apache Lounge publishes checksums on a separate page per file; not machine-readable.
            x.Sha256 = null;
        }

        // ---------------- PHP (windows.php.net) ----------------

        const string PhpReleases = "https://windows.php.net/downloads/releases/";

        /// <summary>Supported PHP branches, newest first (e.g. 8.5, 8.4, 8.3).</summary>
        public static List<string> PhpBranches()
        {
            var o = Json.Obj(Json.Parse(Get(PhpReleases + "releases.json")));
            return o.Keys.Where(k => Regex.IsMatch(k, @"^\d+\.\d+$")).OrderByDescending(WebHutConfig.VersionKey).ToList();
        }

        /// <summary>branch null = newest stable branch.</summary>
        public static Component Php(WebHutConfig cfg, string branch)
        {
            var c = new Component { Id = "php", Name = "PHP", Branch = branch, Homepage = "https://windows.php.net/download/" };
            if (branch != null && FromManifest(cfg, "php-" + branch, c) != null) return c;
            return Resolve(cfg, "php-latest-not-in-manifest", c, x =>
            {
                string json = TryGet(PhpReleases + "releases.json");
                if (json != null && ParsePhpReleases(json, x)) return;
                // Fallback: scrape the download page.
                ParsePhpDownloadPage(Get(x.Homepage), x);
            });
        }

        /// <summary>windows.php.net releases.json -> newest x64 Thread Safe zip for x.Branch (or newest branch).</summary>
        public static bool ParsePhpReleases(string json, Component x)
        {
            var all = Json.Obj(Json.Parse(json));
            if (all == null) return false;
            string b = x.Branch ?? all.Keys.Where(k => Regex.IsMatch(k, @"^\d+\.\d+$")).OrderByDescending(WebHutConfig.VersionKey).FirstOrDefault();
            object rel;
            if (b == null || !all.TryGetValue(b, out rel) || Json.Obj(rel) == null) return false;
            // Keys look like "ts-vs17-x64" / "nts-vs17-x64". Apache's mod_php needs Thread Safe.
            var tsKey = Json.Obj(rel).Keys
                .Where(k => Regex.IsMatch(k, @"^ts-v[sc]\d+-x64$"))
                .OrderByDescending(k => int.Parse(Regex.Match(k, @"\d+").Value))
                .FirstOrDefault();
            if (tsKey == null) return false;
            var zip = Json.Get(Json.Get(rel, tsKey), "zip");
            var path = Json.Str(zip, "path");
            if (path == null) return false;
            x.Branch = b;
            x.Version = Json.Str(rel, "version");
            x.Url = PhpReleases + path;
            x.FileName = path;
            var sha = (Json.Str(zip, "sha256") ?? "").ToLowerInvariant();
            x.Sha256 = sha.Length == 64 ? sha : null;
            return true;
        }

        public static void ParsePhpDownloadPage(string html, Component x)
        {
            string pattern = x.Branch == null ? @"\d+\.\d+" : Regex.Escape(x.Branch);
            var best = Regex.Matches(html, @"(?<![\w-])(php-(" + pattern + @")\.(\d+)-Win32-v[sc](\d+)-x64\.zip)")
                .Cast<Match>()
                .OrderByDescending(m => WebHutConfig.VersionKey(m.Groups[2].Value + "." + m.Groups[3].Value))
                .FirstOrDefault();
            if (best == null) { x.Error = "No PHP " + (x.Branch ?? "") + " x64 Thread Safe build found."; return; }
            x.Branch = best.Groups[2].Value;
            x.Version = best.Groups[2].Value + "." + best.Groups[3].Value;
            x.FileName = best.Groups[1].Value;
            x.Url = PhpReleases + x.FileName;
        }

        // ---------------- MariaDB (REST API) ----------------

        public static Component MariaDb(WebHutConfig cfg)
        {
            var c = new Component { Id = "mariadb", Name = "MariaDB", Homepage = "https://mariadb.org/download/" };
            return Resolve(cfg, "mariadb", c, x =>
            {
                const string api = "https://downloads.mariadb.org/rest-api/mariadb/";
                // Newest stable Long Term Support series, e.g. "12.3".
                string series = ParseMariaSeries(TryGet(api)) ?? "12.3";
                ParseMariaFiles(Get(api + series + "/latest/"), series, x);
            });
        }

        /// <summary>Newest stable Long Term Support series from the MariaDB REST API, e.g. "12.3".</summary>
        public static string ParseMariaSeries(string json)
        {
            if (json == null) return null;
            return Json.Arr(Json.Get(Json.Parse(json), "major_releases"))
                .Where(r => (Json.Str(r, "release_status") ?? "").Equals("Stable", StringComparison.OrdinalIgnoreCase)
                         && (Json.Str(r, "release_support_type") ?? "").IndexOf("Long Term", StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(r => Json.Str(r, "release_id"))
                .Where(id => id != null)
                .OrderByDescending(WebHutConfig.VersionKey)
                .FirstOrDefault();
        }

        public static void ParseMariaFiles(string json, string series, Component x)
        {
            var file = FindObjects(Json.Parse(json)).FirstOrDefault(f =>
                (Json.Str(f, "file_name") ?? "").EndsWith("-winx64.zip", StringComparison.OrdinalIgnoreCase));
            if (file == null) { x.Error = "No Windows x64 zip found for MariaDB " + series + "."; return; }
            x.FileName = Json.Str(file, "file_name");
            x.Url = Json.Str(file, "file_download_url");
            if (x.Url != null && x.Url.StartsWith("http://")) x.Url = "https://" + x.Url.Substring(7);
            var sha = Json.Str(Json.Get(file, "checksum"), "sha256sum");
            x.Sha256 = sha != null && sha.Length == 64 ? sha.ToLowerInvariant() : null;
            var m = Regex.Match(x.FileName, @"mariadb-([\d.]+)-winx64");
            x.Version = m.Success ? m.Groups[1].Value : series;
        }

        static IEnumerable<object> FindObjects(object node)
        {
            var d = Json.Obj(node);
            if (d != null)
            {
                yield return d;
                foreach (var v in d.Values) foreach (var o in FindObjects(v)) yield return o;
            }
            else if (node is object[] || node is System.Collections.ArrayList)
            {
                foreach (var item in Json.Arr(node)) foreach (var o in FindObjects(item)) yield return o;
            }
        }

        // ---------------- phpMyAdmin ----------------

        public static Component PhpMyAdmin(WebHutConfig cfg)
        {
            var c = new Component { Id = "phpmyadmin", Name = "phpMyAdmin", Homepage = "https://www.phpmyadmin.net/downloads/" };
            return Resolve(cfg, "phpmyadmin", c, x =>
            {
                var v = Json.Str(Json.Parse(Get("https://www.phpmyadmin.net/home_page/version.json")), "version");
                if (string.IsNullOrEmpty(v)) { x.Error = "phpMyAdmin version.json had no version."; return; }
                x.Version = v;
                x.FileName = "phpMyAdmin-" + v + "-all-languages.zip";
                x.Url = "https://files.phpmyadmin.net/phpMyAdmin/" + v + "/" + x.FileName;
                x.Sha256 = ShaFromText(TryGet(x.Url + ".sha256"), x.FileName);
            });
        }

        // ---------------- Node.js ----------------

        /// <summary>major null = newest LTS line.</summary>
        public static Component Node(WebHutConfig cfg, string major)
        {
            var c = new Component { Id = "node", Name = "Node.js", Branch = major, Homepage = "https://nodejs.org/en/download" };
            return Resolve(cfg, major == null ? "node" : "node-" + major, c, x =>
            {
                if (!ParseNodeIndex(Get("https://nodejs.org/dist/index.json"), major, x)) return;
                x.Sha256 = ShaFromText(TryGet("https://nodejs.org/dist/v" + x.Version + "/SHASUMS256.txt"), x.FileName);
            });
        }

        /// <summary>nodejs.org/dist/index.json -> newest LTS (major null) or newest release of a major.</summary>
        public static bool ParseNodeIndex(string json, string major, Component x)
        {
            var rel = Json.Arr(Json.Parse(json))
                .FirstOrDefault(r =>
                {
                    var v = Json.Str(r, "version") ?? "";
                    var lts = Json.Get(r, "lts");
                    bool isLts = lts != null && !(lts is bool && !(bool)lts);
                    bool hasZip = Json.Arr(Json.Get(r, "files")).Any(f => Convert.ToString(f) == "win-x64-zip");
                    bool majorOk = major == null ? isLts : v.StartsWith("v" + major + ".");
                    return hasZip && majorOk;
                });
            if (rel == null) { x.Error = "No matching Node.js release found."; return false; }
            var ver = Json.Str(rel, "version");
            x.Version = ver.TrimStart('v');
            x.Branch = x.Version.Split('.')[0];
            x.FileName = "node-" + ver + "-win-x64.zip";
            x.Url = "https://nodejs.org/dist/" + ver + "/" + x.FileName;
            return true;
        }

        // ---------------- Small extras ----------------

        public static Component Composer(WebHutConfig cfg)
        {
            var c = new Component
            {
                Id = "composer", Name = "Composer", Version = "latest stable", FileName = "composer.phar",
                Url = "https://getcomposer.org/download/latest-stable/composer.phar",
                Homepage = "https://getcomposer.org/download/"
            };
            c.Sha256 = ShaFromText(TryGet(c.Url + ".sha256"), null);
            return c;
        }

        public static Component CaCert(WebHutConfig cfg)
        {
            var c = new Component
            {
                Id = "cacert", Name = "CA certificates", Version = "Mozilla bundle", FileName = "cacert.pem",
                Url = "https://curl.se/ca/cacert.pem", Homepage = "https://curl.se/docs/caextract.html"
            };
            c.Sha256 = ShaFromText(TryGet(c.Url + ".sha256"), "cacert.pem");
            return c;
        }
    }
}
