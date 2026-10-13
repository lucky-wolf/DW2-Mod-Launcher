using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>
    /// A mod's translated text for its settings dialog, from a sidecar file next to settings.schema.json:
    /// <c>settings.schema.&lt;language code&gt;.json</c>. The schema itself stays as the author wrote it (in whatever language that is),
    /// and the sidecar only supplies display text, looked up by the schema's own keys. Everything it does not translate is shown as the
    /// schema has it, so a translator can do just the labels. Limits, defaults and types never belong in a sidecar.
    /// <code>
    /// {
    ///   "groups": { "Hubs": "ハブ" },
    ///   "fields": {
    ///     "HubsPlayer": { "label": "…", "description": "…", "options": { "Normal": "通常" } },
    ///     "Seed": "シード"
    ///   }
    /// }
    /// </code>
    /// A field given as a plain string translates just its label. Lookups ignore case.
    /// </summary>
    public class ModSettingsTranslation
    {
        private readonly Dictionary<string, string> groups = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, FieldText> fields = new Dictionary<string, FieldText>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The translated text of one field; any part may be null (not translated).</summary>
        public class FieldText
        {
            public string Label { get; set; }
            public string Description { get; set; }
            public Dictionary<string, string> Options { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>No translation at all: every lookup answers with the schema's own text.</summary>
        public static readonly ModSettingsTranslation None = new ModSettingsTranslation();

        /// <summary>The group heading, translated if the sidecar has it (the heading is the schema's root key).</summary>
        public string Heading(string groupName, string schemaText)
        {
            return groupName != null && groups.TryGetValue(groupName, out string text) && !string.IsNullOrWhiteSpace(text) ? text : schemaText;
        }

        public string Label(string key, string schemaText)
        {
            return key != null && fields.TryGetValue(key, out FieldText f) && !string.IsNullOrWhiteSpace(f.Label) ? f.Label : schemaText;
        }

        public string Description(string key, string schemaText)
        {
            return key != null && fields.TryGetValue(key, out FieldText f) && !string.IsNullOrWhiteSpace(f.Description) ? f.Description : schemaText;
        }

        /// <summary>What to show for an enum option; the stored value is always the schema's option text.</summary>
        public string Option(string key, string option)
        {
            return key != null && option != null && fields.TryGetValue(key, out FieldText f) && f.Options.TryGetValue(option, out string text) && !string.IsNullOrWhiteSpace(text) ? text : option;
        }

        private static readonly JsonDocumentOptions DocumentOptions = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

        /// <summary>
        /// The mod's sidecar for <paramref name="languageCode"/>, or <see cref="None"/> when the mod ships none (or the language is blank).
        /// A sidecar that exists but is not valid throws InvalidDataException, so the author hears about it instead of the
        /// translation silently not appearing.
        /// </summary>
        public static ModSettingsTranslation Read(string modContentRoot, string languageCode)
        {
            if (string.IsNullOrWhiteSpace(modContentRoot) || string.IsNullOrWhiteSpace(languageCode)) return None;
            string path = Path.Combine(modContentRoot, "settings.schema." + languageCode.Trim() + ".json");
            if (!File.Exists(path)) return None;
            try
            {
                using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8), DocumentOptions))
                {
                    if (document.RootElement.ValueKind != JsonValueKind.Object)
                        throw new InvalidDataException(Path.GetFileName(path) + " must be a JSON object: " + path);
                    ModSettingsTranslation result = new ModSettingsTranslation();
                    foreach (JsonProperty root in document.RootElement.EnumerateObject())
                    {
                        if (string.Equals(root.Name, "groups", StringComparison.OrdinalIgnoreCase) && root.Value.ValueKind == JsonValueKind.Object) ReadGroups(root.Value, result);
                        else if (string.Equals(root.Name, "fields", StringComparison.OrdinalIgnoreCase) && root.Value.ValueKind == JsonValueKind.Object) ReadFields(root.Value, result, path);
                    }
                    return result;
                }
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException(Path.GetFileName(path) + " is not valid: " + path + "\n" + ex.Message, ex);
            }
        }

        private static void ReadGroups(JsonElement element, ModSettingsTranslation result)
        {
            foreach (JsonProperty group in element.EnumerateObject())
            {
                if (group.Value.ValueKind == JsonValueKind.String) result.groups[group.Name] = group.Value.GetString();
            }
        }

        private static void ReadFields(JsonElement element, ModSettingsTranslation result, string path)
        {
            foreach (JsonProperty field in element.EnumerateObject())
            {
                FieldText text = new FieldText();
                if (field.Value.ValueKind == JsonValueKind.String)
                {
                    text.Label = field.Value.GetString();
                }
                else if (field.Value.ValueKind == JsonValueKind.Object)
                {
                    foreach (JsonProperty part in field.Value.EnumerateObject())
                    {
                        if (string.Equals(part.Name, "label", StringComparison.OrdinalIgnoreCase) && part.Value.ValueKind == JsonValueKind.String) text.Label = part.Value.GetString();
                        else if (string.Equals(part.Name, "description", StringComparison.OrdinalIgnoreCase) && part.Value.ValueKind == JsonValueKind.String) text.Description = part.Value.GetString();
                        else if (string.Equals(part.Name, "options", StringComparison.OrdinalIgnoreCase) && part.Value.ValueKind == JsonValueKind.Object)
                        {
                            foreach (JsonProperty option in part.Value.EnumerateObject())
                            {
                                if (option.Value.ValueKind == JsonValueKind.String) text.Options[option.Name] = option.Value.GetString();
                            }
                        }
                    }
                }
                else
                {
                    throw new InvalidDataException("The translation of '" + field.Name + "' must be a string or an object: " + path);
                }
                result.fields[field.Name] = text;
            }
        }
    }
}
