using System;
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

            if (OperatingSystem.IsWindows())
            {
                for (char d = 'C'; d <= 'Z'; d++)
                {
                    string[] bases = new string[] { d + @":\Steam", d + @":\steam", d + @":\SteamLibrary" };
                    foreach (string b in bases)
                    {
                        string p = Path.Combine(b, "steamapps", "common", "Distant Worlds 2");
                        if (IsGameRoot(p)) return p;
                    }
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

            if (OperatingSystem.IsWindows())
            {
                for (char d = 'C'; d <= 'Z'; d++)
                {
                    string[] bases = new string[] { d + @":\Steam", d + @":\steam", d + @":\SteamLibrary" };
                    foreach (string b in bases)
                    {
                        string p = Path.Combine(b, "steamapps", "workshop", "content", AppId);
                        if (Directory.Exists(p)) return p;
                    }
                }
            }
            return "";
        }

        public static List<string> GetSteamLibraries()
        {
            List<string> libs = new List<string>();
            foreach (string root in FindSteamRoots()) AddUnique(libs, root);

            List<string> initial = new List<string>(libs);
            foreach (string root in initial)
            {
                string vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
                try
                {
                    if (!File.Exists(vdf)) continue;
                    foreach (string p in ParseLibraryPaths(File.ReadAllText(vdf)))
                    {
                        if (Directory.Exists(p)) AddUnique(libs, p);
                    }
                }
                catch { }
            }
            return libs;
        }

        internal static List<string> ParseLibraryPaths(string vdfText)
        {
            List<string> paths = new List<string>();
            MatchCollection matches = Regex.Matches(vdfText ?? "", "\\\"path\\\"\\s+\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase);
            foreach (Match m in matches) paths.Add(m.Groups[1].Value.Replace("\\\\", "\\"));
            return paths;
        }

        // The same library is often reachable through several paths on Linux (~/.steam/steam is a
        // symlink to the real install), so compare by resolved path.
        private static void AddUnique(List<string> libs, string path)
        {
            StringComparer comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            string resolved = Resolve(path);
            if (!libs.Any(l => comparer.Equals(Resolve(l), resolved))) libs.Add(path);
        }

        private static string Resolve(string path)
        {
            try
            {
                FileSystemInfo target = new DirectoryInfo(path).ResolveLinkTarget(true);
                return (target == null ? Path.GetFullPath(path) : target.FullName).TrimEnd(Path.DirectorySeparatorChar);
            }
            catch { return path; }
        }

        /// <summary>The Steam install folder(s) on this machine: registry on Windows, well-known folders on Linux.</summary>
        public static List<string> FindSteamRoots()
        {
            List<string> roots = new List<string>();
            if (OperatingSystem.IsWindows())
            {
                string fromRegistry = ReadSteamPathFromRegistry();
                if (!string.IsNullOrEmpty(fromRegistry)) roots.Add(fromRegistry);
            }
            else if (OperatingSystem.IsLinux())
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
                if (string.IsNullOrEmpty(dataHome)) dataHome = Path.Combine(home, ".local", "share");
                string[] candidates = new string[]
                {
                    Path.Combine(home, ".steam", "steam"),
                    Path.Combine(dataHome, "Steam"),
                    Path.Combine(home, ".steam", "debian-installation"),
                    Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam"),
                    Path.Combine(home, "snap", "steam", "common", ".local", "share", "Steam")
                };
                foreach (string c in candidates)
                {
                    if (Directory.Exists(Path.Combine(c, "steamapps"))) AddUnique(roots, c);
                }
            }
            return roots;
        }

        public static string ReadSteamPathFromRegistry()
        {
            if (!OperatingSystem.IsWindows()) return "";
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
                    // Steam stores this with forward slashes ("c:/steam"); use the native form so every path built from it is consistent.
                    if (v != null && Directory.Exists(v.ToString())) return Path.GetFullPath(v.ToString());
                }
                catch { }
            }
            return "";
        }
    }
}
