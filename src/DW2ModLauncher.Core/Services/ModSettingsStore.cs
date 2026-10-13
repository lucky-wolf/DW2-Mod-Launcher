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

        /// <summary>Reads existing stored values and fills in any schema key the file lacks with its default
        /// (no file counts as all keys lacking). A file that exists but is not a JSON object throws and is left
        /// untouched, so the user's values are never overwritten by defaults. If anything was filled in the file is
        /// rewritten at once, so what the editor shows is exactly what is on disk and only a user edit makes
        /// the values differ from the file.</summary>
        public static JsonObject GetOrCreateValues(ModInfo mod, ModSettingsSchema schema)
        {
            string path = GetSettingsPath(mod);
            JsonObject values = null;
            if (File.Exists(path))
            {
                try
                {
                    values = JsonNode.Parse(File.ReadAllText(path, Encoding.UTF8)) as JsonObject;
                }
                catch (JsonException ex)
                {
                    throw new InvalidDataException("The settings file for " + (mod?.DisplayName ?? mod?.Id) + " is not valid JSON, so it was left as it is: " + path + "\n" + ex.Message, ex);
                }
                if (values == null)
                    throw new InvalidDataException("The settings file for " + (mod?.DisplayName ?? mod?.Id) + " is not a JSON object, so it was left as it is: " + path);
            }

            bool changed = values == null;
            if (values == null) values = new JsonObject();
            foreach (ModSettingsField field in schema?.Fields ?? new System.Collections.Generic.List<ModSettingsField>())
            {
                if (string.IsNullOrWhiteSpace(field.Key) || values.ContainsKey(field.Key)) continue;
                values[field.Key] = ToJsonNode(field.Default);
                changed = true;
            }
            if (changed) SaveValues(mod, values);
            return values;
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
        public static JsonNode ToJsonNode(object value)
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
