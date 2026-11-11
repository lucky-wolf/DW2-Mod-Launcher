using System.IO;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>
    /// The game's "last session crashed" marker. DW2 writes a file named SessionActive in its application-data folder
    /// (the install's data folder, beside Logs/SessionLog.txt) at startup and deletes it on a clean exit; a kill, hang or
    /// crash leaves it, and the next launch then shows a Yes/No box before the game loop starts. Deleting it before launch
    /// skips that box. Only call this while the game is not running.
    /// </summary>
    public static class CrashMarker
    {
        public const string FileName = "SessionActive";

        public static string PathFor(string gameRoot)
        {
            return Path.Combine(gameRoot ?? "", "data", FileName);
        }

        /// <summary>Deletes the marker if present; true when one was there. Throws on I/O failure so the caller can report it.</summary>
        public static bool Clear(string gameRoot)
        {
            string path = PathFor(gameRoot);
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }
    }
}
