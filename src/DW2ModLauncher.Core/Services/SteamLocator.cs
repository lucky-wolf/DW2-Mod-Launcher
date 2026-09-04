using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>
    /// Locates the DW2 game install and its Steam Workshop content folder, whether by
    /// Steam registry lookup or by scanning common Steam library drive letters.
    /// </summary>
    public static class SteamLocator
    {
        public const string AppId = "1531540";

        public static bool IsGameRoot(string p)
        {
            try { return !string.IsNullOrEmpty(p) && File.Exists(Path.Combine(p, "DistantWorlds2.exe")); }
            catch { return false; }
        }

        public static string FindGameRoot(string currentGameRoot)
        {
            if (IsGameRoot(currentGameRoot)) return currentGameRoot;

            foreach (string lib in GetSteamLibraries())
            {
                string p = Path.Combine(lib, "steamapps", "common", "Distant Worlds 2");
                if (IsGameRoot(p)) return p;
            }

            for (char d = 'C'; d <= 'Z'; d++)
            {
                string[] bases = new string[] { d + @":\Steam", d + @":\steam", d + @":\SteamLibrary" };
                foreach (string b in bases)
                {
                    string p = Path.Combine(b, "steamapps", "common", "Distant Worlds 2");
                    if (IsGameRoot(p)) return p;
                }
            }
            return "";
        }

        public static string FindWorkshopRoot(string gameRoot, string currentWorkshopRoot)
        {
            if (Directory.Exists(currentWorkshopRoot)) return currentWorkshopRoot;

            if (IsGameRoot(gameRoot))
            {
                try
                {
                    DirectoryInfo common = Directory.GetParent(gameRoot);
                    DirectoryInfo steamapps = common == null ? null : common.Parent;
                    if (steamapps != null)
                    {
                        string p = Path.Combine(steamapps.FullName, "workshop", "content", AppId);
                        if (Directory.Exists(p)) return p;
                    }
                }
                catch { }
            }

            foreach (string lib in GetSteamLibraries())
            {
                string p = Path.Combine(lib, "steamapps", "workshop", "content", AppId);
                if (Directory.Exists(p)) return p;
            }

            for (char d = 'C'; d <= 'Z'; d++)
            {
                string[] bases = new string[] { d + @":\Steam", d + @":\steam", d + @":\SteamLibrary" };
                foreach (string b in bases)
                {
                    string p = Path.Combine(b, "steamapps", "workshop", "content", AppId);
                    if (Directory.Exists(p)) return p;
                }
            }
            return "";
        }

        public static List<string> GetSteamLibraries()
        {
            List<string> libs = new List<string>();
            string steam = ReadSteamPathFromRegistry();
            if (!string.IsNullOrEmpty(steam)) libs.Add(steam);

            List<string> initial = new List<string>(libs);
            foreach (string root in initial)
            {
                string vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
                try
                {
                    if (!File.Exists(vdf)) continue;
                    string text = File.ReadAllText(vdf);
                    MatchCollection matches = Regex.Matches(text, "\\\"path\\\"\\s+\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase);
                    foreach (Match m in matches)
                    {
                        string p = m.Groups[1].Value.Replace("\\\\", "\\");
                        if (Directory.Exists(p) && !libs.Contains(p, System.StringComparer.OrdinalIgnoreCase)) libs.Add(p);
                    }
                }
                catch { }
            }
            return libs;
        }

        public static string ReadSteamPathFromRegistry()
        {
            string[] keys = new string[]
            {
                @"HKEY_CURRENT_USER\Software\Valve\Steam",
                @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam",
                @"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam"
            };
            foreach (string key in keys)
            {
                try
                {
                    object v = Registry.GetValue(key, "SteamPath", null);
                    if (v == null) v = Registry.GetValue(key, "InstallPath", null);
                    if (v != null && Directory.Exists(v.ToString())) return v.ToString();
                }
                catch { }
            }
            return "";
        }
    }
}
