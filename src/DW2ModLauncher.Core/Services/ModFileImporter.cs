using System;
using System.IO;
using System.Linq;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>
    /// mod.json may only refer to files inside the mod's own folder (the launcher refuses a path that escapes it, and a Workshop
    /// upload only carries that folder). These helpers turn a file the author picked anywhere into such a mod-relative path,
    /// copying it in first when it lives elsewhere.
    /// </summary>
    public static class ModFileImporter
    {
        private static StringComparison PathComparison
        {
            get { return OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal; }
        }

        /// <summary>True when the file is in the mod folder or one of its subfolders.</summary>
        public static bool IsInside(string modFolder, string file)
        {
            string root = Path.GetFullPath(modFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return Path.GetFullPath(file).StartsWith(root, PathComparison);
        }

        /// <summary>The file's path relative to the mod folder, written with '/' so mod.json reads the same on every OS. The file must be inside.</summary>
        public static string RelativePath(string modFolder, string file)
        {
            return Path.GetRelativePath(modFolder, file).Replace('\\', '/');
        }

        /// <summary>
        /// Copies a file from outside the mod into the mod folder's top level and returns its mod-relative path. A file of the same
        /// name that already has identical contents is reused; one with different contents is never overwritten (the copy gets
        /// " (2)", " (3)"... before its extension).
        /// </summary>
        public static string CopyIntoMod(string modFolder, string sourceFile)
        {
            string name = Path.GetFileName(sourceFile);
            string target = Path.Combine(modFolder, name);
            for (int n = 2; File.Exists(target); n++)
            {
                if (File.ReadAllBytes(target).SequenceEqual(File.ReadAllBytes(sourceFile))) return RelativePath(modFolder, target);
                target = Path.Combine(modFolder, Path.GetFileNameWithoutExtension(name) + " (" + n + ")" + Path.GetExtension(name));
            }
            File.Copy(sourceFile, target);
            return RelativePath(modFolder, target);
        }
    }
}
