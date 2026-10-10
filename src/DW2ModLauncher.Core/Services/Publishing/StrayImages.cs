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
            foreach (string file in Directory.EnumerateFiles(modFolder))
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
