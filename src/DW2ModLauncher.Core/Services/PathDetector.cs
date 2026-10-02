using System.IO;
using DW2ModLauncher.Core.Models;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>Fills in game / Workshop / mods folders on a settings object from the local Steam install.</summary>
    public static class PathDetector
    {
        /// <summary>With overwrite false, only fills in values that are empty or point at a missing folder.</summary>
        public static void Detect(LauncherSettings settings, bool overwrite)
        {
            string game = SteamLocator.FindGameRoot(settings.GameRoot);
            if (!string.IsNullOrEmpty(game) && (overwrite || string.IsNullOrEmpty(settings.GameRoot) || !Directory.Exists(settings.GameRoot)))
                settings.GameRoot = game;

            string workshop = SteamLocator.FindWorkshopRoot(settings.GameRoot, settings.WorkshopRoot);
            if (!string.IsNullOrEmpty(workshop) && (overwrite || string.IsNullOrEmpty(settings.WorkshopRoot) || !Directory.Exists(settings.WorkshopRoot)))
                settings.WorkshopRoot = workshop;

            if (SteamLocator.IsGameRoot(settings.GameRoot))
            {
                string dw2Mods = Path.Combine(settings.GameRoot, "mods");
                if (overwrite || string.IsNullOrEmpty(settings.ManagedModsRoot) || !Directory.Exists(settings.ManagedModsRoot))
                    settings.ManagedModsRoot = dw2Mods;
            }
        }
    }
}
