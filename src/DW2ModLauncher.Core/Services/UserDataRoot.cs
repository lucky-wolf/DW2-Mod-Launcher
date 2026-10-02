using System;
using System.IO;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>
    /// Resolves the launcher's per-user AppData folder, used for data that must survive both
    /// Steam Workshop re-syncing a mod's own folder and the launcher's own install folder being
    /// replaced/re-extracted on update (unlike the existing appRoot-based
    /// launcher_settings.json/Profiles\, which this does not affect or migrate).
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
    }
}
