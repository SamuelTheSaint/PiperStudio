using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PiperStudio
{
    // ================================================================== Persian interface polish
    // The free Vazirmatn font (SIL Open Font License, by Saber Rastikerdar) is built into the exe and used
    // for the whole interface in Persian; the main window is laid out right-to-left.
    public static class PersianUi
    {
        [DllImport("gdi32.dll")] static extern IntPtr AddFontMemResourceEx(IntPtr pbFont, uint cbFont, IntPtr pdv, ref uint pcFonts);
        static PrivateFontCollection pfc;
        static FontFamily family;
        static bool tried;
        static readonly HashSet<string> Replace = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Segoe UI", "Tahoma", "Microsoft Sans Serif", "MS Sans Serif", "Arial", "DejaVu Sans" };

        public static FontFamily Family
        {
            get
            {
                if (tried) return family;
                tried = true;
                try
                {
                    pfc = new PrivateFontCollection();
                    var asm = Assembly.GetExecutingAssembly();
                    foreach (var name in new[] { "Vazirmatn-Regular.ttf", "Vazirmatn-Bold.ttf" })
                        using (var st = asm.GetManifestResourceStream(name))
                        {
                            if (st == null) continue;
                            var b = new byte[st.Length]; int r = 0; while (r < b.Length) { int k = st.Read(b, r, b.Length - r); if (k <= 0) break; r += k; }
                            IntPtr p = Marshal.AllocCoTaskMem(b.Length);   // kept for the life of the program, as GDI+ requires
                            Marshal.Copy(b, 0, p, b.Length);
                            pfc.AddMemoryFont(p, b.Length);
                            // also register it for GDI, which most WinForms controls use to draw text
                            if (Environment.OSVersion.Platform == PlatformID.Win32NT) { uint n = 0; try { AddFontMemResourceEx(p, (uint)b.Length, IntPtr.Zero, ref n); } catch { } }
                        }
                    family = pfc.Families.FirstOrDefault();
                }
                catch { family = null; }
                return family;
            }
        }

        public static bool Enabled = true;   // Access tab: "Persian font (Vazirmatn)"

        // Swaps the interface fonts for Vazirmatn (same size and style). Fixed-width fonts (times, logs) stay.
        public static void Apply(Control root)
        {
            if (!Lang.Fa || !Enabled || Family == null || root == null) return;
            Walk(root);
        }

        static void Walk(Control c)
        {
            var strip = c as ToolStrip;
            if (strip != null) { strip.Font = Swap(strip.Font); foreach (ToolStripItem it in strip.Items) Item(it); }
            else c.Font = Swap(c.Font);
            foreach (Control ch in c.Controls) Walk(ch);
        }

        static void Item(ToolStripItem it)
        {
            it.Font = Swap(it.Font);
            var dd = it as ToolStripDropDownItem;
            if (dd != null) foreach (ToolStripItem s in dd.DropDownItems) Item(s);
        }

        static Font Swap(Font f)
        {
            if (f == null || !Replace.Contains(f.FontFamily.Name)) return f;
            try { return new Font(Family, f.Size, f.Style); } catch { return f; }
        }

        // Right-to-left layout for a whole window (menus on the right, tabs and columns mirrored).
        public static void Mirror(Form f, bool on)
        {
            if (!Lang.Fa) on = false;
            f.RightToLeft = on ? RightToLeft.Yes : RightToLeft.No;
            f.RightToLeftLayout = on;
        }
    }

    // ================================================================== "new version available"
    public static class UpdateCheck
    {
        public const string Repo = "SamuelTheSaint/PiperStudio";
        public static string ReleasesPage { get { return "https://github.com/" + Repo + "/releases/latest"; } }

        public static Version Parse(string tag)
        {
            var m = Regex.Match(tag ?? "", @"(\d+)(?:\.(\d+))?(?:\.(\d+))?");
            if (!m.Success) return null;
            return new Version(int.Parse(m.Groups[1].Value), m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0, m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0);
        }

        public static bool IsNewer(string latestTag, string current)
        {
            var a = Parse(latestTag); var b = Parse(current);
            return a != null && b != null && a > b;
        }

        // Returns (tag, page) of the latest GitHub release, or null if it can't be reached.
        public static Task<string[]> Latest(string apiBase = null)
        {
            return Task.Run(() =>
            {
                try
                {
                    try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
                    using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) })
                    {
                        http.DefaultRequestHeaders.UserAgent.ParseAdd("PiperStudio/" + Program.Version);
                        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
                        string url = (apiBase ?? "https://api.github.com") + "/repos/" + Repo + "/releases/latest";
                        var d = MiniJson.Parse(http.GetStringAsync(url).Result) as Dictionary<string, object>;
                        if (d == null || !d.ContainsKey("tag_name")) return null;
                        return new[] { Convert.ToString(d["tag_name"]), d.ContainsKey("html_url") ? Convert.ToString(d["html_url"]) : ReleasesPage };
                    }
                }
                catch { return (string[])null; }
            });
        }
    }
}
