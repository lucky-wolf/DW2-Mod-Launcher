using System;
using System.Collections.Generic;
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

        /// <summary>
        /// Sets "hooksCoordinatedWith" (see LauncherMeta) to <paramref name="entries"/>, in the order given. An empty list removes the key, and
        /// writes nothing when there is no file. Throws if the existing file cannot be parsed, so a file with other content in it is never replaced.
        /// </summary>
        public static void SetCoordinatedWith(string modFolder, IList<string> entries)
        {
            const string field = "hooksCoordinatedWith";
            List<string> names = (entries ?? new List<string>()).Select(e => (e ?? "").Trim()).Where(e => e.Length > 0).ToList();
            string path = Path.Combine(modFolder, FileName);
            if (names.Count == 0 && !File.Exists(path)) return;
            JsonObject root = Load(path);
            string key = root.Select(kv => kv.Key).FirstOrDefault(k => k.Equals(field, StringComparison.OrdinalIgnoreCase));
            if (key != null) root.Remove(key);
            if (names.Count > 0)
            {
                JsonArray array = new JsonArray();
                foreach (string name in names) array.Add(name);
                root[field] = array;
            }
            Save(path, root);
        }

        private static void Stamp(string modFolder, string field, string value, bool overwrite)
        {
            string name = (value ?? "").Trim();
            if (name.Length == 0) return;
            string path = Path.Combine(modFolder, FileName);
            JsonObject root = Load(path);
            string key = root.Select(kv => kv.Key).FirstOrDefault(k => k.Equals(field, StringComparison.OrdinalIgnoreCase));
            if (key != null)
            {
                if (!overwrite && !string.IsNullOrWhiteSpace(root[key]?.ToString())) return;
                root.Remove(key);
            }
            root[field] = name;
            Save(path, root);
        }

        /// <summary>The file's JSON object, or an empty one when there is no file. Throws when the file is not a JSON object.</summary>
        private static JsonObject Load(string path)
        {
            if (!File.Exists(path)) return new JsonObject();
            JsonNode node = JsonNode.Parse(File.ReadAllText(path, Encoding.UTF8), null, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            return node as JsonObject ?? throw new InvalidDataException(path + " is not a JSON object.");
        }

        private static void Save(string path, JsonObject root)
        {
            // LF always, whatever the platform: mod repos keep LF-only files, and a CRLF stamp would read as a change once the file
            // is copied back through one (the published-state comparison hashes the bytes).
            File.WriteAllText(path, root.ToJsonString(Indented).Replace("\r\n", "\n"), new UTF8Encoding(false));
        }
    }
}
