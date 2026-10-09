using System;

namespace DW2ModLauncher.Loader
{
    // The loader's own features (XML patching, fonts, text files, title lookup) report here when something is wrong, so it shows in the
    // in-game status line instead of only in a log file. Nothing is reported while everything works, so the row stays away unless needed.
    internal static class HookStatus
    {
        public const string Id = "DW2ModLauncher.Loader";

        public static void Failed(string key, string text, string logFile) => Set(key, text, StatusLevel.Error, logFile);

        public static void Warn(string key, string text, string logFile) => Set(key, text, StatusLevel.Warn, logFile);

        private static void Set(string key, string text, StatusLevel level, string logFile)
        {
            try
            {
                ModStatus.Registry.Register(Id, "Mod Launcher (loader)", null, null);
                ModStatus.Registry.Detail(Id, key, text, level);
                if (!string.IsNullOrEmpty(logFile)) ModStatus.Registry.SetLog(Id, logFile);
            }
            catch (Exception)
            {
                // a status report must never take the game down
            }
        }
    }
}
