using DW2ModLauncher.Core.Models;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>The master Steam switch's first-run decision.</summary>
    public static class SteamSwitch
    {
        /// <summary>A never-decided switch (null) becomes "on exactly when a Steam install is found"; a decided one is left alone.</summary>
        public static void Resolve(LauncherSettings settings)
        {
            Resolve(settings, SteamLocator.FindSteamRoots().Count > 0);
        }

        internal static void Resolve(LauncherSettings settings, bool steamInstalled)
        {
            if (settings.SteamEnabled == null) settings.SteamEnabled = steamInstalled;
        }
    }
}
