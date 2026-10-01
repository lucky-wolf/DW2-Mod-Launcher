using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DW2ModLauncher.Core.Models;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>
    /// Reads/writes the canonical per-mod settings values (as opposed to ModSettingsSchemaReader,
    /// which reads the mod-authored description of those values). Stored under
    /// UserDataRoot()\ModSettings\&lt;mod token&gt;.json - never inside the mod's own folder, so
    /// nothing here is at risk from a Steam Workshop re-sync.
    /// </summary>
    public static class ModSettingsStore
    {
        private static readonly JsonSerializerOptions WriteOptions = new JsonSerializerOptions { WriteIndented = true };

        public static string GetSettingsPath(ModInfo mod)
        {
            string token = mod?.ActiveToken ?? mod?.Key ?? "unknown";
            foreach (char c in Path.GetInvalidFileNameChars()) token = token.Replace(c, '_');
            return Path.Combine(UserDataRoot.Get(), "ModSettings", token + ".json");
        }

        /// <summary>Reads existing stored values; if none exist and a schema is given, synthesizes
        /// defaults from it, persists them, and returns those instead.</summary>
        public static JsonObject GetOrCreateValues(ModInfo mod, ModSettingsSchema schema)
        {
            string path = GetSettingsPath(mod);
            if (File.Exists(path))
            {
                try
                {
                    JsonNode node = JsonNode.Parse(File.ReadAllText(path, Encoding.UTF8));
                    if (node is JsonObject existing) return existing;
                }
                catch { }
            }

            JsonObject defaults = new JsonObject();
            foreach (ModSettingsField field in schema?.Fields ?? new System.Collections.Generic.List<ModSettingsField>())
            {
                if (string.IsNullOrWhiteSpace(field.Key)) continue;
                defaults[field.Key] = ToJsonNode(field.Default);
            }
            SaveValues(mod, defaults);
            return defaults;
        }

        public static void SaveValues(ModInfo mod, JsonObject values)
        {
            string path = GetSettingsPath(mod);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, (values ?? new JsonObject()).ToJsonString(WriteOptions), new UTF8Encoding(false));
        }

        // ModSettingsField.Default is a loosely-typed `object`: when the schema comes from
        // JsonSerializer (the normal case) it arrives as a boxed JsonElement, but callers
        // constructing a schema directly (e.g. unit tests) may set plain CLR primitives instead.
        // JsonValue.Create(object) can't serialize either shape via its generic/reflection path
        // without a source-generated resolver, so both are normalized explicitly here.
        private static JsonNode ToJsonNode(object value)
        {
            if (value == null) return null;
            if (value is JsonElement element) return JsonNode.Parse(element.GetRawText());
            if (value is bool b) return JsonValue.Create(b);
            if (value is string s) return JsonValue.Create(s);
            if (value is int i) return JsonValue.Create(i);
            if (value is long l) return JsonValue.Create(l);
            if (value is double d) return JsonValue.Create(d);
            if (value is float f) return JsonValue.Create(f);
            return JsonValue.Create(value.ToString());
        }
    }
}
