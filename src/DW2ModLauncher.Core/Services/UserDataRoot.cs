using System;
using System.IO;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>
    /// Resolves the launcher's per-user AppData folder, used for data that must survive both
    /// Steam Workshop re-syncing a mod's own folder and the launcher's own install folder being
    /// replaced/re-extracted on update. Also holds launcher_settings.json, Profiles\ and the log
    /// (see GetLauncherDataRoot).
    /// </summary>
    public static class UserDataRoot
    {
        public static string Get()
        {
            // SpecialFolderOption.Create: on Unix the default returns "" when ~/.config (or $XDG_CONFIG_HOME) doesn't
            // exist yet, which would turn this into a relative path in the working directory.
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create);
            string root = Path.Combine(appData, "DW2ModLauncher");
            Directory.CreateDirectory(root);
            return root;
        }

        /// <summary>
        /// Where launcher_settings.json, Profiles\, WorkshopBackups\ and the log live: the per-user config
        /// folder on every OS, since the install folder (or an AppImage/package location) is not a place to
        /// write user data. Anything left next to the binary by an older version is moved over once.
        /// </summary>
        public static string GetLauncherDataRoot(string installDir)
        {
            string root = Get();
            try
            {
                string oldSettings = Path.Combine(installDir, "launcher_settings.json");
                string newSettings = Path.Combine(root, "launcher_settings.json");
                if (File.Exists(oldSettings) && !File.Exists(newSettings))
                    File.Move(oldSettings, newSettings);

                string oldProfiles = Path.Combine(installDir, "Profiles");
                string newProfiles = Path.Combine(root, "Profiles");
                if (Directory.Exists(oldProfiles) && !Directory.Exists(newProfiles))
                    Directory.Move(oldProfiles, newProfiles);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
            return root;
        }
    }
}
