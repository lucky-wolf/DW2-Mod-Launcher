using System;
using System.Diagnostics;
using System.Linq;

namespace DW2ModLauncher.Core.Services
{
    public static class GameProcess
    {
        public static bool IsRunning()
        {
            try { return Process.GetProcesses().Any(p => IsGameProcessName(SafeName(p))); }
            catch { return false; }
        }

        // Under Wine/Proton the process name is the .exe name cut to Linux's 15-character limit
        // ("DistantWorlds2." or similar), so match on the prefix rather than the full name.
        internal static bool IsGameProcessName(string name)
        {
            return name != null && name.StartsWith("DistantWorlds2", StringComparison.OrdinalIgnoreCase);
        }

        private static string SafeName(Process p)
        {
            try { return p.ProcessName; }
            catch { return null; }
        }
    }
}
