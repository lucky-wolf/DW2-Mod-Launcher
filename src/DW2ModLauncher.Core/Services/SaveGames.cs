using System.IO;
using System.Linq;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>Finds the save the game's own "Continue" would load: the most recently written *.DWGame in data/SavedGames.</summary>
    public static class SaveGames
    {
        /// <summary>The newest save's name without extension, or null when there is none.</summary>
        public static string LatestName(string gameRoot)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(gameRoot)) return null;
                string dir = Path.Combine(gameRoot, "data", "SavedGames");
                if (!Directory.Exists(dir)) return null;
                FileInfo newest = new DirectoryInfo(dir).EnumerateFiles("*.DWGame", SearchOption.TopDirectoryOnly)
                    .OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault();
                return newest == null ? null : Path.GetFileNameWithoutExtension(newest.Name);
            }
            catch (IOException) { return null; }
            catch (System.UnauthorizedAccessException) { return null; }
        }
    }
}
