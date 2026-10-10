using System;
using System.Collections.Generic;
using System.IO;

namespace DW2ModLauncher.Core.Services.Publishing
{
    /// <summary>
    /// The rules for shrinking a Workshop preview image to fit Steam's 1 MiB limit, without any imaging code: how big the file may be,
    /// which sizes to try and what to call the result. The encoder that uses them lives in the launcher UI project.
    /// </summary>
    public static class PreviewImagePlan
    {
        /// <summary>Steam rejects preview images over this many bytes.</summary>
        public const long SteamLimitBytes = 1024 * 1024;

        /// <summary>What a resized image aims for: a little under the limit, so a re-encode by another tool can't tip it over.</summary>
        public const long TargetBytes = 1000 * 1000;

        /// <summary>No resize goes below this on its longest side; a preview that small is not worth shipping, so the attempt gives up.</summary>
        public const int MinLongEdge = 320;

        /// <summary>Each step scales both sides by this much, so a non-square image keeps its shape (the pixel budget shrinks, not one side).</summary>
        private const double StepScale = 0.85;

        public static bool NeedsResize(long bytes)
        {
            return bytes > SteamLimitBytes;
        }

        /// <summary>
        /// The sizes to try, largest first: the original size (often just re-encoding is enough), then each step 15% smaller on both
        /// sides until the longest side would drop below <see cref="MinLongEdge"/>.
        /// </summary>
        public static List<KeyValuePair<int, int>> Sizes(int width, int height)
        {
            List<KeyValuePair<int, int>> sizes = new List<KeyValuePair<int, int>>();
            if (width <= 0 || height <= 0) return sizes;
            double scale = 1;
            while (true)
            {
                int w = Math.Max(1, (int)Math.Round(width * scale));
                int h = Math.Max(1, (int)Math.Round(height * scale));
                if (Math.Max(w, h) < MinLongEdge && sizes.Count > 0) break;
                sizes.Add(new KeyValuePair<int, int>(w, h));
                if (Math.Max(w, h) <= MinLongEdge) break;
                scale *= StepScale;
            }
            return sizes;
        }

        /// <summary>
        /// The file name for a name the author typed: the image's extension is always the one it was encoded as, so a typed ".png" /
        /// ".jpg" / ".jpeg" is replaced and anything else ("cover", "cover.v2") gets it appended. Null when the text is blank or is not a
        /// plain file name (path separators, characters the OS forbids).
        /// </summary>
        public static string FileNameFor(string typed, string extension)
        {
            string name = (typed ?? "").Trim();
            if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name == "." || name == "..") return null;
            string ext = Path.GetExtension(name);
            if (ext.Equals(".png", StringComparison.OrdinalIgnoreCase) || ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - ext.Length).TrimEnd();
            return name.Length == 0 ? null : name + extension;
        }

        /// <summary>
        /// A path in the mod folder for the resized copy: the source's name with the new extension, or "name (resized).ext",
        /// "name (resized 2).ext"... when that is taken. Never an existing file, so neither the original nor anything else is overwritten.
        /// </summary>
        public static string TargetPath(string modFolder, string sourceFile, string extension)
        {
            string name = Path.GetFileNameWithoutExtension(sourceFile);
            string target = Path.Combine(modFolder, name + extension);
            if (!File.Exists(target)) return target;
            target = Path.Combine(modFolder, name + " (resized)" + extension);
            for (int n = 2; File.Exists(target); n++)
                target = Path.Combine(modFolder, name + " (resized " + n + ")" + extension);
            return target;
        }
    }
}
