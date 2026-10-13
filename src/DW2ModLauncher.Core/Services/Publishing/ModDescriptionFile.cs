using System;
using System.IO;
using System.Linq;
using System.Text;
using DW2ModLauncher.Core.Models;

namespace DW2ModLauncher.Core.Services.Publishing
{
    /// <summary>
    /// The long (Steam page) description of a mod, which lives in a text file in the mod's content root: the file mod.json's
    /// "descriptionFile" already names if there is one, otherwise "description.bbcode". mod.json's older "description" text is only
    /// read to seed that file the first time.
    /// </summary>
    public static class ModDescriptionFile
    {
        public const string FileName = "description.bbcode";
        /// <summary>The extension a new description file gets unless the author chose another one in the launcher settings.</summary>
        public const string DefaultExtension = ".bbcode";
        /// <summary>The earlier default name. A mod that has this file (and no description.bbcode, and names no file of its own) keeps using it.</summary>
        public const string LegacyFileName = "description.txt";

        /// <summary>
        /// The mod-relative name of the description file: the mod's own "descriptionFile" when it names a path inside the mod
        /// folder; otherwise <see cref="LegacyFileName"/> if that is what the mod has (and no <see cref="FileName"/>); otherwise <see cref="FileName"/>.
        /// </summary>
        public static string NameFor(string contentRoot, string descriptionFile, string defaultExtension = null)
        {
            if (string.IsNullOrWhiteSpace(contentRoot)) return PreferredName(defaultExtension);
            if (string.IsNullOrWhiteSpace(descriptionFile)) return DefaultFor(contentRoot, defaultExtension);
            try
            {
                string name = descriptionFile.Trim().Replace('\\', '/');
                string fullRoot = Path.GetFullPath(contentRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string full = Path.GetFullPath(Path.Combine(contentRoot, name.Replace('/', Path.DirectorySeparatorChar)));
                StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                return full.StartsWith(fullRoot, comparison) && !Directory.Exists(full) ? name : DefaultFor(contentRoot, defaultExtension);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is NotSupportedException)
            {
                Diagnostics.Logger.LogException("Resolve description file name: " + descriptionFile, ex);
                return DefaultFor(contentRoot, defaultExtension);
            }
        }

        /// <summary>
        /// A mod that names no file of its own uses the first of "description" + the preferred extension, <see cref="FileName"/> and
        /// <see cref="LegacyFileName"/> that exists, so an existing file is always honored; with none, the preferred name is what gets created.
        /// </summary>
        private static string DefaultFor(string contentRoot, string extension)
        {
            string preferred = PreferredName(extension);
            foreach (string candidate in new[] { preferred, FileName, LegacyFileName })
                if (File.Exists(PathFor(contentRoot, candidate))) return candidate;
            return preferred;
        }

        public static string PreferredName(string extension)
        {
            return "description" + NormalizeExtension(extension);
        }

        /// <summary>
        /// The extension as it is stored and used: trimmed, always starting with one dot, with anything a file name can't hold removed.
        /// Blank (or nothing usable) gives <see cref="DefaultExtension"/>.
        /// </summary>
        public static string NormalizeExtension(string extension)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            string cleaned = new string((extension ?? "").Where(c => !char.IsWhiteSpace(c) && Array.IndexOf(invalid, c) < 0).ToArray()).Trim('.');
            return cleaned.Length == 0 ? DefaultExtension : "." + cleaned;
        }

        public static string PathFor(string contentRoot, string fileName)
        {
            return Path.Combine(contentRoot, fileName.Replace('/', Path.DirectorySeparatorChar));
        }

        /// <summary>The raw text of the description file, or null when it does not exist (or cannot be read right now).</summary>
        public static string ReadFile(string contentRoot, string fileName)
        {
            if (string.IsNullOrWhiteSpace(contentRoot)) return null;
            try
            {
                string path = PathFor(contentRoot, fileName);
                return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;
            }
            catch (IOException ex)
            {
                Diagnostics.Logger.LogException("Read description file: " + fileName, ex);
                return null;
            }
        }

        /// <summary>
        /// The description to edit/publish: the description file if it exists (it wins over any legacy mod.json "description"),
        /// otherwise the legacy mod.json "description", otherwise "".
        /// </summary>
        public static string Load(string contentRoot, ModPublishMetadata metadata, string defaultExtension = null)
        {
            string fromFile = ReadFile(contentRoot, NameFor(contentRoot, metadata?.DescriptionFile, defaultExtension));
            if (fromFile != null) return fromFile;
            return metadata != null && !string.IsNullOrWhiteSpace(metadata.Description) ? metadata.Description : "";
        }

        /// <summary>Whether two descriptions are the same text, ignoring line-ending style and surrounding whitespace (Steam normalizes both).</summary>
        public static bool SameText(string a, string b)
        {
            return Normalize(a) == Normalize(b);
        }

        private static string Normalize(string text)
        {
            return (text ?? "").Replace("\r\n", "\n").Trim();
        }

        /// <summary>Writes the description file (UTF-8, no BOM, LF). Leaves it alone when it already holds exactly this text.</summary>
        public static void Save(string contentRoot, string fileName, string text)
        {
            string normalized = (text ?? "").Replace("\r\n", "\n");
            if (ReadFile(contentRoot, fileName) == normalized) return;
            string path = PathFor(contentRoot, fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, normalized, new UTF8Encoding(false));
        }
    }
}
