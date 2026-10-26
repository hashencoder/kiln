using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace ServNook
{
    /// <summary>
    /// Loads the fonts embedded in ServNook.exe (Inter and JetBrains Mono, SIL Open Font License 1.1)
    /// so the panel looks the same on every PC without installing anything. Each font is registered
    /// twice: with GDI+ (PrivateFontCollection, for Font objects) and with GDI (AddFontMemResourceEx,
    /// so TextRenderer and standard controls can draw it). Falls back to Segoe UI / Consolas.
    /// </summary>
    public static class Fonts
    {
        static readonly PrivateFontCollection Pfc = new PrivateFontCollection();
        static readonly List<IntPtr> Pinned = new List<IntPtr>();
        static readonly Dictionary<string, FontFamily> Families = new Dictionary<string, FontFamily>(StringComparer.OrdinalIgnoreCase);
        static bool _loaded;

        [DllImport("gdi32.dll")]
        static extern IntPtr AddFontMemResourceEx(IntPtr pbFont, uint cbFont, IntPtr pdv, out uint pcFonts);

        static readonly string[] Files =
        {
            "Inter-Regular.ttf", "Inter-Medium.ttf", "Inter-SemiBold.ttf", "Inter-Bold.ttf", "JetBrainsMono-Regular.ttf"
        };

        static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            var asm = Assembly.GetExecutingAssembly();
            foreach (var f in Files)
            {
                try
                {
                    using (var s = asm.GetManifestResourceStream("fonts/" + f))
                    {
                        if (s == null) continue;
                        var data = new byte[s.Length];
                        int read = 0;
                        while (read < data.Length) { int n = s.Read(data, read, data.Length - read); if (n <= 0) break; read += n; }
                        // Memory must stay valid for the life of the process: allocate unmanaged and keep it.
                        IntPtr mem = Marshal.AllocCoTaskMem(data.Length);
                        Marshal.Copy(data, 0, mem, data.Length);
                        Pinned.Add(mem);
                        Pfc.AddMemoryFont(mem, data.Length);
                        if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                        {
                            uint count;
                            try { AddFontMemResourceEx(mem, (uint)data.Length, IntPtr.Zero, out count); } catch { }
                        }
                    }
                }
                catch { }
            }
            foreach (var fam in Pfc.Families) Families[fam.Name] = fam;
        }

        /// <summary>A font from an embedded family, e.g. Make("Inter SemiBold", 10.5f).</summary>
        public static Font Make(string family, float points, FontStyle style = FontStyle.Regular)
        {
            Load();
            FontFamily fam;
            try
            {
                if (Families.TryGetValue(family, out fam) && fam.IsStyleAvailable(style))
                {
                    var f = new Font(fam, points, style, GraphicsUnit.Point);
                    if (Usable(f)) return f;
                }
            }
            catch { }
            try
            {
                // Installed on the system (or a runtime without memory-font support): use it by name.
                var f = new Font(family, points, style, GraphicsUnit.Point);
                if (string.Equals(f.Name, family, StringComparison.OrdinalIgnoreCase) && Usable(f)) return f;
            }
            catch { }
            bool mono = family.StartsWith("JetBrains", StringComparison.OrdinalIgnoreCase);
            string fallback = mono ? "Consolas"
                            : family.EndsWith("SemiBold") || family.EndsWith("Medium") ? "Segoe UI Semibold" : "Segoe UI";
            return new Font(fallback, points, style, GraphicsUnit.Point);
        }

        static bool Usable(Font f)
        {
            try { return f.Height > 0; } catch { return false; }
        }

        /// <summary>CSS pixels at 96 dpi to points (1px = 0.75pt).</summary>
        public static float Px(float px) { return px * 0.75f; }
    }
}
