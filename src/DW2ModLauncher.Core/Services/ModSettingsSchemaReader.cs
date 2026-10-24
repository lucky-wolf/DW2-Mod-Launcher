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
                    return schema.Groups.Count > 0 ? schema : null;
                }
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException("settings.schema.json is not valid: " + path + "\n" + ex.Message, ex);
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

        private static void ValidateLadder(ModSettingsGroup group, ModSettingsLayout layout, string path)
        {
            string head = "settings.schema.json: group '" + group.Name + "' is a ladder, but ";
            List<string> options = null;
            foreach (ModSettingsField field in group.Fields)
            {
                if (!string.Equals(field.Type, "enum", StringComparison.OrdinalIgnoreCase) || field.Options == null || field.Options.Count == 0)
                    throw new InvalidDataException(head + "field '" + field.Key + "' is not an enum with options: " + path);
                if (options == null) options = field.Options;
                else if (!options.SequenceEqual(field.Options, StringComparer.OrdinalIgnoreCase))
                    throw new InvalidDataException(head + "field '" + field.Key + "' has different options from '" + group.Fields[0].Key + "': " + path);
            }
            if (string.IsNullOrWhiteSpace(layout.None) || !options.Contains(layout.None, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException(head + "its 'none' option '" + layout.None + "' is not one of the options: " + path);
            if (options.Count < 2)
                throw new InvalidDataException(head + "it has no column besides 'none': " + path);
            foreach (string key in layout.Short.Keys)
                if (!options.Contains(key, StringComparer.OrdinalIgnoreCase) || string.Equals(key, layout.None, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(head + "'short' names '" + key + "', which is not a column: " + path);
        }
    }
}
