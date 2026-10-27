using System;
using System.Diagnostics;
using System.Linq;

namespace DW2ModLauncher.Core.Services
{
    public static class GameProcess
    {
        /// <summary>
        /// Whether the game installed in <paramref name="gameRoot"/> is running. Another install of the game (a test copy) running at the same
        /// time does not count: it has its own mods folder and loads none of this install's files. With no game folder configured nothing
        /// can be running from it. On Windows a game process counts when its exe lies inside the folder, or when Windows will not say where it is;
        /// under Wine/Proton the exe path is not the game's, so any game process counts.
        /// </summary>
        public static bool IsRunning(string gameRoot)
        {
            if (string.IsNullOrWhiteSpace(gameRoot)) return false;
            try { return Process.GetProcesses().Any(p => IsOfInstall(p, gameRoot)); }
            catch { return false; }
        }

        /// <summary>Kills every running process of the game installed in <paramref name="gameRoot"/> (the whole tree, so Wine helpers go too). Returns true if any was found.</summary>
        public static bool Kill(string gameRoot)
        {
            if (string.IsNullOrWhiteSpace(gameRoot)) return false;
            bool found = false;
            try
            {
                foreach (Process p in Process.GetProcesses())
                {
                    using (p)
                    {
                        if (!IsOfInstall(p, gameRoot)) continue;
                        found = true;
                        try { p.Kill(true); }
                        catch { }
                    }
                }
            }
            catch { }
            return found;
        }

        // Under Wine/Proton the process name is the .exe name cut to Linux's 15-character limit
        // ("DistantWorlds2." or similar), so match on the prefix rather than the full name.
        internal static bool IsGameProcessName(string name)
        {
            return name != null && name.StartsWith("DistantWorlds2", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsOfInstall(Process p, string gameRoot)
        {
            if (!IsGameProcessName(SafeName(p))) return false;
            if (!OperatingSystem.IsWindows()) return true;
            string exe;
            try { exe = p.MainModule?.FileName; }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException) { return true; }
            return exe == null || IsInside(gameRoot, exe);
        }

        /// <summary>Whether <paramref name="path"/> lies inside <paramref name="root"/> (the folder itself with a separator after it, so "Game - Copy" is not inside "Game").</summary>
        internal static bool IsInside(string root, string path)
        {
            string folder = System.IO.Path.GetFullPath(root).TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
            return System.IO.Path.GetFullPath(path).StartsWith(folder, StringComparison.OrdinalIgnoreCase);
        }

        private static string SafeName(Process p)
        {
            try { return p.ProcessName; }
            catch { return null; }
        }
    }
}
