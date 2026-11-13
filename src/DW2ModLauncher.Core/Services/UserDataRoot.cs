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
        /// <summary>Why moving the old settings out of the install folder failed, or null.</summary>
        public static string MigrationError { get; private set; }

        /// <summary>The command-line switch that moves the whole data root: <c>--data-root &lt;folder&gt;</c>. For tools and testers, not players.</summary>
        public const string Flag = "--data-root";

        /// <summary>The folder given with <see cref="Flag"/>, or null for the normal per-user folder.</summary>
        public static string Override { get; private set; }

        /// <summary>
        /// Takes <see cref="Flag"/> and its folder out of the command line and returns the remaining arguments. With the switch, settings, profiles,
        /// per-mod settings, the log and the single-instance lock all live in that folder, completely apart from the normal user's, so a second
        /// launcher (or a helper such as --prepare-launch) never reads or overwrites the owner's settings.
        /// </summary>
        public static string[] ApplyCommandLine(string[] args)
        {
            System.Collections.Generic.List<string> rest = new System.Collections.Generic.List<string>();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] != Flag) { rest.Add(args[i]); continue; }
                if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]))
                    throw new ArgumentException(Flag + " needs a folder.");
                Override = Path.GetFullPath(args[++i]);
            }
            return rest.ToArray();
        }

        public static string Get()
        {
            if (Override != null)
            {
                Directory.CreateDirectory(Override);
                return Override;
            }
            // SpecialFolderOption.Create: on Unix the default returns "" when ~/.config (or $XDG_CONFIG_HOME) doesn't
            // exist yet, which would turn this into a relative path in the working directory.
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create);
            string root = Path.Combine(appData, "DW2ModLauncher");
            Directory.CreateDirectory(root);
            return root;
        }

        /// <summary>
        /// Where data that can be fetched again lives (the catalog's previews, downloads and saved index): the per-user local folder
        /// (<c>%LocalAppData%\DW2ModLauncher</c>, <c>~/.local/share/DW2ModLauncher</c>), which unlike <see cref="Get"/> (the roaming folder, for
        /// settings) is never roamed or backed up. With <see cref="Flag"/> it is the <c>Cache</c> folder inside the moved root, so a test copy stays apart.
        /// </summary>
        public static string GetCache()
        {
            if (Override != null)
            {
                string moved = Path.Combine(Override, "Cache");
                Directory.CreateDirectory(moved);
                return moved;
            }
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create);
            string root = Path.Combine(local, "DW2ModLauncher");
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
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Can't use Logger (it locates its file through this method); the launcher shows this once its window is up.
                MigrationError = "Your old launcher settings or profiles could not be moved from " + installDir + " to " + root + ", so they are not in use.\n" + ex.Message;
            }
            return root;
        }
    }
}
