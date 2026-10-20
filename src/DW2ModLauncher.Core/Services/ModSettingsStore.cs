using System;
using System.IO;
using System.Linq;
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

        // A local mod with a Workshop id is stored under "local/<id>" (see ModInfo.PublishedLocalKey), so renaming its folder
        // keeps its settings; any other mod under its ActiveToken, as before.
        private static string TokenOf(ModInfo mod)
        {
            return mod?.PublishedLocalKey ?? mod?.ActiveToken ?? mod?.Key ?? "unknown";
        }

        private static string PathFor(string folder, string token)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) token = token.Replace(c, '_');
            return Path.Combine(UserDataRoot.Get(), folder, token + ".json");
        }

        public static string GetSettingsPath(ModInfo mod)
        {
            return PathFor("ModSettings", TokenOf(mod));
        }

        /// <summary>Moves the settings (and remembered defaults) stored under an older token of this mod - its folder name
        /// before it was renamed or before it had a Workshop id - to where they live now. Only when nothing is stored yet under
        /// the current token: values already there are never replaced. Returns whether anything moved.</summary>
        public static bool AdoptStoredUnder(ModInfo mod, string legacyToken)
        {
            if (string.IsNullOrWhiteSpace(legacyToken) || legacyToken == TokenOf(mod)) return false;
            bool moved = false;
            foreach (string folder in new[] { "ModSettings", "ModSettingsDefaults" })
            {
                string from = PathFor(folder, legacyToken);
                string to = PathFor(folder, TokenOf(mod));
                if (!File.Exists(from) || File.Exists(to)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(to));
                File.Move(from, to);
                moved = true;
            }
            return moved;
        }

        /// <summary>Reads existing stored values and fills in any schema key the file lacks with its default
        /// (no file counts as all keys lacking) and drops any stored key the schema no longer declares. A file that exists but is not a JSON object throws and is left
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
            var fields = schema?.Fields ?? new System.Collections.Generic.List<ModSettingsField>();
            foreach (ModSettingsField field in fields)
            {
                if (string.IsNullOrWhiteSpace(field.Key) || values.ContainsKey(field.Key)) continue;
                values[field.Key] = ToJsonNode(field.Default);
                changed = true;
            }
            // A key the schema no longer declares (the mod dropped or renamed the setting) is stale: remove it. Only when a
            // schema was given, so a read without one (null) never strips anything.
            if (schema != null)
            {
                var known = new System.Collections.Generic.HashSet<string>(fields.Select(f => f.Key).Where(k => !string.IsNullOrWhiteSpace(k)));
                foreach (string stale in values.Select(p => p.Key).Where(k => !known.Contains(k)).ToList())
                {
                    values.Remove(stale);
                    changed = true;
                }
            }
            if (schema != null && FollowChangedDefaults(mod, fields, values)) changed = true;
            if (changed) SaveValues(mod, values);
            return values;
        }

        /// <summary>Where the defaults a mod's schema declared at the last read are remembered, so a later read can tell
        /// "the user left this setting alone" (stored value still equals that old default) from "the user chose it".</summary>
        public static string GetDefaultsPath(ModInfo mod)
        {
            return PathFor("ModSettingsDefaults", TokenOf(mod));
        }

        /// <summary>A setting still holding the default the schema declared last time moves to the schema's new default;
        /// one the user changed keeps their value. With no remembered defaults (first read, or a mod seen before this
        /// existed) nothing moves - the current defaults just become the baseline. Returns whether any value changed.</summary>
        private static bool FollowChangedDefaults(ModInfo mod, System.Collections.Generic.List<ModSettingsField> fields, JsonObject values)
        {
            string path = GetDefaultsPath(mod);
            JsonObject remembered = null;
            if (File.Exists(path))
            {
                try
                {
                    remembered = JsonNode.Parse(File.ReadAllText(path, Encoding.UTF8)) as JsonObject;
                }
                catch (JsonException ex)
                {
                    throw new InvalidDataException("The remembered defaults for " + (mod?.DisplayName ?? mod?.Id) + " are not valid JSON, so they were left as they are: " + path + "\n" + ex.Message, ex);
                }
                if (remembered == null)
                    throw new InvalidDataException("The remembered defaults for " + (mod?.DisplayName ?? mod?.Id) + " are not a JSON object, so they were left as they are: " + path);
            }

            bool valuesChanged = false;
            JsonObject current = new JsonObject();
            foreach (ModSettingsField field in fields)
            {
                if (string.IsNullOrWhiteSpace(field.Key)) continue;
                JsonNode newDefault = ToJsonNode(field.Default);
                current[field.Key] = newDefault?.DeepClone();
                if (remembered == null || !remembered.TryGetPropertyValue(field.Key, out JsonNode oldDefault)) continue;
                if (SameValue(oldDefault, newDefault)) continue;
                if (values.TryGetPropertyValue(field.Key, out JsonNode stored) && SameValue(stored, oldDefault))
                {
                    values[field.Key] = newDefault?.DeepClone();
                    valuesChanged = true;
                }
            }

            if (remembered == null || !SameValue(remembered, current))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, current.ToJsonString(WriteOptions), new UTF8Encoding(false));
            }
            return valuesChanged;
        }

        // 1 and 1.0 are the same setting value even though their JSON text differs.
        private static bool SameValue(JsonNode a, JsonNode b)
        {
            if (a == null || b == null) return a == null && b == null;
            if (decimal.TryParse(a.ToJsonString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out decimal x)
                && decimal.TryParse(b.ToJsonString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out decimal y))
                return x == y;
            return JsonNode.DeepEquals(a, b);
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
