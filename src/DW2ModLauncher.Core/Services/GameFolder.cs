using System.IO;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>What makes a folder a Distant Worlds 2 install, whichever store it came from.</summary>
    public static class GameFolder
    {
        public const string ExeName = "DistantWorlds2.exe";

        public static bool IsGameRoot(string path)
        {
            return !string.IsNullOrEmpty(path) && File.Exists(Path.Combine(path, ExeName));
        }
    }
}
