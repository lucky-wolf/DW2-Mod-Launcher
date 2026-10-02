using System;
using System.Globalization;
using System.Text.Json.Nodes;
using DW2ModLauncher.Core.Models;

namespace DW2ModLauncher.Core.Services
{
    public enum ModSettingKind
    {
        Text,
        Bool,
        Choice,
        Integer,
        Number
    }

    /// <summary>Maps a mod's settings.schema.json field to an editor control kind and converts values to and from JSON. No UI.</summary>
    public static class ModSettingsValues
    {
        public static ModSettingKind KindOf(ModSettingsField field)
        {
            string type = (field.Type ?? "").ToLowerInvariant();
            if (type == "bool") return ModSettingKind.Bool;
            if (type == "enum" && field.Options != null && field.Options.Count > 0) return ModSettingKind.Choice;
            if (type == "int") return ModSettingKind.Integer;
            if (type == "float") return ModSettingKind.Number;
            return ModSettingKind.Text;
        }

        public static bool ToBool(JsonNode current)
        {
            try { return current != null && current.GetValue<bool>(); }
            catch { return false; }
        }

        /// <summary>The option matching the stored text (case-insensitively), else the first option.</summary>
        public static string ToChoice(ModSettingsField field, JsonNode current)
        {
            string text = current?.ToString() ?? "";
            string match = field.Options.Find(x => x.Equals(text, StringComparison.OrdinalIgnoreCase));
            return match ?? field.Options[0];
        }

        /// <summary>The stored number clamped to the field's Min/Max (defaults +-1,000,000).</summary>
        public static decimal ToNumber(ModSettingsField field, JsonNode current)
        {
            decimal min = (decimal)(field.Min ?? -1000000);
            decimal max = (decimal)(field.Max ?? 1000000);
            // Parse the JSON text rather than GetValue<double>(): that throws for integer-backed values (e.g. defaults
            // built in code), which would silently show as 0.
            decimal value = 0;
            if (current != null && decimal.TryParse(current.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out decimal parsed)) value = parsed;
            return Math.Max(min, Math.Min(max, value));
        }

        public static JsonNode FromBool(bool value) { return JsonValue.Create(value); }
        public static JsonNode FromChoice(string value) { return JsonValue.Create(value ?? ""); }
        public static JsonNode FromText(string value) { return JsonValue.Create(value ?? ""); }

        public static JsonNode FromNumber(ModSettingsField field, decimal value)
        {
            return KindOf(field) == ModSettingKind.Integer ? JsonValue.Create((long)value) : JsonValue.Create((double)value);
        }
    }
}
