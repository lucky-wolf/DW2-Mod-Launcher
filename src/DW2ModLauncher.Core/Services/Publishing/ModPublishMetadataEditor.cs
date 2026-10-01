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
    /// (displayName, description, previewImage, version, bundles - see
    /// docs/workshop-publish.md), preserving every other field already in the file (workshopId,
    /// launcher.*, a MOD's own custom fields such as GalCivMusic's "disableDefaultMusic", etc.)
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
            metadata.Description = GetString(root, "description");
            metadata.PreviewImage = GetString(root, "previewImage");
            metadata.Version = GetString(root, "version");
            metadata.Bundles = GetStringArray(root, "bundles");
            return metadata;
        }

        public static void Write(string modJsonPath, ModPublishMetadata metadata)
        {
            JsonObject root = JsonNode.Parse(File.ReadAllText(modJsonPath, Encoding.UTF8)) as JsonObject;
            if (root == null) throw new InvalidDataException("mod.json is not a JSON object.");

            SetString(root, "displayName", metadata.DisplayName);
            SetString(root, "description", metadata.Description);
            SetString(root, "previewImage", metadata.PreviewImage);
            SetString(root, "version", metadata.Version);

            JsonArray bundles = new JsonArray();
            foreach (string bundle in metadata.Bundles ?? new List<string>())
                if (!string.IsNullOrWhiteSpace(bundle)) bundles.Add(JsonValue.Create(bundle.Trim()));
            SetNode(root, "bundles", bundles);

            File.WriteAllText(modJsonPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
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

        private static void SetNode(JsonObject root, string canonicalKey, JsonNode value)
        {
            string existingKey = root.Select(kv => kv.Key).FirstOrDefault(k => k.Equals(canonicalKey, StringComparison.OrdinalIgnoreCase));
            if (existingKey != null) root.Remove(existingKey);
            root[canonicalKey] = value;
        }
    }
}
