using System;
using System.Linq;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>
    /// How a mod's music\ folder is used by the game, so the launcher can say so instead of reporting a file collision.
    /// The game asks for music by mood and intensity: it lists music\&lt;mood&gt;\&lt;intensity&gt; across the base game and every enabled mod, merges the
    /// lists (same-named files do not replace each other) and plays one. Only when nothing is found there anywhere does it try music\&lt;mood&gt;,
    /// then music itself. So a mod's tracks in the leveled folders always join the pool, and tracks placed higher up are used only when no enabled
    /// mod has music for the exact mood and intensity asked for.
    /// </summary>
    public static class ModMusic
    {
        private static readonly string[] AudioExtensions = { ".mp3", ".wav", ".ogg", ".flac", ".wma", ".m4a", ".aac" };

        /// <summary>
        /// Whether a path (relative to the mod, '/' or '\') is an audio file under music\, and if so whether it sits in a leveled folder
        /// (music\&lt;mood&gt;\&lt;intensity&gt;\file: the game lists a folder's top level only, so these are the only files it finds at that depth).
        /// </summary>
        public static bool IsMusicFile(string relativePath, out bool leveled)
        {
            leveled = false;
            if (string.IsNullOrWhiteSpace(relativePath)) return false;
            string[] parts = relativePath.Replace('\\', '/').TrimStart('/').ToLowerInvariant().Split('/');
            if (parts.Length < 2 || parts[0] != "music") return false;
            if (!AudioExtensions.Contains(System.IO.Path.GetExtension(parts[parts.Length - 1]))) return false;
            leveled = parts.Length == 4;
            return true;
        }
    }
}
