using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DW2ModLauncher.Core.Services.Publishing
{
    /// <summary>
    /// Finds image files lying loose in the top level of a mod folder that nothing uses: the only image a mod refers to from there is its
    /// preview image, yet the whole folder is uploaded to Steam, so leftovers (the oversized original of a resized preview, a replaced
    /// cover, a screenshot dropped in) are shipped to every subscriber for no purpose. Images in subfolders are the mod's own content
    /// (textures, icons) and are never touched.
    /// </summary>
    public static class StrayImages
    {
        private static readonly string[] Extensions = { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp" };

        /// <summary>The loose image file names (not paths) in <paramref name="modFolder"/> other than <paramref name="previewImage"/>, sorted.</summary>
        public static List<string> Find(string modFolder, string previewImage)
        {
            List<string> found = new List<string>();
            if (string.IsNullOrWhiteSpace(modFolder) || !Directory.Exists(modFolder)) return found;
            string preview = (previewImage ?? "").Trim().Replace('\\', '/');
            // A preview that names a file which isn't there (renamed or deleted by hand) leaves no way to tell what the mod means to
            // use, and the image beside it may be the very one it meant: report nothing rather than offer to delete it.
            // Names are compared case-insensitively below (the game and Steam run on Windows), so the existence check must be too:
            // File.Exists is case-sensitive on Linux.
            List<string> files = Directory.EnumerateFiles(modFolder).ToList();
            bool previewExists = preview.Contains('/')
                ? File.Exists(Path.Combine(modFolder, preview.Replace('/', Path.DirectorySeparatorChar)))
                : files.Any(f => string.Equals(Path.GetFileName(f), preview, StringComparison.OrdinalIgnoreCase));
            if (preview.Length > 0 && !previewExists) return found;
            foreach (string file in files)
            {
                string name = Path.GetFileName(file);
                if (!Extensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase)) continue;
                if (string.Equals(name, preview, StringComparison.OrdinalIgnoreCase)) continue;
                found.Add(name);
            }
            found.Sort(StringComparer.OrdinalIgnoreCase);
            return found;
        }
    }
}
