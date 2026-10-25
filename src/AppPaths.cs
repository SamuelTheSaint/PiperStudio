using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace PiperStudio
{
    // Where Piper Studio keeps its own files.
    // Normal mode: %APPDATA%\PiperStudio (works even when the program sits in Program Files).
    // Portable mode: put an empty file called portable.txt next to PiperStudio.exe to keep everything in that folder.
    public static class AppPaths
    {
        public static readonly string AppDir = AppDomain.CurrentDomain.BaseDirectory;
        static string data;

        public static bool Portable { get { return File.Exists(Path.Combine(AppDir, "portable.txt")); } }

        public static string DataDir
        {
            get
            {
                if (data == null)
                {
                    data = Portable ? AppDir : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PiperStudio");
                    try { Directory.CreateDirectory(data); } catch { }
                }
                return data;
            }
        }

        public static string In(string name) { return Path.Combine(DataDir, name); }

        public static string DefaultExportDir
        {
            get
            {
                string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (string.IsNullOrEmpty(docs)) docs = DataDir;
                return Path.Combine(docs, "Piper Studio");
            }
        }

        // A folder the user can drop voices into without admin rights.
        public static string VoicesDir
        {
            get
            {
                string local = Path.Combine(AppDir, "voices");
                if (Directory.Exists(local) || CanWrite(AppDir)) return local;
                return Path.Combine(DataDir, "voices");
            }
        }

        public static bool CanWrite(string dir)
        {
            try
            {
                Directory.CreateDirectory(dir);
                string probe = Path.Combine(dir, ".piperstudio_write_test_" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(probe, "x");
                File.Delete(probe);
                return true;
            }
            catch { return false; }
        }

        public static readonly string[] SettingsFiles = { "PiperStudio.ini", "replacements.txt", "PiperStudio_lasttext.txt" };

        // Moves settings written by older versions (next to the exe) into the data folder.
        // Returns the list of moved files (empty if nothing was moved).
        public static List<string> MigrateOldSettings(string fromDir, string toDir)
        {
            var moved = new List<string>();
            try
            {
                if (string.Equals(Path.GetFullPath(fromDir).TrimEnd('\\', '/'), Path.GetFullPath(toDir).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)) return moved;
                if (!File.Exists(Path.Combine(fromDir, "PiperStudio.ini"))) return moved;
                if (File.Exists(Path.Combine(toDir, "PiperStudio.ini"))) return moved;   // already migrated earlier
                Directory.CreateDirectory(toDir);
                foreach (var f in SettingsFiles)
                {
                    string src = Path.Combine(fromDir, f), dst = Path.Combine(toDir, f);
                    if (!File.Exists(src)) continue;
                    File.Copy(src, dst, false);
                    moved.Add(f);
                }
                // remove the old copies (the old .ini may hold the bot token in plain text)
                foreach (var f in moved) try { File.Delete(Path.Combine(fromDir, f)); } catch { }
            }
            catch { }
            return moved;
        }
    }

    // Encrypts secrets (the bot token) with Windows DPAPI: only the same Windows user on the same PC can read them.
    public static class Secret
    {
        static readonly byte[] Entropy = Encoding.UTF8.GetBytes("PiperStudio.secret.v1");

        public static string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return "";
            try
            {
                var enc = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser);
                return "dpapi:" + Convert.ToBase64String(enc);
            }
            catch
            {
                // DPAPI is always there on Windows; this is only for other systems.
                return "b64:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(plain));
            }
        }

        public static string Unprotect(string stored)
        {
            if (string.IsNullOrEmpty(stored)) return "";
            try
            {
                if (stored.StartsWith("dpapi:"))
                    return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(stored.Substring(6)), Entropy, DataProtectionScope.CurrentUser));
                if (stored.StartsWith("b64:"))
                    return Encoding.UTF8.GetString(Convert.FromBase64String(stored.Substring(4)));
            }
            catch { return ""; }
            return stored;   // old versions stored it as plain text
        }

        public static bool IsEncrypted(string stored) { return stored != null && stored.StartsWith("dpapi:"); }
    }
}
