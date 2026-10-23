using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace WebHut
{
    /// <summary>Well-known paths. Everything is relative to the folder WebHut.exe lives in.</summary>
    public static class K
    {
        public const string AppName = "WebHut";
        public const string AppVersion = "1.7.0";

        // Maintainers: set this to the raw URL of versions.json in your GitHub
        // repo so every WebHut install can pick up pinned versions you publish,
        // e.g. https://raw.githubusercontent.com/<you>/webhut/main/versions.json
        public const string DefaultManifestUrl = "";

        public static readonly string Root = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
        public static string RootFwd { get { return Fwd(Root); } }

        public static string Apache { get { return Path.Combine(Root, "apache"); } }
        public static string Httpd { get { return Path.Combine(Apache, "bin", "httpd.exe"); } }
        public static string PhpRoot { get { return Path.Combine(Root, "php"); } }
        public static string PhpDir(string branch) { return Path.Combine(PhpRoot, branch); }
        public static string MariaDb { get { return Path.Combine(Root, "mariadb"); } }
        public static string MariaDbBin { get { return Path.Combine(MariaDb, "bin"); } }
        public static string NodeRoot { get { return Path.Combine(Root, "node"); } }
        // Optional database engines (Engines.cs)
        public static string MySql { get { return Path.Combine(Root, "mysql"); } }
        public static string MySqlBin { get { return Path.Combine(MySql, "bin"); } }
        public static string MySqlData { get { return Path.Combine(Data, "mysql"); } }
        public static string PgSql { get { return Path.Combine(Root, "postgresql"); } }
        public static string PgBin { get { return Path.Combine(PgSql, "bin"); } }
        public static string PgData { get { return Path.Combine(Data, "postgresql"); } }
        public static string Mongo { get { return Path.Combine(Root, "mongodb"); } }
        public static string MongoBin { get { return Path.Combine(Mongo, "bin"); } }
        public static string MongoData { get { return Path.Combine(Data, "mongodb"); } }
        public static string Mongosh { get { return Path.Combine(Root, "mongosh"); } }
        public static string MongoshBin { get { return Path.Combine(Mongosh, "bin"); } }
        public static string Adminer { get { return Path.Combine(Root, "apps", "adminer"); } }
        public static string NodeDir(string major) { return Path.Combine(NodeRoot, major); }
        public static string Config { get { return Path.Combine(Root, "config"); } }
        public static string ApacheConf { get { return Path.Combine(Config, "apache"); } }
        public static string HttpdConf { get { return Path.Combine(ApacheConf, "httpd.conf"); } }
        public static string MyIni { get { return Path.Combine(Config, "mariadb", "my.ini"); } }
        public static string Data { get { return Path.Combine(Root, "data"); } }
        public static string MariaDbData { get { return Path.Combine(Data, "mariadb"); } }
        public static string Apps { get { return Path.Combine(Root, "apps"); } }
        public static string Pma { get { return Path.Combine(Apps, "phpmyadmin"); } }
        public static string Www { get { return Path.Combine(Root, "www"); } }
        public static string Logs { get { return Path.Combine(Root, "logs"); } }
        public static string Tmp { get { return Path.Combine(Root, "tmp"); } }
        public static string Pids { get { return Path.Combine(Tmp, "pids"); } }
        public static string Downloads { get { return Path.Combine(Tmp, "downloads"); } }
        public static string Ssl { get { return Path.Combine(Root, "ssl"); } }
        public static string Tools { get { return Path.Combine(Root, "tools"); } }
        public static string Backups { get { return Path.Combine(Data, "backups"); } }
        public static string ConfigFile { get { return Path.Combine(Root, "webhut.json"); } }
        public static string NodeAppsFile { get { return Path.Combine(Root, "node-apps.json"); } }

        public static string Fwd(string p) { return p.Replace('\\', '/').TrimEnd('/'); }

        /// <summary>WebHut refuses to run from paths Apache/PHP handle badly.</summary>
        public static string RootProblem()
        {
            if (Root.Contains(" "))
                return "The WebHut folder path contains a space (" + Root + "). Apache and PHP config paths break on spaces. Move WebHut to a path like C:\\webhut.";
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if ((pf.Length > 0 && Root.StartsWith(pf, StringComparison.OrdinalIgnoreCase)) ||
                (pf86.Length > 0 && Root.StartsWith(pf86, StringComparison.OrdinalIgnoreCase)))
                return "WebHut is inside Program Files, where it can't write its own files without admin rights. Move it to a path like C:\\webhut.";
            return null;
        }

        /// <summary>Non-blocking advice about the WebHut folder location.</summary>
        public static string RootWarning()
        {
            if (Root.Length > 60)
                return "The WebHut folder path is long (" + Root.Length + " characters). Deep node_modules folders can hit Windows' 260-character path limit; a short path like C:\\webhut avoids that.";
            return null;
        }
    }

    public static class Log
    {
        public static event Action<string> Line;
        public static bool ToConsole;
        static readonly object Gate = new object();

        public static void Info(string msg) { Write("INFO", msg); }
        public static void Warn(string msg) { Write("WARN", msg); }
        public static void Error(string msg) { Write("ERROR", msg); }

        static void Write(string level, string msg)
        {
            string line = DateTime.Now.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) + "  " + (level == "INFO" ? "" : level + ": ") + msg;
            lock (Gate)
            {
                try
                {
                    Directory.CreateDirectory(K.Logs);
                    File.AppendAllText(Path.Combine(K.Logs, "webhut.log"),
                        DateTime.Now.ToString("yyyy-MM-dd ", System.Globalization.CultureInfo.InvariantCulture) + line + Environment.NewLine);
                }
                catch { }
            }
            if (ToConsole) Console.WriteLine(line);
            var h = Line;
            if (h != null) h(line);
        }
    }

    public static class Json
    {
        static JavaScriptSerializer Ser()
        {
            return new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 256 };
        }

        public static object Parse(string text) { return Ser().DeserializeObject(text); }

        public static Dictionary<string, object> Obj(object o) { return o as Dictionary<string, object>; }

        public static IEnumerable<object> Arr(object o)
        {
            var a = o as object[];
            if (a != null) return a;
            var l = o as ArrayList;
            if (l != null) return l.Cast<object>();
            return Enumerable.Empty<object>();
        }

        public static object Get(object o, string key)
        {
            var d = Obj(o);
            object v;
            return d != null && d.TryGetValue(key, out v) ? v : null;
        }

        public static string Str(object o, string key)
        {
            var v = Get(o, key);
            return v == null ? null : Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>Pretty-printed JSON so webhut.json stays hand-editable.</summary>
        public static string Pretty(object value)
        {
            var sb = new StringBuilder();
            WriteValue(sb, value, 0);
            sb.Append('\n');
            return sb.ToString();
        }

        static void WriteValue(StringBuilder sb, object v, int indent)
        {
            string pad = new string(' ', (indent + 1) * 2), end = new string(' ', indent * 2);
            if (v == null) { sb.Append("null"); return; }
            if (v is string) { sb.Append(Ser().Serialize(v)); return; }
            if (v is bool) { sb.Append((bool)v ? "true" : "false"); return; }
            var dict = v as IDictionary;
            if (dict != null)
            {
                if (dict.Count == 0) { sb.Append("{}"); return; }
                sb.Append("{\n");
                int i = 0;
                foreach (DictionaryEntry e in dict)
                {
                    sb.Append(pad).Append(Ser().Serialize(Convert.ToString(e.Key))).Append(": ");
                    WriteValue(sb, e.Value, indent + 1);
                    sb.Append(++i < dict.Count ? ",\n" : "\n");
                }
                sb.Append(end).Append('}');
                return;
            }
            var list = v as IEnumerable;
            if (list != null)
            {
                var items = list.Cast<object>().ToList();
                if (items.Count == 0) { sb.Append("[]"); return; }
                sb.Append("[\n");
                for (int i = 0; i < items.Count; i++)
                {
                    sb.Append(pad);
                    WriteValue(sb, items[i], indent + 1);
                    sb.Append(i < items.Count - 1 ? ",\n" : "\n");
                }
                sb.Append(end).Append(']');
                return;
            }
            sb.Append(Ser().Serialize(v));
        }
    }

    public static class Native
    {
        const uint EVENT_MODIFY_STATE = 0x0002;
        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern IntPtr OpenEvent(uint access, bool inherit, string name);
        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetEvent(IntPtr handle);
        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(IntPtr handle);
        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr GetStdHandle(int which);
        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetHandleInformation(IntPtr handle, uint mask, uint flags);

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern bool ShutdownBlockReasonCreate(IntPtr hwnd, string reason);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool ShutdownBlockReasonDestroy(IntPtr hwnd);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        static extern uint GetConsoleProcessList(uint[] list, uint count);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        struct STARTUPINFO
        {
            public int cb; public string lpReserved, lpDesktop, lpTitle;
            public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
            public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
        }
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }
        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern bool CreateProcess(string app, System.Text.StringBuilder cmd, IntPtr pa, IntPtr ta, bool inherit, uint flags,
                                         string env, string dir, ref STARTUPINFO si, out PROCESS_INFORMATION pi);

        /// <summary>
        /// Start a server so it shares nothing with WebHut: no inherited handles (a server must never hold the pipe of a
        /// script that ran "whut start ... | something", or that script waits for ever), no console, its own process group.
        /// Returns null if the process is already gone. Off Windows (tests), a normal Process.Start.
        /// </summary>
        public static System.Diagnostics.Process StartDetached(System.Diagnostics.ProcessStartInfo psi)
        {
            if (!Sys.IsWindows) return System.Diagnostics.Process.Start(psi);
            var env = new System.Text.StringBuilder();
            foreach (var key in psi.EnvironmentVariables.Keys.Cast<string>().OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
                env.Append(key).Append('=').Append(psi.EnvironmentVariables[key]).Append('\0');
            env.Append('\0');
            var cmd = new System.Text.StringBuilder("\"" + psi.FileName + "\" " + (psi.Arguments ?? ""));
            var si = new STARTUPINFO();
            si.cb = System.Runtime.InteropServices.Marshal.SizeOf(typeof(STARTUPINFO));
            PROCESS_INFORMATION pi;
            const uint CREATE_NO_WINDOW = 0x08000000, CREATE_UNICODE_ENVIRONMENT = 0x00000400, CREATE_NEW_PROCESS_GROUP = 0x00000200;
            if (!CreateProcess(null, cmd, IntPtr.Zero, IntPtr.Zero, false, CREATE_NO_WINDOW | CREATE_UNICODE_ENVIRONMENT | CREATE_NEW_PROCESS_GROUP,
                               env.ToString(), string.IsNullOrEmpty(psi.WorkingDirectory) ? null : psi.WorkingDirectory, ref si, out pi))
                throw new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error(), "Couldn't start " + psi.FileName);
            try { return System.Diagnostics.Process.GetProcessById(pi.dwProcessId); }
            catch { return null; }   // exited already
            finally { CloseHandle(pi.hThread); CloseHandle(pi.hProcess); }
        }

        /// <summary>True when this process is the only one attached to its console, i.e. Windows created the
        /// console just for us because the program was double-clicked rather than run from a terminal.</summary>
        public static bool OwnsConsoleAlone()
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) return false;
            try { var l = new uint[4]; return GetConsoleProcessList(l, 4) == 1; } catch { return false; }
        }

        /// <summary>Show "WebHut: stopping the database safely" on the Windows shutdown screen while we work.</summary>
        public static void BlockShutdown(IntPtr hwnd, string reason) { try { ShutdownBlockReasonCreate(hwnd, reason); } catch { } }
        public static void UnblockShutdown(IntPtr hwnd) { try { ShutdownBlockReasonDestroy(hwnd); } catch { } }

        /// <summary>Signal a named event, e.g. Apache's "ap1234_shutdown". False if it doesn't exist.</summary>
        public static bool Signal(string name)
        {
            try
            {
                IntPtr h = OpenEvent(EVENT_MODIFY_STATE, false, name);
                if (h == IntPtr.Zero) return false;
                try { return SetEvent(h); } finally { CloseHandle(h); }
            }
            catch { return false; }
        }

        /// <summary>
        /// Stop servers we launch from inheriting our stdin/stdout/stderr. Otherwise a caller that
        /// captures whut's output (PowerShell, CI) would wait forever for Apache to close the pipe.
        /// </summary>
        public static void DontInheritStdHandles()
        {
            try
            {
                foreach (int which in new[] { -10, -11, -12 })
                {
                    IntPtr h = GetStdHandle(which);
                    if (h != IntPtr.Zero && h != new IntPtr(-1)) SetHandleInformation(h, 1 /*HANDLE_FLAG_INHERIT*/, 0);
                }
            }
            catch { }
        }
    }

    public static class Sys
    {
        /// <summary>
        /// True if something accepts TCP connections on this port on the loopback address
        /// (IPv4 or IPv6). A real connect, so it never mistakes closing/TIME_WAIT sockets for a server.
        /// </summary>
        public static bool IsWindows { get { return Path.DirectorySeparatorChar == '\\'; } }

        public static bool IsPortListening(int port)
        {
            foreach (var addr in new[] { IPAddress.Loopback, IPAddress.IPv6Loopback })
            {
                try
                {
                    using (var c = new System.Net.Sockets.TcpClient(addr.AddressFamily))
                    {
                        var ar = c.BeginConnect(addr, port, null, null);
                        if (ar.AsyncWaitHandle.WaitOne(400) && c.Connected) { c.EndConnect(ar); return true; }
                    }
                }
                catch { }
            }
            return false;
        }

        /// <summary>This PC's IPv4 addresses on private networks (Wi-Fi/Ethernet), e.g. 192.168.1.5.</summary>
        public static List<string> LanAddresses()
        {
            var list = new List<string>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback || ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                    var d = (ni.Description + " " + ni.Name).ToLowerInvariant();
                    if (d.Contains("virtual") || d.Contains("vmware") || d.Contains("hyper-v") || d.Contains("vethernet") || d.Contains("wsl") || d.Contains("docker")) continue;
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) continue;
                        var b = ua.Address.GetAddressBytes();
                        bool priv = b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168);
                        if (priv && !list.Contains(ua.Address.ToString())) list.Add(ua.Address.ToString());
                    }
                }
            }
            catch { }
            return list;
        }

        /// <summary>Who is listening on a port, e.g. "httpd.exe (PID 1234)". Uses netstat.</summary>
        public static string PortOwner(int port)
        {
            try
            {
                var r = Run("netstat.exe", "-ano -p TCP", null, 15000);
                var r6 = Run("netstat.exe", "-ano -p TCPv6", null, 15000);
                foreach (var raw in (r.Output + "\n" + r6.Output).Split('\n'))
                {
                    var parts = raw.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 5 || !parts[3].Equals("LISTENING", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!parts[1].EndsWith(":" + port)) continue;
                    int pid;
                    if (!int.TryParse(parts[4], out pid)) continue;
                    if (pid == 4)
                        return "Windows HTTP.sys (PID 4) - usually IIS / \"World Wide Web Publishing Service\", or another app using HTTP.sys";
                    string name = "unknown";
                    try { name = Process.GetProcessById(pid).ProcessName + ".exe"; } catch { }
                    return name + " (PID " + pid + ")";
                }
            }
            catch { }
            return "another program";
        }

        public static bool IsAlive(int pid, params string[] names)
        {
            if (pid <= 0) return false;
            try
            {
                // Mono/Linux only: a killed process can linger as a zombie; Windows never has one.
                var stat = "/proc/" + pid + "/stat";
                if (File.Exists(stat))
                {
                    var st = File.ReadAllText(stat);
                    int close = st.LastIndexOf(')');
                    if (close > 0 && close + 2 < st.Length && st[close + 2] == 'Z') return false;
                }
                var p = Process.GetProcessById(pid);
                if (p.HasExited) return false;
                return names.Length == 0 || names.Any(n => p.ProcessName.Equals(n, StringComparison.OrdinalIgnoreCase));
            }
            catch { return false; }
        }

        /// <summary>
        /// PID from a pid file, but only if that process was started around the time the file was
        /// written - so a stale file from before a reboot never points at an unrelated process.
        /// </summary>
        public static int LivePid(string pidFile, params string[] names)
        {
            int pid = ReadPid(pidFile);
            if (!IsAlive(pid, names)) return 0;
            try
            {
                var written = File.GetLastWriteTime(pidFile);
                var started = Process.GetProcessById(pid).StartTime;
                if (started > written.AddSeconds(30) || started < written.AddMinutes(-10)) return 0;
            }
            catch { }
            return pid;
        }

        /// <summary>Parent process id (Linux /proc only; 0 elsewhere).</summary>
        public static int ParentPid(int pid)
        {
            try
            {
                var st = File.ReadAllText("/proc/" + pid + "/stat");
                var parts = st.Substring(st.LastIndexOf(')') + 2).Split(' ');
                return int.Parse(parts[1]);
            }
            catch { return 0; }
        }

        public static int ReadPid(string file)
        {
            try
            {
                int pid;
                return File.Exists(file) && int.TryParse(File.ReadAllText(file).Trim(), out pid) ? pid : 0;
            }
            catch { return 0; }
        }

        /// <summary>Processes with this image name whose exe lives under a folder (catches orphans).</summary>
        public static List<Process> ProcessesUnder(string name, string folder)
        {
            var list = new List<Process>();
            foreach (var p in Process.GetProcessesByName(name))
            {
                try
                {
                    if (p.MainModule.FileName.StartsWith(folder, StringComparison.OrdinalIgnoreCase) && IsAlive(p.Id)) list.Add(p);
                }
                catch { }
            }
            return list;
        }

        public static void KillTree(int pid)
        {
            if (pid <= 0) return;
            if (IsWindows) Run("taskkill.exe", "/PID " + pid + " /T /F", null, 15000);
            else { try { Run("kill", "-9 " + pid, null, 5000); } catch { } }   // Linux test stand-in
        }

        public class RunResult { public int ExitCode; public string Output; }

        public static RunResult Run(string exe, string args, Dictionary<string, string> env, int timeoutMs, string workDir = null)
        {
            return Run(exe, args, env, timeoutMs, workDir, null);
        }

        /// <summary>Run a program; <paramref name="stdin"/> (UTF-8) is written to its input, e.g. SQL that must not appear on the command line.</summary>
        public static RunResult Run(string exe, string args, Dictionary<string, string> env, int timeoutMs, string workDir, string stdin)
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = stdin != null,
                WorkingDirectory = workDir ?? K.Root
            };
            if (env != null) foreach (var kv in env) psi.EnvironmentVariables[kv.Key] = kv.Value;
            var sb = new StringBuilder();
            using (var outDone = new System.Threading.ManualResetEvent(false))
            using (var errDone = new System.Threading.ManualResetEvent(false))
            using (var p = new Process { StartInfo = psi })
            {
                p.OutputDataReceived += (s, e) => { if (e.Data == null) outDone.Set(); else lock (sb) sb.AppendLine(e.Data); };
                p.ErrorDataReceived += (s, e) => { if (e.Data == null) errDone.Set(); else lock (sb) sb.AppendLine(e.Data); };
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                if (stdin != null)
                {
                    try
                    {
                        var bytes = new UTF8Encoding(false).GetBytes(stdin);
                        p.StandardInput.BaseStream.Write(bytes, 0, bytes.Length);
                        p.StandardInput.Close();
                    }
                    catch (IOException) { }   // the program exited early; its output says why
                }
                if (!p.WaitForExit(timeoutMs))
                {
                    try { p.Kill(); } catch { }
                    return new RunResult { ExitCode = -1, Output = sb + "\n(timed out)" };
                }
                // Wait for the output streams to close so no output is lost - but bounded, because a
                // server started meanwhile could have inherited the pipe and keep it open.
                outDone.WaitOne(5000);
                errDone.WaitOne(5000);
                lock (sb) return new RunResult { ExitCode = p.ExitCode, Output = sb.ToString() };
            }
        }

        public static string Tail(string file, int lines)
        {
            try
            {
                if (!File.Exists(file)) return "";
                using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sr = new StreamReader(fs))
                {
                    var all = sr.ReadToEnd().Split('\n');
                    return string.Join("\n", all.Skip(Math.Max(0, all.Length - lines - 1))).Trim();
                }
            }
            catch { return ""; }
        }

        public static string Sha256File(string path)
        {
            using (var sha = SHA256.Create())
            using (var fs = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
        }

        public static string RandomToken(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            var bytes = new byte[length];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            var sb = new StringBuilder(length);
            foreach (var b in bytes) sb.Append(chars[b % chars.Length]);
            return sb.ToString();
        }

        public static void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception e) { Log.Error("Couldn't open " + url + ": " + e.Message); }
        }

        public static void OpenInEditor(string file)
        {
            try
            {
                if (!File.Exists(file)) { Log.Warn("File doesn't exist yet: " + file); return; }
                Process.Start(new ProcessStartInfo("notepad.exe", "\"" + file + "\"") { UseShellExecute = true });
            }
            catch (Exception e) { Log.Error("Couldn't open " + file + ": " + e.Message); }
        }

        public static void OpenFolder(string dir)
        {
            try { Directory.CreateDirectory(dir); Process.Start(new ProcessStartInfo("explorer.exe", "\"" + dir + "\"") { UseShellExecute = true }); }
            catch (Exception e) { Log.Error("Couldn't open " + dir + ": " + e.Message); }
        }

        public static void EnableModernTls()
        {
            // WebHut.exe.config opts into SystemDefault (0): Windows then picks TLS 1.2/1.3 itself.
            // Never force TLS 1.3: on Windows 10 SChannel has no TLS 1.3 client, and naming it
            // explicitly makes every HTTPS request fail ("The function requested is not supported").
            try
            {
                var p = ServicePointManager.SecurityProtocol;
                if (p != 0 && (p & SecurityProtocolType.Tls12) == 0)
                    ServicePointManager.SecurityProtocol = p | SecurityProtocolType.Tls12;
            }
            catch { }
        }

        /// <summary>Best-effort Windows time zone -> IANA name for php.ini.</summary>
        public static string IanaTimeZone()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                {"India Standard Time","Asia/Kolkata"},{"UTC","UTC"},{"GMT Standard Time","Europe/London"},
                {"W. Europe Standard Time","Europe/Berlin"},{"Central Europe Standard Time","Europe/Budapest"},
                {"Romance Standard Time","Europe/Paris"},{"E. Europe Standard Time","Europe/Bucharest"},
                {"Russian Standard Time","Europe/Moscow"},{"Eastern Standard Time","America/New_York"},
                {"Central Standard Time","America/Chicago"},{"Mountain Standard Time","America/Denver"},
                {"Pacific Standard Time","America/Los_Angeles"},{"Alaskan Standard Time","America/Anchorage"},
                {"Hawaiian Standard Time","Pacific/Honolulu"},{"E. South America Standard Time","America/Sao_Paulo"},
                {"Argentina Standard Time","America/Argentina/Buenos_Aires"},{"Arabian Standard Time","Asia/Dubai"},
                {"Arab Standard Time","Asia/Riyadh"},{"Pakistan Standard Time","Asia/Karachi"},
                {"Bangladesh Standard Time","Asia/Dhaka"},{"Nepal Standard Time","Asia/Kathmandu"},
                {"Sri Lanka Standard Time","Asia/Colombo"},{"SE Asia Standard Time","Asia/Bangkok"},
                {"Singapore Standard Time","Asia/Singapore"},{"China Standard Time","Asia/Shanghai"},
                {"Tokyo Standard Time","Asia/Tokyo"},{"Korea Standard Time","Asia/Seoul"},
                {"AUS Eastern Standard Time","Australia/Sydney"},{"New Zealand Standard Time","Pacific/Auckland"},
                {"South Africa Standard Time","Africa/Johannesburg"},{"Egypt Standard Time","Africa/Cairo"},
                {"W. Central Africa Standard Time","Africa/Lagos"},{"Turkey Standard Time","Europe/Istanbul"},
                {"Israel Standard Time","Asia/Jerusalem"},{"Iran Standard Time","Asia/Tehran"},
                {"West Asia Standard Time","Asia/Tashkent"},{"Myanmar Standard Time","Asia/Yangon"},
                {"Taipei Standard Time","Asia/Taipei"},{"W. Australia Standard Time","Australia/Perth"},
                {"Canada Central Standard Time","America/Regina"},{"Atlantic Standard Time","America/Halifax"},
                {"Mexico Standard Time","America/Mexico_City"},{"Central Standard Time (Mexico)","America/Mexico_City"},
                {"SA Pacific Standard Time","America/Bogota"},{"Pacific SA Standard Time","America/Santiago"}
            };
            string iana;
            return map.TryGetValue(TimeZoneInfo.Local.Id, out iana) ? iana : "UTC";
        }
    }
}
