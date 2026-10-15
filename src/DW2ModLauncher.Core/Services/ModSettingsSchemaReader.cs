using System;
using System.IO;
using System.Text;
using System.Text.Json;
using DW2ModLauncher.Core.Models;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>
    /// Reads a mod's optional settings.schema.json (shipped by the mod author, describing the
    /// shape of its own settings.json values) into a ModSettingsSchema. Every root key whose value is an
    /// array is a group of fields, in file order; the root "_groups" object ({"GroupName": "what it is about"}) gives groups a description;
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
                    return schema.Groups.Count > 0 ? schema : null;
                }
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException("settings.schema.json is not valid: " + path + "\n" + ex.Message, ex);
            }
        }
    }
}
