using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace WebHut
{
    /// <summary>
    /// Controlled editing of WebHut's httpd.conf. Only specific directives are touched, in place, keeping
    /// indentation, comments, unknown directives, ordering and line endings.
    /// </summary>
    public class ApacheConfFile
    {
        public class Directive
        {
            public int Line;
            public string Name;            // as written
            public string Args;
            public List<string> Blocks;    // enclosing sections, outermost first, lower-case ("ifmodule mpm_winnt_module")
            public bool TopLevel { get { return Blocks.Count == 0; } }
        }

        public class Module
        {
            public string Name;   // rewrite_module
            public string Path;   // modules/mod_rewrite.so
            public bool Active;   // not commented out
            public int Line;
            public bool Conditional;   // inside <IfModule>/<IfDefine>
        }

        public readonly List<string> Lines = new List<string>();
        public string Newline = "\r\n";
        bool _trailing = true;

        public const string DotfilesBegin = "# BEGIN WebHut: protect dotfiles";
        public const string DotfilesEnd = "# END WebHut: protect dotfiles";
        public const string ConnHeader = "# --- Connections (set by WebHut's server configuration) ---";

        public static ApacheConfFile Load(string path) { return Parse(File.ReadAllText(path, new UTF8Encoding(false))); }

        public static ApacheConfFile Parse(string text)
        {
            var f = new ApacheConfFile();
            f.Newline = text.Contains("\r\n") ? "\r\n" : text.Contains("\n") ? "\n" : "\r\n";
            f._trailing = text.Length == 0 || text.EndsWith("\n");
            var parts = text.Replace("\r\n", "\n").Split('\n').ToList();
            if (f._trailing && parts.Count > 0 && parts[parts.Count - 1] == "") parts.RemoveAt(parts.Count - 1);
            f.Lines.AddRange(parts);
            return f;
        }

        public string Text { get { return string.Join(Newline, Lines) + (_trailing ? Newline : ""); } }

        public ApacheConfFile Clone() { return Parse(Text); }

        /// <summary>Every active directive with the sections that enclose it. Continued lines (ending in \) are skipped.</summary>
        public List<Directive> Directives()
        {
            var list = new List<Directive>();
            var stack = new List<string>();
            bool cont = false;
            for (int i = 0; i < Lines.Count; i++)
            {
                string t = Lines[i].Trim();
                bool wasCont = cont;
                cont = t.EndsWith("\\");
                if (wasCont || t.Length == 0 || t[0] == '#') continue;
                if (t.StartsWith("</")) { if (stack.Count > 0) stack.RemoveAt(stack.Count - 1); continue; }
                if (t[0] == '<')
                {
                    stack.Add(t.Trim('<', '>').Trim().ToLowerInvariant());
                    continue;
                }
                var m = Regex.Match(t, @"^(\S+)\s*(.*)$");
                list.Add(new Directive { Line = i, Name = m.Groups[1].Value, Args = m.Groups[2].Value.Trim(), Blocks = new List<string>(stack) });
            }
            return list;
        }

        public Directive Top(string name)
        {
            return Directives().LastOrDefault(d => d.TopLevel && d.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        public Directive InBlock(string name, string block)
        {
            block = block.ToLowerInvariant();
            return Directives().LastOrDefault(d => d.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && d.Blocks.Count == 1 && d.Blocks[0] == block);
        }

        public void SetArgs(Directive d, string args)
        {
            string line = Lines[d.Line];
            string indent = line.Substring(0, line.Length - line.TrimStart().Length);
            string rest = line.TrimStart().Substring(d.Name.Length);
            string gap = rest.Length - rest.TrimStart().Length > 0 ? rest.Substring(0, rest.Length - rest.TrimStart().Length) : " ";
            Lines[d.Line] = indent + d.Name + gap + args;
        }

        // ------------------------------------------------------------------ MPM (ThreadsPerChild, MaxConnectionsPerChild)

        public const string MpmBlock = "ifmodule mpm_winnt_module";

        public string Mpm(string name) { var d = InBlock(name, MpmBlock); return d == null ? null : d.Args; }

        public void SetMpm(string name, string value)
        {
            var d = InBlock(name, MpmBlock);
            if (d != null) { SetArgs(d, value); return; }
            int close = BlockEnd(MpmBlock);
            if (close < 0)
            {
                int at = InsertPoint();
                Lines.InsertRange(at, new[] { "<IfModule mpm_winnt_module>", "    " + name + " " + value, "</IfModule>" });
                return;
            }
            Lines.Insert(close, "    " + name + " " + value);
        }

        /// <summary>Index of the closing line of the first top-level section with this header, or -1.</summary>
        int BlockEnd(string header)
        {
            int depth = 0, start = -1;
            for (int i = 0; i < Lines.Count; i++)
            {
                string t = Lines[i].Trim();
                if (t.StartsWith("#") || t.Length == 0) continue;
                if (t.StartsWith("</")) { depth--; if (start >= 0 && depth == 0) return i; continue; }
                if (t.StartsWith("<"))
                {
                    if (depth == 0 && t.Trim('<', '>').Trim().ToLowerInvariant() == header) start = i;
                    depth++;
                }
            }
            return -1;
        }

        /// <summary>Where new top-level directives go: after the MPM section, else before the modules list, else at the end.</summary>
        int InsertPoint()
        {
            int end = BlockEnd(MpmBlock);
            if (end >= 0) return end + 1;
            int mods = Lines.FindIndex(l => l.TrimStart().StartsWith("LoadModule", StringComparison.OrdinalIgnoreCase));
            return mods >= 0 ? mods : Lines.Count;
        }

        // ------------------------------------------------------------------ top-level directives (KeepAlive, LogLevel, ServerName)

        public string TopArgs(string name) { var d = Top(name); return d == null ? null : d.Args; }

        public void SetTop(string name, string value)
        {
            var d = Top(name);
            if (d != null) { SetArgs(d, value); return; }
            int header = Lines.FindIndex(l => l.Trim() == ConnHeader);
            if (header < 0)
            {
                int at = InsertPoint();
                Lines.InsertRange(at, new[] { "", ConnHeader });
                header = at + 1;
            }
            int pos = header + 1;
            while (pos < Lines.Count && Lines[pos].Trim().Length > 0 && !Lines[pos].TrimStart().StartsWith("#")) pos++;
            Lines.Insert(pos, name + " " + value);
        }

        // ------------------------------------------------------------------ the www <Directory> block

        static readonly Regex WwwHeader = new Regex(@"^<Directory\s+""?\$\{WEBHUT_ROOT\}/www/?""?\s*>$", RegexOptions.IgnoreCase);

        Directive WwwDirective(string name)
        {
            return Directives().FirstOrDefault(d => d.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && d.Blocks.Count == 1 &&
                                                    WwwHeader.IsMatch("<" + Lines[FindHeader(d.Line)].Trim().Trim('<', '>') + ">"));
        }

        int FindHeader(int line)
        {
            int depth = 0;
            for (int i = line - 1; i >= 0; i--)
            {
                string t = Lines[i].Trim();
                if (t.StartsWith("#") || t.Length == 0) continue;
                if (t.StartsWith("</")) depth++;
                else if (t.StartsWith("<")) { if (depth == 0) return i; depth--; }
            }
            return 0;
        }

        /// <summary>Directory listing for www (and, through Vhosts, every generated site). Null when the line can't be found.</summary>
        public bool? Indexes
        {
            get
            {
                var d = WwwDirective("Options");
                if (d == null) return null;
                var toks = d.Args.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (toks.Any(t => t.Equals("-Indexes", StringComparison.OrdinalIgnoreCase))) return false;
                return toks.Any(t => t.TrimStart('+').Equals("Indexes", StringComparison.OrdinalIgnoreCase) || t.Equals("All", StringComparison.OrdinalIgnoreCase));
            }
        }

        public void SetIndexes(bool on)
        {
            var d = WwwDirective("Options");
            if (d == null) throw new InvalidOperationException("The Options line for the www folder wasn't found in httpd.conf.");
            var toks = d.Args.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            bool signed = toks.Any(t => t.StartsWith("+") || t.StartsWith("-"));
            toks.RemoveAll(t => t.TrimStart('+', '-').Equals("Indexes", StringComparison.OrdinalIgnoreCase));
            if (on) toks.Insert(0, signed ? "+Indexes" : "Indexes");
            else if (signed) toks.Insert(0, "-Indexes");
            if (toks.Count == 0) toks.Add("None");
            if (toks.Count > 1) toks.RemoveAll(t => t.Equals("None", StringComparison.OrdinalIgnoreCase));
            SetArgs(d, string.Join(" ", toks));
        }

        /// <summary>AllowOverride for www: true = All (.htaccess allowed), false = None.</summary>
        public bool? HtaccessAllowed
        {
            get
            {
                var d = WwwDirective("AllowOverride");
                if (d == null) return null;
                return !d.Args.Trim().Equals("None", StringComparison.OrdinalIgnoreCase);
            }
        }

        public void SetHtaccess(bool on)
        {
            var d = WwwDirective("AllowOverride");
            if (d == null) throw new InvalidOperationException("The AllowOverride line for the www folder wasn't found in httpd.conf.");
            SetArgs(d, on ? "All" : "None");
        }

        // ------------------------------------------------------------------ PHP mode

        static readonly Regex FcgidDefine = new Regex(@"^(\s*)(#\s*)?Define\s+WEBHUT_PHP_FCGID\b(.*)$", RegexOptions.IgnoreCase);

        public bool Fcgid
        {
            get { return Lines.Any(l => { var m = FcgidDefine.Match(l); return m.Success && !m.Groups[2].Success; }); }
        }

        public void SetFcgid(bool on)
        {
            int i = Lines.FindIndex(l => FcgidDefine.IsMatch(l));
            if (i < 0)
            {
                if (!on) return;
                Lines.Insert(InsertPoint(), "Define WEBHUT_PHP_FCGID 1");
                return;
            }
            var m = FcgidDefine.Match(Lines[i]);
            string rest = m.Groups[3].Value.Trim().Length == 0 ? " 1" : m.Groups[3].Value;
            Lines[i] = m.Groups[1].Value + (on ? "" : "#") + "Define WEBHUT_PHP_FCGID" + rest;
        }

        // ------------------------------------------------------------------ ServerName host

        public string ServerHost
        {
            get
            {
                var a = TopArgs("ServerName");
                if (a == null) return null;
                int c = a.LastIndexOf(':');
                return c > 0 ? a.Substring(0, c) : a;
            }
        }

        public void SetServerHost(string host)
        {
            var a = TopArgs("ServerName") ?? "localhost:${WEBHUT_HTTP_PORT}";
            int c = a.LastIndexOf(':');
            SetTop("ServerName", host + (c > 0 ? a.Substring(c) : ":${WEBHUT_HTTP_PORT}"));
        }

        // ------------------------------------------------------------------ dotfile protection (managed block)

        public bool ProtectDotfiles { get { return Lines.Any(l => l.Trim() == DotfilesBegin); } }

        public void SetProtectDotfiles(bool on)
        {
            int b = Lines.FindIndex(l => l.Trim() == DotfilesBegin);
            if (!on)
            {
                if (b < 0) return;
                int e = Lines.FindIndex(b, l => l.Trim() == DotfilesEnd);
                if (e < 0) throw new InvalidOperationException("The WebHut dotfile block in httpd.conf has no END line; edit it by hand.");
                Lines.RemoveRange(b, e - b + 1);
                return;
            }
            if (b >= 0) return;
            int at = Lines.FindIndex(l => l.Trim().StartsWith("<Files \".ht*\">", StringComparison.OrdinalIgnoreCase));
            if (at >= 0) { at = Lines.FindIndex(at, l => l.Trim().StartsWith("</Files", StringComparison.OrdinalIgnoreCase)) + 1; }
            if (at <= 0) at = Lines.Count;
            Lines.InsertRange(at, new[]
            {
                DotfilesBegin,
                "# Version-control folders and secrets files (.git, .svn, .env) are never served.",
                "<FilesMatch \"^\\.(env|env\\..*|git.*|svn.*|hg.*|DS_Store)$\">",
                "    Require all denied",
                "</FilesMatch>",
                "<DirectoryMatch \"/\\.(git|svn|hg)(/|$)\">",
                "    Require all denied",
                "</DirectoryMatch>",
                DotfilesEnd
            });
        }

        public bool HtFilesDenied
        {
            get
            {
                int i = Lines.FindIndex(l => l.Trim().StartsWith("<Files \".ht*\">", StringComparison.OrdinalIgnoreCase));
                return i >= 0 && i + 1 < Lines.Count && Lines[i + 1].Trim().Equals("Require all denied", StringComparison.OrdinalIgnoreCase);
            }
        }

        // ------------------------------------------------------------------ modules

        static readonly Regex LoadRx = new Regex(@"^(\s*)(#\s*)?LoadModule\s+(\S+)\s+(.+?)\s*$", RegexOptions.IgnoreCase);

        public List<Module> Modules()
        {
            var list = new List<Module>();
            int depth = 0;
            for (int i = 0; i < Lines.Count; i++)
            {
                string t = Lines[i].Trim();
                var m = LoadRx.Match(Lines[i]);
                if (m.Success)
                {
                    list.Add(new Module { Name = m.Groups[3].Value, Path = m.Groups[4].Value.Trim('"'), Active = !m.Groups[2].Success, Line = i, Conditional = depth > 0 });
                    continue;
                }
                if (t.StartsWith("#") || t.Length == 0) continue;
                if (t.StartsWith("</")) depth--;
                else if (t.StartsWith("<")) depth++;
            }
            return list;
        }

        /// <summary>Load or stop loading a module from httpd.conf (comment / uncomment, or add a LoadModule line).</summary>
        public void SetModule(string name, bool on)
        {
            var all = Modules().Where(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (!on)
            {
                foreach (var x in all.Where(x => x.Active))
                {
                    var m = LoadRx.Match(Lines[x.Line]);
                    Lines[x.Line] = m.Groups[1].Value + "#" + Lines[x.Line].TrimStart();
                }
                return;
            }
            if (all.Any(x => x.Active)) return;
            var commented = all.LastOrDefault();
            if (commented != null)
            {
                var m = LoadRx.Match(Lines[commented.Line]);
                string body = Lines[commented.Line].TrimStart().Substring(m.Groups[2].Value.Length);
                Lines[commented.Line] = m.Groups[1].Value + body;
                return;
            }
            var mods = Modules().Where(x => !x.Conditional).ToList();
            int at = mods.Count > 0 ? mods.Max(x => x.Line) + 1 : InsertPoint();
            Lines.Insert(at, "LoadModule " + name.PadRight(20) + " modules/" + FileFor(name));
        }

        /// <summary>rewrite_module -> mod_rewrite.so (the Apache naming rule).</summary>
        public static string FileFor(string module)
        {
            string n = module.EndsWith("_module") ? module.Substring(0, module.Length - 7) : module;
            return "mod_" + n + ".so";
        }

        public static string ModuleForFile(string file)
        {
            string n = Path.GetFileNameWithoutExtension(file);
            if (n.StartsWith("mod_")) n = n.Substring(4);
            return n + "_module";
        }
    }

    /// <summary>Facts about the installed Apache: version, MPM, modules (from httpd itself).</summary>
    public class ApacheInfo
    {
        public bool Installed;
        public string Version = "", Mpm = "";
        public List<string> Loaded = new List<string>();     // from httpd -M (effective)
        public string LoadedError;                            // httpd -M failed (config invalid)
        public List<string> Available = new List<string>();  // modules/*.so
        public bool FcgidModule, PhpCgi;
        public string SyntaxOutput;

        public bool MpmIsWinnt { get { return Mpm.IndexOf("winnt", StringComparison.OrdinalIgnoreCase) >= 0; } }

        public static ApacheInfo Probe(WebHutConfig cfg)
        {
            var a = new ApacheInfo { Installed = File.Exists(K.Httpd) };
            string mods = Path.Combine(K.Apache, "modules");
            if (Directory.Exists(mods))
                a.Available = Directory.GetFiles(mods, "*.so").Select(ApacheConfFile.ModuleForFile).OrderBy(x => x).ToList();
            a.FcgidModule = File.Exists(Path.Combine(mods, "mod_fcgid.so"));
            a.PhpCgi = !string.IsNullOrEmpty(cfg.Php) && File.Exists(Path.Combine(cfg.PhpDir, "php-cgi.exe"));
            if (!a.Installed) return a;
            string bin = Path.Combine(K.Apache, "bin");
            try
            {
                var v = Sys.Run(K.Httpd, "-V", null, 20000, bin);
                var m = Regex.Match(v.Output, @"Server version:\s*Apache/(\S+)");
                if (m.Success) a.Version = m.Groups[1].Value;
                m = Regex.Match(v.Output, @"Server MPM:\s*(\S+)");
                if (m.Success) a.Mpm = m.Groups[1].Value;
                var r = Sys.Run(K.Httpd, Services.Apache.Args(cfg, K.HttpdConf) + " -M", Services.Env(), 30000, bin);
                if (r.ExitCode == 0)
                    a.Loaded = Regex.Matches(r.Output, @"^\s*(\w+_module)\s+\((?:static|shared)\)", RegexOptions.Multiline).Cast<Match>().Select(x => x.Groups[1].Value).ToList();
                else a.LoadedError = r.Output.Trim();
            }
            catch (Exception e) { a.LoadedError = e.Message; }
            return a;
        }
    }

    /// <summary>A finding from checking a virtual-host file's access rules.</summary>
    public class VhostFinding { public string File; public int Line; public bool Warning; public string Text; }

    public class VhostFile
    {
        public string Path, Name;
        public bool Generated;
        public List<string> Hosts = new List<string>();
        public List<VhostFinding> Findings = new List<VhostFinding>();
    }

    public static class VhostAudit
    {
        public static List<VhostFile> Scan(bool lan)
        {
            var list = new List<VhostFile>();
            foreach (var kind in new[] { "generated", "custom" })
            {
                string dir = System.IO.Path.Combine(K.ApacheConf, kind);
                if (!Directory.Exists(dir)) continue;
                foreach (var f in Directory.GetFiles(dir, "*.conf").OrderBy(x => x))
                {
                    var v = new VhostFile { Path = f, Name = kind + "/" + System.IO.Path.GetFileName(f), Generated = kind == "generated" };
                    string[] lines;
                    try { lines = File.ReadAllLines(f); } catch (Exception e) { v.Findings.Add(new VhostFinding { File = v.Name, Warning = true, Text = "Can't read: " + e.Message }); list.Add(v); continue; }
                    for (int i = 0; i < lines.Length; i++)
                    {
                        string t = lines[i].Trim();
                        if (t.StartsWith("#")) continue;
                        var m = Regex.Match(t, @"^Server(?:Name|Alias)\s+(.+)$", RegexOptions.IgnoreCase);
                        if (m.Success) v.Hosts.AddRange(m.Groups[1].Value.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries));
                        if (Regex.IsMatch(t, @"^Require\s+all\s+granted\b", RegexOptions.IgnoreCase))
                            v.Findings.Add(new VhostFinding { File = v.Name, Line = i + 1, Warning = lan, Text = lan
                                ? "Line " + (i + 1) + ": \"Require all granted\" lets any device that can reach this PC open it while network access is on. Use \"Require local\" or the private-network rule WebHut generates."
                                : "Line " + (i + 1) + ": \"Require all granted\" — fine while network access is off (Apache listens on 127.0.0.1 only), but it opens this site to any device once network access is on." });
                        if (Regex.IsMatch(t, @"^Listen\s+", RegexOptions.IgnoreCase))
                            v.Findings.Add(new VhostFinding { File = v.Name, Line = i + 1, Warning = true, Text = "Line " + (i + 1) + ": \"" + t + "\" opens an extra port that WebHut's network-access setting doesn't control." + (t.Contains("127.0.0.1") ? "" : " Bind it to 127.0.0.1 to keep it local.") });
                    }
                    v.Hosts = v.Hosts.Distinct().ToList();
                    list.Add(v);
                }
            }
            return list;
        }

        /// <summary>Server-wide directives that a later-included file sets again (and therefore overrides).</summary>
        public static Dictionary<string, string> Overrides(params string[] names)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kind in new[] { "generated", "custom" })
            {
                string dir = System.IO.Path.Combine(K.ApacheConf, kind);
                if (!Directory.Exists(dir)) continue;
                foreach (var f in Directory.GetFiles(dir, "*.conf").OrderBy(x => x))
                {
                    try
                    {
                        var conf = ApacheConfFile.Load(f);
                        foreach (var n in names)
                        {
                            var x = conf.Top(n);
                            if (x != null) d[n] = kind + "/" + System.IO.Path.GetFileName(f) + " (line " + (x.Line + 1) + ": " + x.Name + " " + x.Args + ")";
                        }
                    }
                    catch { }
                }
            }
            return d;
        }
    }
}
