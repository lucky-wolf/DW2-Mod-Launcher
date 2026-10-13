using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>Writes fields of a mod's dw2modlauncher.json, keeping everything else in the file as it is.</summary>
    public static class LauncherMetaWriter
    {
        public const string FileName = "dw2modlauncher.json";

        private static readonly JsonSerializerOptions Indented = new JsonSerializerOptions { WriteIndented = true };

        /// <summary>
        /// Records <paramref name="author"/> in the mod folder's dw2modlauncher.json (creating the file if there is none). With
        /// <paramref name="overwrite"/> false an author the file already names is kept. Does nothing for a blank author.
        /// Throws if the existing file cannot be parsed, so a file with other content in it is never replaced.
        /// </summary>
        public static void StampAuthor(string modFolder, string author, bool overwrite)
        {
            Stamp(modFolder, "author", author, overwrite);
        }

        /// <summary>Records the language code of a translation mod (see LauncherMeta.language); the same rules as <see cref="StampAuthor"/>.</summary>
        public static void StampLanguage(string modFolder, string languageCode, bool overwrite)
        {
            Stamp(modFolder, "language", languageCode, overwrite);
        }

        private static void Stamp(string modFolder, string field, string value, bool overwrite)
        {
            string name = (value ?? "").Trim();
            if (name.Length == 0) return;
            string path = Path.Combine(modFolder, FileName);
            JsonObject root = new JsonObject();
            if (File.Exists(path))
            {
                JsonNode node = JsonNode.Parse(File.ReadAllText(path, Encoding.UTF8), null, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                root = node as JsonObject ?? throw new InvalidDataException(path + " is not a JSON object.");
            }
            string key = root.Select(kv => kv.Key).FirstOrDefault(k => k.Equals(field, StringComparison.OrdinalIgnoreCase));
            if (key != null)
            {
                if (!overwrite && !string.IsNullOrWhiteSpace(root[key]?.ToString())) return;
                root.Remove(key);
            }
            root[field] = name;
            File.WriteAllText(path, root.ToJsonString(Indented), new UTF8Encoding(false));
        }
    }
}
