using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using DW2ModLauncher.Core.Models;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>
    /// Builds a ModSettingsSchema (and matching current values) directly from a plain INI file, so
    /// INI-based MODs render through the same schema-driven settings editor as MODs that ship
    /// settings.schema.json - the launcher no longer needs a second, bespoke INI editor UI. Type
    /// inference is intentionally minimal: "true"/"false" becomes bool, the well-known "Language"
    /// key becomes an enum of ["ja", "en"], everything else is a plain string. A comment line
    /// (starting with '#' or ';') directly above a key becomes that field's description.
    /// </summary>
    public static class IniSettingsSchemaBuilder
    {
        public static ModSettingsSchema BuildSchema(string iniPath, out JsonObject values)
        {
            ModSettingsSchema schema = new ModSettingsSchema { Fields = new List<ModSettingsField>() };
            JsonObject collectedValues = new JsonObject();
            List<string> comments = new List<string>();
            foreach (string raw in File.ReadAllLines(iniPath, Encoding.UTF8))
            {
                string line = raw.Trim();
                if (line.StartsWith("#") || line.StartsWith(";"))
                {
                    comments.Add(line.Substring(1).Trim());
                    continue;
                }
                if (line.Length == 0) { comments.Clear(); continue; }
                int eq = line.IndexOf('=');
                if (eq <= 0) { comments.Clear(); continue; }
                string key = line.Substring(0, eq).Trim();
                string value = line.Substring(eq + 1).Trim();

                ModSettingsField field = new ModSettingsField { Key = key, Label = IniKeyHumanizer.Humanize(key) };
                if (comments.Count > 0) field.Description = string.Join(" ", comments.ToArray());

                string lower = value.ToLowerInvariant();
                if (lower == "true" || lower == "false")
                {
                    field.Type = "bool";
                    collectedValues[key] = JsonValue.Create(lower == "true");
                }
                else if (key.Equals("Language", StringComparison.OrdinalIgnoreCase))
                {
                    field.Type = "enum";
                    field.Options = new List<string> { "ja", "en" };
                    collectedValues[key] = JsonValue.Create(value);
                }
                else
                {
                    field.Type = "string";
                    collectedValues[key] = JsonValue.Create(value);
                }

                schema.Fields.Add(field);
                comments.Clear();
            }
            values = collectedValues;
            return schema;
        }
    }
}
