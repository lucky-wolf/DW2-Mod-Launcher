using System;
using System.IO;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>
    /// Decides which relative file paths inside a Mod folder are excluded from file-conflict detection.
    /// Only files the game actually loads from the data folder can collide (a Mod folder mirrors "data"):
    /// *.xml, *.atlas and music\*.mp3 anywhere below the root; dialog\*.txt and galactopedia\*.txt;
    /// and the root-level GameText.txt, Hints.txt and SystemNames.txt. Everything else is ignored.
    /// </summary>
    public static class ConflictRules
    {
        private static readonly string[] RootFiles = { "gametext.txt", "hints.txt", "systemnames.txt" };

        public static bool IsIgnored(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath)) return true;
            // Normalize to '/' (valid on every platform) and split on it ourselves: Path.GetFileName only
            // honors the current platform's separator, so Windows-style input would not split on Linux.
            string rel = relativePath.Replace('\\', '/').TrimStart('/').ToLowerInvariant();
            int slash = rel.LastIndexOf('/');
            string file = rel.Substring(slash + 1);
            string ext = Path.GetExtension(file);

            // Repo/editor/OS litter folders can hold anything, including .xml.
            if (rel.StartsWith(".git/") || rel.StartsWith(".vs/") || rel.StartsWith(".vscode/") || rel.StartsWith("__macosx/") ||
                rel.Contains("/.git/") || rel.Contains("/.vs/") || rel.Contains("/.vscode/") || rel.Contains("/__macosx/")) return true;

            if (ext == ".xml" || ext == ".atlas") return false;
            if (ext == ".mp3") return !rel.StartsWith("music/");
            if (ext == ".txt")
            {
                if (slash < 0) return Array.IndexOf(RootFiles, file) < 0;
                return !(rel.StartsWith("dialog/") || rel.StartsWith("galactopedia/"));
            }
            return true;
        }
    }
}
