using System;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>
    /// Translates host paths into the form the game process sees. On Windows that is the path
    /// unchanged. On Linux the game runs under Proton/Wine, where the host filesystem root is the
    /// Z: drive, so "/home/u/mod.dll" must be handed to the game (CLI flags, loader manifest) as
    /// "Z:\home\u\mod.dll".
    /// </summary>
    public static class GamePaths
    {
        public static string ToGameVisiblePath(string hostPath)
        {
            return ToGameVisiblePath(hostPath, OperatingSystem.IsWindows());
        }

        internal static string ToGameVisiblePath(string hostPath, bool hostIsWindows)
        {
            if (hostIsWindows || string.IsNullOrEmpty(hostPath) || !hostPath.StartsWith("/")) return hostPath;
            return "Z:" + hostPath.Replace('/', '\\');
        }
    }
}
