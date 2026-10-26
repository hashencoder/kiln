using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ServNook
{
    /// <summary>
    /// A MariaDB option file (my.ini) edited line by line. Comments, blank lines, unknown options, ordering,
    /// encoding (UTF-8 with or without BOM, or the ANSI code page) and line endings are kept exactly; only the
    /// value part of an edited option changes. Option names compare like MariaDB does: case-insensitive, with
    /// '-' and '_' equivalent and an optional "loose-" prefix.
    /// </summary>
    public class OptionFile
    {
        public class Entry
        {
            public int Line;          // index into Lines
            public string Section;    // lower-case group name, e.g. "mysqld"
            public string RawKey;     // as written, e.g. "character-set-server"
            public string Key;        // normalised, e.g. "character_set_server"
            public string Value;      // unquoted value, or null for a bare flag ("skip-name-resolve")
            public string Prefix;     // "skip", "enable", "disable", "maximum" or null
        }

        public readonly List<string> Lines = new List<string>();
        public string Newline = "\r\n";
        bool _trailingNewline = true, _bom;
        Encoding _enc = new UTF8Encoding(false);

        public static OptionFile Load(string path)
        {
            var bytes = File.ReadAllBytes(path);
            var f = new OptionFile();
            string text;
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                f._bom = true;
                text = new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
            }
            else
            {
                try { text = new UTF8Encoding(false, true).GetString(bytes); }
                catch (DecoderFallbackException) { f._enc = Encoding.Default; text = Encoding.Default.GetString(bytes); }
            }
            f.SetText(text);
            return f;
        }

        public static OptionFile Parse(string text) { var f = new OptionFile(); f.SetText(text); return f; }

        void SetText(string text)
        {
            Newline = text.Contains("\r\n") ? "\r\n" : text.Contains("\n") ? "\n" : "\r\n";
            _trailingNewline = text.Length == 0 || text.EndsWith("\n");
            var parts = text.Replace("\r\n", "\n").Split('\n').ToList();
            if (_trailingNewline && parts.Count > 0 && parts[parts.Count - 1] == "") parts.RemoveAt(parts.Count - 1);
            Lines.Clear();
            Lines.AddRange(parts);
        }

        public string Text { get { return string.Join(Newline, Lines) + (_trailingNewline ? Newline : ""); } }

        public byte[] Bytes()
        {
            var body = _enc.GetBytes(Text);
            if (!_bom) return body;
            var all = new byte[body.Length + 3];
            all[0] = 0xEF; all[1] = 0xBB; all[2] = 0xBF;
            Buffer.BlockCopy(body, 0, all, 3, body.Length);
            return all;
        }

        public static string Norm(string key)
        {
            string k = (key ?? "").Trim().ToLowerInvariant().Replace('-', '_');
            if (k.StartsWith("loose_")) k = k.Substring(6);
            return k;
        }

        /// <summary>Groups mariadbd reads (besides version-specific ones like [mariadb-11.4]).</summary>
        public static bool IsServerGroup(string section)
        {
            var s = (section ?? "").ToLowerInvariant();
            // Version-specific groups look like [mariadb-11.4] or [mysqld-10.11]; [mariadb-dump] is a client tool's group.
            return s == "mysqld" || s == "server" || s == "mariadb" || s == "mariadbd" || s == "client-server" ||
                   System.Text.RegularExpressions.Regex.IsMatch(s, @"^(mysqld|mariadb|mariadbd)-\d+(\.\d+)*$");
        }

        public List<string> Includes()
        {
            return Lines.Where(l => l.TrimStart().StartsWith("!include", StringComparison.OrdinalIgnoreCase)).Select(l => l.Trim()).ToList();
        }

        /// <summary>Every option line, in file order.</summary>
        public List<Entry> Entries()
        {
            var list = new List<Entry>();
            string section = "";
            for (int i = 0; i < Lines.Count; i++)
            {
                string t = Lines[i].Trim();
                if (t.Length == 0 || t[0] == '#' || t[0] == ';' || t[0] == '!') continue;
                if (t[0] == '[')
                {
                    int end = t.IndexOf(']');
                    section = (end > 0 ? t.Substring(1, end - 1) : t.Substring(1)).Trim().ToLowerInvariant();
                    continue;
                }
                string body = StripComment(t).Trim();
                if (body.Length == 0) continue;
                int eq = body.IndexOf('=');
                string raw = (eq < 0 ? body : body.Substring(0, eq)).Trim();
                string val = eq < 0 ? null : Unquote(body.Substring(eq + 1).Trim());
                string key = Norm(raw), prefix = null;
                foreach (var p in new[] { "skip_", "enable_", "disable_", "maximum_" })
                    if (key.StartsWith(p)) { prefix = p.TrimEnd('_'); key = key.Substring(p.Length); break; }
                list.Add(new Entry { Line = i, Section = section, RawKey = raw, Key = key, Value = val, Prefix = prefix });
            }
            return list;
        }

        /// <summary>Remove a trailing # comment that is outside quotes (MariaDB's own rule).</summary>
        public static string StripComment(string s)
        {
            char quote = '\0'; bool escape = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if ((c == '\'' || c == '"') && !escape) { if (quote == '\0') quote = c; else if (quote == c) quote = '\0'; }
                else if (quote == '\0' && c == '#') return s.Substring(0, i);
                escape = !escape && c == '\\';
            }
            return s;
        }

        static string Unquote(string v)
        {
            if (v.Length >= 2 && (v[0] == '"' || v[0] == '\'') && v[v.Length - 1] == v[0]) return v.Substring(1, v.Length - 2);
            return v;
        }

        /// <summary>The last occurrence of <paramref name="key"/> in exactly this section (the one that wins), or null.</summary>
        public Entry Find(string section, string key)
        {
            key = Norm(key); section = section.ToLowerInvariant();
            return Entries().LastOrDefault(e => e.Section == section && e.Key == key);
        }

        /// <summary>The occurrence mariadbd actually uses: the last one in any server group.</summary>
        public Entry Effective(string key)
        {
            key = Norm(key);
            return Entries().LastOrDefault(e => IsServerGroup(e.Section) && e.Key == key);
        }

        public string Get(string section, string key)
        {
            var e = Find(section, key);
            if (e == null) return null;
            if (e.Prefix == "skip" || e.Prefix == "disable") return "0";
            if (e.Prefix == "enable") return "1";
            return e.Value ?? "1";
        }

        /// <summary>
        /// Set an option. Edits the value of the last existing occurrence in place (keeping its spelling,
        /// alignment and trailing comment), or inserts a new line after the section's last option.
        /// </summary>
        public void Set(string section, string key, string value)
        {
            if (value == null || value.IndexOfAny(new[] { '\r', '\n', '#', '"', '\'' }) >= 0)
                throw new ArgumentException("Unsupported characters in the value for " + key + ".");
            section = section.ToLowerInvariant();
            var e = Find(section, key);
            if (e != null && e.Prefix == null)
            {
                Lines[e.Line] = ReplaceValue(Lines[e.Line], value);
                return;
            }
            if (e != null)
            {
                // "skip-slow-query-log" style: rewrite the line as key = value, keeping indentation.
                string line = Lines[e.Line];
                string indent = line.Substring(0, line.Length - line.TrimStart().Length);
                Lines[e.Line] = indent + Norm(key) + " = " + value;
                return;
            }
            Insert(section, Norm(key) + Pad(section, Norm(key)) + "= " + value);
        }

        string Pad(string section, string key)
        {
            // Match the "=" column other options in the section use, if any.
            var cols = Entries().Where(x => x.Section == section && x.Value != null)
                                .Select(x => Lines[x.Line].IndexOf('=')).Where(c => c > 0).ToList();
            int col = cols.Count == 0 ? 0 : cols.GroupBy(c => c).OrderByDescending(g => g.Count()).First().Key;
            return col > key.Length ? new string(' ', col - key.Length) : " ";
        }

        static string ReplaceValue(string line, string value)
        {
            int eq = line.IndexOf('=');
            if (eq < 0)
            {
                string body = StripComment(line);
                string tail = line.Substring(body.Length);
                return body.TrimEnd() + " = " + value + (tail.Length > 0 ? "  " + tail.TrimStart() : "");
            }
            string head = line.Substring(0, eq + 1);
            string rest = line.Substring(eq + 1);
            int lead = rest.Length - rest.TrimStart().Length;
            string valueAndComment = rest.Substring(lead);
            string valuePart = StripComment(valueAndComment);
            string comment = valueAndComment.Substring(valuePart.Length);
            string gap = valuePart.Substring(valuePart.TrimEnd().Length);
            return head + rest.Substring(0, lead) + value + (comment.Length > 0 ? (gap.Length > 0 ? gap : " ") + comment : "");
        }

        void Insert(string section, string line)
        {
            int header = -1, last = -1;
            string cur = "";
            for (int i = 0; i < Lines.Count; i++)
            {
                string t = Lines[i].Trim();
                if (t.StartsWith("["))
                {
                    int end = t.IndexOf(']');
                    cur = (end > 0 ? t.Substring(1, end - 1) : t.Substring(1)).Trim().ToLowerInvariant();
                    if (cur == section) { header = i; last = i; }
                    continue;
                }
                if (cur == section && t.Length > 0 && t[0] != '#' && t[0] != ';') last = i;
            }
            if (header < 0)
            {
                if (Lines.Count > 0 && Lines[Lines.Count - 1].Trim().Length > 0) Lines.Add("");
                Lines.Add("[" + section + "]");
                Lines.Add(line);
                return;
            }
            Lines.Insert(last + 1, line);
        }

        /// <summary>Turn an option line into a comment (used to resolve settings overridden by startup arguments).</summary>
        public void CommentOut(int lineIndex, string why)
        {
            string line = Lines[lineIndex];
            string indent = line.Substring(0, line.Length - line.TrimStart().Length);
            Lines[lineIndex] = indent + "# " + line.TrimStart() + "    # disabled by ServNook: " + why;
        }
    }
}
