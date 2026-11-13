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

        /// <summary>
        /// Gives the mod its identity ("uid", see <see cref="ModUid"/>) if it has none, and returns the identity it has. An identity already there is kept; one that
        /// is not a UUID throws (the author wrote it, so it is not replaced). With <paramref name="replace"/> a new identity is written over any existing one
        /// (a deliberate fork). Throws if the existing file cannot be parsed, so a file with other content in it is never replaced.
        /// </summary>
        public static string StampUid(string modFolder, bool replace = false)
        {
            const string field = "uid";
            string path = Path.Combine(modFolder, FileName);
            JsonObject root = Load(path);
            string key = root.Select(kv => kv.Key).FirstOrDefault(k => k.Equals(field, StringComparison.OrdinalIgnoreCase));
            if (key != null)
            {
                string existing = root[key]?.ToString();
                if (!replace && !string.IsNullOrWhiteSpace(existing))
                    return ModUid.Normalize(existing) ?? throw new InvalidDataException(path + " has a \"uid\" (" + existing + ") that is not a UUID.");
                root.Remove(key);
            }
            string uid = ModUid.New();
            root[field] = uid;
            Save(path, root);
            return uid;
        }

        /// <summary>
        /// Sets "versionBump" (see LauncherMeta) to <paramref name="level"/>; "patch", the default, removes the key and writes nothing when there is no file.
        /// Throws if the existing file cannot be parsed, so a file with other content in it is never replaced.
        /// </summary>
        public static void SetVersionBump(string modFolder, string level)
        {
            const string field = "versionBump";
            string path = Path.Combine(modFolder, FileName);
            bool isDefault = string.Equals((level ?? "").Trim(), "patch", StringComparison.OrdinalIgnoreCase);
            if (isDefault && !File.Exists(path)) return;
            JsonObject root = Load(path);
            string key = root.Select(kv => kv.Key).FirstOrDefault(k => k.Equals(field, StringComparison.OrdinalIgnoreCase));
            if (key != null) root.Remove(key);
            if (!isDefault) root[field] = level.Trim().ToLowerInvariant();
            Save(path, root);
        }

        /// <summary>
        /// Sets "tags" (see LauncherMeta) to <paramref name="tags"/> written the way <see cref="Catalog.CatalogTags"/> says. No tags removes the key, and
        /// writes nothing when there is no file. Throws if the existing file cannot be parsed, so a file with other content in it is never replaced.
        /// </summary>
        public static void SetTags(string modFolder, IEnumerable<string> tags)
        {
            const string field = "tags";
            List<string> names = Catalog.CatalogTags.Normalize(tags);
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

        /// <summary>
        /// Sets "releaseRepo" (see LauncherMeta) to <paramref name="repo"/> (OWNER/NAME); blank removes the key, and writes nothing when there is no file. A value
        /// that is not OWNER/NAME throws. Throws if the existing file cannot be parsed, so a file with other content in it is never replaced.
        /// </summary>
        public static void SetReleaseRepo(string modFolder, string repo)
        {
            const string field = "releaseRepo";
            string value = (repo ?? "").Trim();
            if (value.Length > 0 && !Publishing.GitHubReleaseHost.IsRepo(value)) throw new FormatException("The release repository \"" + value + "\" is not written OWNER/NAME.");
            string path = Path.Combine(modFolder, FileName);
            if (value.Length == 0 && !File.Exists(path)) return;
            JsonObject root = Load(path);
            string key = root.Select(kv => kv.Key).FirstOrDefault(k => k.Equals(field, StringComparison.OrdinalIgnoreCase));
            if (key != null) root.Remove(key);
            if (value.Length > 0) root[field] = value;
            Save(path, root);
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

        /// <summary>
        /// Sets "dependencies" to <paramref name="dependencies"/>, in the order given. Each entry is written with the fields it uses ("required" always;
        /// "steamLink", "minVersion", "loadAfter" and "loadBefore" only when set). An empty list removes the key, and writes nothing when there is no file.
        /// Throws if the existing file cannot be parsed, so a file with other content in it is never replaced.
        /// </summary>
        public static void SetDependencies(string modFolder, IList<Models.LauncherDependency> dependencies)
        {
            const string field = "dependencies";
            List<Models.LauncherDependency> list = (dependencies ?? new List<Models.LauncherDependency>()).Where(d => d != null).ToList();
            string path = Path.Combine(modFolder, FileName);
            if (list.Count == 0 && !File.Exists(path)) return;
            JsonObject root = Load(path);
            string key = root.Select(kv => kv.Key).FirstOrDefault(k => k.Equals(field, StringComparison.OrdinalIgnoreCase));
            // Keep the key where it was in the file; a new one goes after the existing fields.
            List<KeyValuePair<string, JsonNode>> items = root.Select(kv => new KeyValuePair<string, JsonNode>(kv.Key, kv.Value)).ToList();
            root.Clear();
            bool placed = false;
            JsonArray array = new JsonArray();
            foreach (Models.LauncherDependency dep in list) array.Add(DependencyNode(dep));
            foreach (KeyValuePair<string, JsonNode> item in items)
            {
                if (key != null && item.Key == key)
                {
                    if (list.Count > 0) root[field] = array;
                    placed = true;
                }
                else root[item.Key] = item.Value;
            }
            if (!placed && list.Count > 0) root[field] = array;
            Save(path, root);
        }

        private static JsonObject DependencyNode(Models.LauncherDependency dep)
        {
            JsonObject node = new JsonObject();
            if (!string.IsNullOrWhiteSpace(dep.mod)) node["mod"] = dep.mod.Trim();
            if (!string.IsNullOrWhiteSpace(dep.uid)) node["uid"] = dep.uid.Trim();
            if (dep.workshopId.HasValue) node["workshopId"] = dep.workshopId.Value;
            node["required"] = dep.required;
            if (dep.steamLink.HasValue) node["steamLink"] = dep.steamLink.Value;
            if (!string.IsNullOrWhiteSpace(dep.minVersion)) node["minVersion"] = dep.minVersion.Trim();
            if (dep.loadAfter) node["loadAfter"] = true;
            if (dep.loadBefore) node["loadBefore"] = true;
            return node;
        }

        /// <summary>
        /// Adds "uid" and/or "workshopId" to the "dependencies" entries named in <paramref name="idsByDependency"/> (index in the array to the ids to add; a null id is
        /// not added), placed right after the entry's "mod" (first when it has none), uid before workshopId. An id the entry already has is left alone, and no id is ever
        /// removed. Throws if the existing file cannot be parsed.
        /// </summary>
        public static void AddDependencyIds(string modFolder, IDictionary<int, (string Uid, long? WorkshopId)> idsByDependency)
        {
            string path = Path.Combine(modFolder, FileName);
            JsonObject root = Load(path);
            string key = root.Select(kv => kv.Key).FirstOrDefault(k => k.Equals("dependencies", StringComparison.OrdinalIgnoreCase));
            JsonArray array = key == null ? null : root[key] as JsonArray;
            if (array == null) throw new InvalidDataException(path + " has no \"dependencies\" array.");
            foreach (KeyValuePair<int, (string Uid, long? WorkshopId)> pair in idsByDependency)
            {
                JsonObject entry = pair.Key >= 0 && pair.Key < array.Count ? array[pair.Key] as JsonObject : null;
                if (entry == null) throw new InvalidDataException(path + " dependency " + pair.Key + " is not an object.");
                bool hasUid = entry.Any(kv => kv.Key.Equals("uid", StringComparison.OrdinalIgnoreCase));
                bool hasId = entry.Any(kv => kv.Key.Equals("workshopId", StringComparison.OrdinalIgnoreCase));
                List<KeyValuePair<string, JsonNode>> added = new List<KeyValuePair<string, JsonNode>>();
                if (!hasUid && !string.IsNullOrWhiteSpace(pair.Value.Uid)) added.Add(new KeyValuePair<string, JsonNode>("uid", pair.Value.Uid.Trim()));
                if (!hasId && pair.Value.WorkshopId.HasValue) added.Add(new KeyValuePair<string, JsonNode>("workshopId", pair.Value.WorkshopId.Value));
                if (added.Count == 0) continue;
                List<KeyValuePair<string, JsonNode>> items = entry.Select(kv => new KeyValuePair<string, JsonNode>(kv.Key, kv.Value)).ToList();
                entry.Clear();
                bool hasName = items.Any(i => i.Key.Equals("mod", StringComparison.OrdinalIgnoreCase));
                if (!hasName) foreach (KeyValuePair<string, JsonNode> a in added) entry[a.Key] = a.Value;
                foreach (KeyValuePair<string, JsonNode> item in items)
                {
                    entry[item.Key] = item.Value;
                    if (item.Key.Equals("mod", StringComparison.OrdinalIgnoreCase)) foreach (KeyValuePair<string, JsonNode> a in added) entry[a.Key] = a.Value;
                }
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
