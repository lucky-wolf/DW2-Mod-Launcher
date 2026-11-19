using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using DW2ModLauncher.Core.Models;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>
    /// Reads a mod's optional settings.schema.json (shipped by the mod author, describing the
    /// shape of its own settings.json values) into a ModSettingsSchema. Every root key whose value is an
    /// array is a group of fields, in file order; the root "_groups" object ({"GroupName": "what it is about"}) gives groups a description; the root "_layouts" object draws a group as a ladder matrix (see ModSettingsLayout);
    /// other root keys (e.g. "$schema") are ignored. A field key
    /// that already appeared in an earlier group is skipped. Returns null only when the mod ships no schema (or an
    /// empty one); a schema that exists but cannot be read throws InvalidDataException.
    /// </summary>
    public static class ModSettingsSchemaReader
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        private static readonly JsonDocumentOptions DocumentOptions = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

        public static ModSettingsSchema Read(string modContentRoot)
        {
            if (string.IsNullOrWhiteSpace(modContentRoot)) return null;
            string path = Path.Combine(modContentRoot, "settings.schema.json");
            if (!File.Exists(path)) return null;
            try
            {
                using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8), DocumentOptions))
                {
                    if (document.RootElement.ValueKind != JsonValueKind.Object)
                        throw new InvalidDataException("settings.schema.json must be a JSON object: " + path);
                    ModSettingsSchema schema = new ModSettingsSchema();
                    System.Collections.Generic.HashSet<string> seen = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (JsonProperty property in document.RootElement.EnumerateObject())
                    {
                        if (property.Value.ValueKind != JsonValueKind.Array) continue;
                        ModSettingsGroup group = new ModSettingsGroup { Name = property.Name };
                        foreach (JsonElement element in property.Value.EnumerateArray())
                        {
                            ModSettingsField field = element.Deserialize<ModSettingsField>(Options);
                            if (field == null) continue;
                            if (!string.IsNullOrWhiteSpace(field.Key) && !seen.Add(field.Key)) continue;
                            group.Fields.Add(field);
                        }
                        if (group.Fields.Count > 0) schema.Groups.Add(group);
                    }
                    if (document.RootElement.TryGetProperty("_groups", out JsonElement descriptions) && descriptions.ValueKind == JsonValueKind.Object)
                    {
                        foreach (JsonProperty entry in descriptions.EnumerateObject())
                        {
                            if (entry.Value.ValueKind != JsonValueKind.String)
                                throw new InvalidDataException("settings.schema.json: the description of group '" + entry.Name + "' must be a string: " + path);
                            foreach (ModSettingsGroup group in schema.Groups)
                                if (string.Equals(group.Name, entry.Name, StringComparison.OrdinalIgnoreCase)) group.Description = entry.Value.GetString();
                        }
                    }
                    if (document.RootElement.TryGetProperty("_layouts", out JsonElement layouts))
                        ReadLayouts(layouts, schema, path);
                    if (document.RootElement.TryGetProperty("_moved", out JsonElement moved))
                        ReadMoved(moved, schema, path);
                    ValidateMovedFrom(schema, path);
                    if (document.RootElement.TryGetProperty("_enables", out JsonElement enables))
                        ReadEnables(enables, schema, path);
                    if (document.RootElement.TryGetProperty("_previews", out JsonElement previews))
                        ReadPreviews(previews, schema, path);
                    // a mod that gave away every setting still has to say so, or the players' values could not follow
                    return schema.Groups.Count > 0 || schema.Moved.Count > 0 ? schema : null;
                }
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException("settings.schema.json is not valid: " + path + "\n" + ex.Message, ex);
            }
        }

        // "_moved": { "OldKey": { "mod": "<receiving mod's uid>", "key": "NewKey" } }
        // A key this schema still declares cannot also be given away, and the receiver must be named by a real uid; both are errors, not guesses.
        private static void ReadMoved(JsonElement moved, ModSettingsSchema schema, string path)
        {
            const string where = "settings.schema.json: ";
            if (moved.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException(where + "_moved must be an object of moved settings: " + path);
            foreach (JsonProperty entry in moved.EnumerateObject())
            {
                if (entry.Value.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException(where + "_moved '" + entry.Name + "' must be an object with \"mod\" and \"key\": " + path);
                ModSettingMoveTarget target = entry.Value.Deserialize<ModSettingMoveTarget>(Options);
                string uid = ModUid.Normalize(target.Mod);
                if (uid == null)
                    throw new InvalidDataException(where + "_moved '" + entry.Name + "' needs the receiving mod's \"mod\" as its uid (a UUID), not \"" + target.Mod + "\": " + path);
                if (string.IsNullOrWhiteSpace(target.Key))
                    throw new InvalidDataException(where + "_moved '" + entry.Name + "' has no \"key\", the name the setting has in the receiving mod: " + path);
                if (schema.Fields.Any(f => string.Equals(f.Key, entry.Name, StringComparison.Ordinal)))
                    throw new InvalidDataException(where + "_moved lists '" + entry.Name + "', but this schema still declares that setting: " + path);
                target.Mod = uid;
                schema.Moved[entry.Name] = target;
            }
        }

        private static readonly string[] PreviewExtensions = { ".png", ".jpg", ".jpeg", ".webp" };

        // "_previews": { "Setting": "a.png", "EnumSetting": { "Option": "b.png" } }
        // Each value is a bare file name, found only in the mod's "_previews" folder; whether the file exists is the editor's to report when the image is wanted.
        private static void ReadPreviews(JsonElement previews, ModSettingsSchema schema, string path)
        {
            const string where = "settings.schema.json: ";
            if (previews.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException(where + "_previews must be an object of {\"Setting\": image file name}: " + path);
            foreach (JsonProperty entry in previews.EnumerateObject())
            {
                ModSettingsField field = schema.Fields.Find(f => string.Equals(f.Key, entry.Name, StringComparison.OrdinalIgnoreCase));
                if (field == null)
                    throw new InvalidDataException(where + "_previews names '" + entry.Name + "', which is not a setting of this schema: " + path);
                if (entry.Value.ValueKind == JsonValueKind.String)
                {
                    field.Preview = ValidatePreviewPath(entry.Name, entry.Value.GetString(), path);
                }
                else if (entry.Value.ValueKind == JsonValueKind.Object)
                {
                    if (!string.Equals(field.Type, "enum", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException(where + "_previews '" + entry.Name + "' gives an image per option, but the setting is a " + field.Type + ", not an enum: " + path);
                    foreach (JsonProperty option in entry.Value.EnumerateObject())
                    {
                        if (field.Options == null || !field.Options.Contains(option.Name, StringComparer.OrdinalIgnoreCase))
                            throw new InvalidDataException(where + "_previews '" + entry.Name + "' names option '" + option.Name + "', which the setting does not have: " + path);
                        if (option.Value.ValueKind != JsonValueKind.String)
                            throw new InvalidDataException(where + "_previews '" + entry.Name + "' option '" + option.Name + "' must be an image file name: " + path);
                        field.OptionPreviews[option.Name] = ValidatePreviewPath(entry.Name + "/" + option.Name, option.Value.GetString(), path);
                    }
                }
                else
                {
                    throw new InvalidDataException(where + "_previews '" + entry.Name + "' must be an image file name, or an object of option: image file name: " + path);
                }
            }
        }

        private static string ValidatePreviewPath(string name, string value, string path)
        {
            string head = "settings.schema.json: _previews '" + name + "' ";
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidDataException(head + "has an empty image file name: " + path);
            if (value.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 || value == "." || value == "..")
                throw new InvalidDataException(head + "must be just an image file name, looked up in the mod's " + ModSettingsField.PreviewFolder + " folder, not '" + value + "': " + path);
            if (!PreviewExtensions.Contains(Path.GetExtension(value), StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException(head + "must be a PNG, JPG or WebP image, not '" + value + "': " + path);
            return value;
        }

        // "_enables": { "Enabler": ["Setting", "OtherSetting"] }
        // Every named key must be a setting of this schema, an enabler must be a bool, and the chains must not loop; each is an error naming the key, not a guess.
        private static void ReadEnables(JsonElement enables, ModSettingsSchema schema, string path)
        {
            const string where = "settings.schema.json: ";
            if (enables.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException(where + "_enables must be an object of {\"Enabler\": [settings]}: " + path);
            foreach (JsonProperty entry in enables.EnumerateObject())
            {
                ModSettingsField enabler = schema.Fields.Find(f => string.Equals(f.Key, entry.Name, StringComparison.OrdinalIgnoreCase));
                if (enabler == null)
                    throw new InvalidDataException(where + "_enables names '" + entry.Name + "', which is not a setting of this schema: " + path);
                bool isEnum = string.Equals(enabler.Type, "enum", StringComparison.OrdinalIgnoreCase);
                if (!isEnum && !string.Equals(enabler.Type, "bool", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(where + "_enables '" + entry.Name + "' is a " + enabler.Type + ", but only a bool or an enum can switch other settings on and off: " + path);
                if (isEnum)
                {
                    if (entry.Value.ValueKind != JsonValueKind.Object)
                        throw new InvalidDataException(where + "_enables '" + entry.Name + "' is an enum, so it must list the settings each option enables as an object of {\"Option\": [settings]}: " + path);
                    foreach (JsonProperty option in entry.Value.EnumerateObject())
                    {
                        string optionText = enabler.Options?.FirstOrDefault(o => string.Equals(o, option.Name, StringComparison.OrdinalIgnoreCase));
                        if (optionText == null)
                            throw new InvalidDataException(where + "_enables '" + entry.Name + "' names option '" + option.Name + "', which the setting does not have: " + path);
                        if (option.Value.ValueKind != JsonValueKind.Array)
                            throw new InvalidDataException(where + "_enables '" + entry.Name + "' option '" + option.Name + "' must list the settings it enables as an array of keys: " + path);
                        foreach (string key in EnabledKeys(option.Value, entry.Name, schema, path))
                            AddNeed(schema, key, enabler.Key, optionText);
                    }
                }
                else
                {
                    if (entry.Value.ValueKind != JsonValueKind.Array)
                        throw new InvalidDataException(where + "_enables '" + entry.Name + "' must list the settings it enables as an array of keys: " + path);
                    foreach (string key in EnabledKeys(entry.Value, entry.Name, schema, path))
                        AddNeed(schema, key, enabler.Key, null);
                }
            }
            // follow every chain up; meeting the starting key again means the switches wait on each other
            foreach (string start in schema.EnabledBy.Keys)
            {
                HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                Stack<string> pending = new Stack<string>(schema.EnabledBy[start]);
                while (pending.Count > 0)
                {
                    string key = pending.Pop();
                    if (string.Equals(key, start, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException(where + "_enables loops: '" + start + "' is switched by a chain of settings that includes itself: " + path);
                    if (!visited.Add(key) || !schema.EnabledBy.TryGetValue(key, out List<string> next)) continue;
                    foreach (string n in next) pending.Push(n);
                }
            }
        }

        // The keys of an array under an enabler: each must be a setting of the schema other than the enabler itself.
        private static List<string> EnabledKeys(JsonElement array, string enablerName, ModSettingsSchema schema, string path)
        {
            const string where = "settings.schema.json: ";
            List<string> keys = new List<string>();
            foreach (JsonElement item in array.EnumerateArray())
            {
                string key = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
                if (key == null || !schema.Fields.Any(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException(where + "_enables '" + enablerName + "' lists '" + (key ?? item.ToString()) + "', which is not a setting of this schema: " + path);
                if (string.Equals(key, enablerName, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(where + "_enables '" + enablerName + "' lists itself: " + path);
                keys.Add(key);
            }
            return keys;
        }

        // Records that a setting needs the enabler: on for a bool (option null), or set to one of the options it was listed under for an enum.
        private static void AddNeed(ModSettingsSchema schema, string key, string enablerKey, string option)
        {
            if (!schema.EnabledBy.TryGetValue(key, out List<string> needs)) schema.EnabledBy[key] = needs = new List<string>();
            if (!needs.Contains(enablerKey, StringComparer.OrdinalIgnoreCase)) needs.Add(enablerKey);
            if (option == null) return;
            if (!schema.EnabledWhen.TryGetValue(key, out Dictionary<string, List<string>> byEnum)) schema.EnabledWhen[key] = byEnum = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            if (!byEnum.TryGetValue(enablerKey, out List<string> options)) byEnum[enablerKey] = options = new List<string>();
            if (!options.Contains(option, StringComparer.OrdinalIgnoreCase)) options.Add(option);
        }

        private static void ValidateMovedFrom(ModSettingsSchema schema, string path)
        {
            foreach (ModSettingsField field in schema.Fields)
            {
                foreach (ModSettingMoveSource source in field.MovedFrom ?? new List<ModSettingMoveSource>())
                {
                    string uid = ModUid.Normalize(source?.Mod);
                    if (uid == null)
                        throw new InvalidDataException("settings.schema.json: movedFrom of '" + field.Key + "' needs the giving mod's \"mod\" as its uid (a UUID), not \"" + source?.Mod + "\": " + path);
                    if (string.IsNullOrWhiteSpace(source.Key))
                        throw new InvalidDataException("settings.schema.json: movedFrom of '" + field.Key + "' has no \"key\", the name the setting had in the giving mod: " + path);
                    source.Mod = uid;
                }
            }
        }

        // "_layouts": { "GroupName": { "kind": "ladder", "none": "Never", "short": { "Close ally": "CA" } } }
        // A layout that cannot be drawn is an error naming the group, not a quiet return to the plain list.
        private static void ReadLayouts(JsonElement layouts, ModSettingsSchema schema, string path)
        {
            const string where = "settings.schema.json: ";
            if (layouts.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException(where + "_layouts must be an object of group layouts: " + path);
            foreach (JsonProperty entry in layouts.EnumerateObject())
            {
                ModSettingsGroup group = schema.Groups.Find(g => string.Equals(g.Name, entry.Name, StringComparison.OrdinalIgnoreCase));
                if (group == null)
                    throw new InvalidDataException(where + "_layouts names group '" + entry.Name + "', which the schema does not have: " + path);
                if (entry.Value.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException(where + "the layout of group '" + entry.Name + "' must be an object: " + path);
                ModSettingsLayout layout = entry.Value.Deserialize<ModSettingsLayout>(Options);
                if (!string.Equals(layout.Kind, ModSettingsLayout.LadderKind, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(where + "the layout of group '" + entry.Name + "' has kind '" + layout.Kind + "'; the only kind is '" + ModSettingsLayout.LadderKind + "': " + path);
                layout.Short = new Dictionary<string, string>(layout.Short ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
                ValidateLadder(group, layout, path);
                group.Layout = layout;
            }
        }

        private static bool IsSubsequence(List<string> part, List<string> whole)
        {
            int at = 0;
            foreach (string option in part)
            {
                while (at < whole.Count && !string.Equals(whole[at], option, StringComparison.OrdinalIgnoreCase)) at++;
                if (at == whole.Count) return false;
                at++;
            }
            return true;
        }

        private static void ValidateLadder(ModSettingsGroup group, ModSettingsLayout layout, string path)
        {
            string head = "settings.schema.json: group '" + group.Name + "' is a ladder, but ";
            foreach (ModSettingsField field in group.Fields)
                if (!string.Equals(field.Type, "enum", StringComparison.OrdinalIgnoreCase) || field.Options == null || field.Options.Count == 0)
                    throw new InvalidDataException(head + "field '" + field.Key + "' is not an enum with options: " + path);
            // the columns are the longest option list; a row with fewer options leaves the rest of its cells blocked, so it must keep the same order
            ModSettingsField widest = group.Fields.OrderByDescending(f => f.Options.Count).First();
            List<string> options = widest.Options;
            foreach (ModSettingsField field in group.Fields)
                if (!IsSubsequence(field.Options, options))
                    throw new InvalidDataException(head + "the options of field '" + field.Key + "' are not in the order of '" + widest.Key + "' (a row may leave options out, not reorder or add them): " + path);
            if (string.IsNullOrWhiteSpace(layout.None) || !options.Contains(layout.None, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException(head + "its 'none' option '" + layout.None + "' is not one of the options: " + path);
            if (options.Count > 1 && !string.Equals(options[0], layout.None, StringComparison.OrdinalIgnoreCase) && !string.Equals(options[options.Count - 1], layout.None, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(head + "its 'none' option '" + layout.None + "' must be the first or the last option of '" + widest.Key + "', the end the bars grow from: " + path);
            if (options.Count < 2)
                throw new InvalidDataException(head + "it has no column besides 'none': " + path);
            foreach (string key in layout.Short.Keys)
                if (!options.Contains(key, StringComparer.OrdinalIgnoreCase))
                    throw new InvalidDataException(head + "'short' names '" + key + "', which is not a column: " + path);
        }
    }
}
