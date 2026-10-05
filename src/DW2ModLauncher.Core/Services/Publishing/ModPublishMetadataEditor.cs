using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DW2ModLauncher.Core.Models;

namespace DW2ModLauncher.Core.Services.Publishing
{
    /// <summary>
    /// Reads/writes just the mod.json fields Steam Workshop publish itself cares about
    /// (displayName, shortDescription, descriptionFile, description, previewImage, version, bundles - see
    /// docs/workshop-publish.md), preserving every other field already in the file (workshopId,
    /// a Mod's own custom fields such as GalCivMusic's "disableDefaultMusic", etc.)
    /// exactly like ModJsonWorkshopIdWriter does for just the one field it owns.
    /// </summary>
    public static class ModPublishMetadataEditor
    {
        public static ModPublishMetadata Read(string modJsonPath)
        {
            ModPublishMetadata metadata = new ModPublishMetadata();
            if (string.IsNullOrWhiteSpace(modJsonPath) || !File.Exists(modJsonPath)) return metadata;
            JsonObject root = JsonNode.Parse(File.ReadAllText(modJsonPath, Encoding.UTF8)) as JsonObject;
            if (root == null) return metadata;

            metadata.DisplayName = GetString(root, "displayName");
            metadata.ShortDescription = GetString(root, "shortDescription");
            metadata.DescriptionFile = GetString(root, "descriptionFile");
            metadata.Description = GetString(root, "description");
            metadata.PreviewImage = GetString(root, "previewImage");
            metadata.Version = GetString(root, "version");
            metadata.Bundles = GetStringArray(root, "bundles");
            return metadata;
        }

        /// <summary>
        /// The text to push to the Steam page: the "description" if there is one, otherwise the contents of the
        /// "descriptionFile" (a path inside the mod folder). Null when neither yields any text.
        /// </summary>
        public static string ResolveSteamDescription(string contentRoot, ModPublishMetadata metadata)
        {
            if (!string.IsNullOrWhiteSpace(metadata.Description)) return metadata.Description;
            return ModScanner.ReadDescriptionFile(contentRoot, metadata.DescriptionFile);
        }

        /// <summary>Rewrites just the "version" key (used to undo the auto-bump after a publish that didn't happen).</summary>
        public static void WriteVersion(string modJsonPath, string version)
        {
            JsonObject root = JsonNode.Parse(File.ReadAllText(modJsonPath, Encoding.UTF8)) as JsonObject;
            if (root == null) throw new InvalidDataException("mod.json is not a JSON object.");
            SetString(root, "version", version);
            ModJsonFile.Save(modJsonPath, root);
        }

        public static void Write(string modJsonPath, ModPublishMetadata metadata)
        {
            JsonObject root = JsonNode.Parse(File.ReadAllText(modJsonPath, Encoding.UTF8)) as JsonObject;
            if (root == null) throw new InvalidDataException("mod.json is not a JSON object.");

            SetString(root, "displayName", metadata.DisplayName);
            // "description" is what gets pushed to the Steam page. A mod that doesn't have one (e.g. it uses descriptionFile and
            // keeps its Steam text by hand) must not gain an empty key, so a blank value removes the key instead.
            if (string.IsNullOrWhiteSpace(metadata.Description)) RemoveKey(root, "description");
            else SetString(root, "description", metadata.Description);
            // Same rule for the other optional description fields: blank means "not used", not an empty key.
            if (string.IsNullOrWhiteSpace(metadata.ShortDescription)) RemoveKey(root, "shortDescription");
            else SetString(root, "shortDescription", metadata.ShortDescription.Trim());
            if (string.IsNullOrWhiteSpace(metadata.DescriptionFile)) RemoveKey(root, "descriptionFile");
            else SetString(root, "descriptionFile", metadata.DescriptionFile.Trim());
            SetString(root, "previewImage", metadata.PreviewImage);
            SetString(root, "version", metadata.Version);

            JsonArray bundles = new JsonArray();
            foreach (string bundle in metadata.Bundles ?? new List<string>())
                if (!string.IsNullOrWhiteSpace(bundle)) bundles.Add(JsonValue.Create(bundle.Trim()));
            SetNode(root, "bundles", bundles);

            ModJsonFile.Save(modJsonPath, root);
        }

        private static string GetString(JsonObject root, string key)
        {
            foreach (KeyValuePair<string, JsonNode> kv in root)
                if (kv.Key.Equals(key, StringComparison.OrdinalIgnoreCase)) return kv.Value?.ToString() ?? "";
            return "";
        }

        private static List<string> GetStringArray(JsonObject root, string key)
        {
            foreach (KeyValuePair<string, JsonNode> kv in root)
            {
                if (!kv.Key.Equals(key, StringComparison.OrdinalIgnoreCase)) continue;
                JsonArray array = kv.Value as JsonArray;
                if (array == null) return new List<string>();
                return array.Select(item => item?.ToString() ?? "").Where(s => s.Length > 0).ToList();
            }
            return new List<string>();
        }

        // JsonObject doesn't dedupe by case, so a plain `root[key] = value` next to an
        // existing-but-differently-cased key (unlikely in practice - every real mod.json seen so
        // far uses exact lowerCamelCase - but not impossible) would leave both in the file. Remove
        // whatever's already there under any casing first, then set the canonical key.
        private static void SetString(JsonObject root, string canonicalKey, string value)
        {
            SetNode(root, canonicalKey, JsonValue.Create(value ?? ""));
        }

        private static void RemoveKey(JsonObject root, string key)
        {
            string existingKey = root.Select(kv => kv.Key).FirstOrDefault(k => k.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (existingKey != null) root.Remove(existingKey);
        }

        private static void SetNode(JsonObject root, string canonicalKey, JsonNode value)
        {
            string existingKey = root.Select(kv => kv.Key).FirstOrDefault(k => k.Equals(canonicalKey, StringComparison.OrdinalIgnoreCase));
            if (existingKey != null) root.Remove(existingKey);
            root[canonicalKey] = value;
        }
    }
}
