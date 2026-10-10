using System;
using System.Text.RegularExpressions;
using DW2ModLauncher.Core.Models;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>A mod's floor on the launcher version (dw2modlauncher.json "minLauncherVersion") against the running launcher.</summary>
    public static class LauncherRequirement
    {
        /// <summary>True when the mod names a minimum launcher version and the running launcher is older than it.</summary>
        public static bool IsUnmet(ModInfo mod)
        {
            return IsUnmet(mod, AppVersion.Current);
        }

        public static bool IsUnmet(ModInfo mod, string currentVersion)
        {
            // Unparseable on either side means we can't tell, so don't nag: a typo in a mod must not block it, and an unversioned
            // build (tests, odd packaging) has nothing to compare.
            return mod != null && TryParse(mod.MinLauncherVersion, out Version required) && TryParse(currentVersion, out Version current) && current < required;
        }

        /// <summary>The numeric MAJOR.MINOR.PATCH of a version such as "v1.0.6", "1.0.6-dev" or "1.0.6+sha"; a dev build counts as its base version.</summary>
        public static bool TryParse(string text, out Version version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(text)) return false;
            Match m = Regex.Match(text.Trim(), @"^v?(\d+(?:\.\d+){1,3})");
            return m.Success && Version.TryParse(m.Groups[1].Value, out version);
        }
    }
}
