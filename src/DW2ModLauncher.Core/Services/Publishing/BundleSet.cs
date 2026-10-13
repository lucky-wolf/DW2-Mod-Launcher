using System;
using System.Collections.Generic;
using System.IO;

namespace DW2ModLauncher.Core.Services.Publishing
{
    /// <summary>
    /// A mod.json bundle ("XLAssets.bundle") is only the table of contents; the content lives in sibling files named
    /// "XLAssets.&lt;hash&gt;.bundle" (one or many). The bundle's real size is the head plus all of those children.
    /// </summary>
    public static class BundleSet
    {
        /// <summary>
        /// The bundles in a mod folder, found the way the launcher finds DLLs: every "*.bundle" that is not itself a hashed part
        /// ("Name.&lt;hash&gt;.bundle" next to an existing "Name.bundle"). Mod-relative paths with '/', sorted.
        /// </summary>
        public static List<string> Detect(string contentFolder)
        {
            List<string> found = new List<string>();
            if (string.IsNullOrWhiteSpace(contentFolder) || !Directory.Exists(contentFolder)) return found;
            // An unreadable folder throws: "no bundles found" would make publishing drop the mod's bundles from mod.json.
            foreach (string file in Directory.EnumerateFiles(contentFolder, "*.bundle", SearchOption.AllDirectories))
                if (!IsPart(file)) found.Add(Path.GetRelativePath(contentFolder, file).Replace('\\', '/'));
            found.Sort(StringComparer.OrdinalIgnoreCase);
            return found;
        }

        private static bool IsPart(string file)
        {
            string name = Path.GetFileNameWithoutExtension(file);
            int dot = name.LastIndexOf('.');
            if (dot <= 0) return false;
            return File.Exists(Path.Combine(Path.GetDirectoryName(file), name.Substring(0, dot) + ".bundle"));
        }

        /// <summary>Total bytes of the head file and its hashed children, or null when the head file does not exist.</summary>
        public static long? TotalBytes(string contentFolder, string bundleName)
        {
            try
            {
                string head = Path.GetFullPath(Path.Combine(contentFolder, bundleName));
                if (!File.Exists(head)) return null;
                string dir = Path.GetDirectoryName(head);
                string stem = Path.GetFileNameWithoutExtension(head);
                long total = new FileInfo(head).Length;
                foreach (string file in Directory.EnumerateFiles(dir, stem + ".*.bundle"))
                {
                    // "<stem>.<hash>.bundle": exactly one dot-free segment between the stem and the extension.
                    string middle = Path.GetFileName(file).Substring(stem.Length + 1);
                    middle = middle.Substring(0, middle.Length - ".bundle".Length);
                    if (middle.Length > 0 && middle.IndexOf('.') < 0 && !string.Equals(file, head, StringComparison.OrdinalIgnoreCase))
                        total += new FileInfo(file).Length;
                }
                return total;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
