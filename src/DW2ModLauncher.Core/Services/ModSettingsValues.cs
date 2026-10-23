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
        Number,
        Folder,
        File
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
            if (type == "folder") return ModSettingKind.Folder;
            if (type == "filename" || type == "file") return ModSettingKind.File;
            return ModSettingKind.Text;
        }

        /// <summary>
        /// Whether the text is acceptable for the kind. Blank is always fine (the mod falls back to its default);
        /// a folder must be an existing directory and a filename an existing file. Other kinds are never invalid.
        /// A %TOKEN% folder name (see <see cref="PathTokens"/>) is expanded first; an unknown one is invalid.
        /// </summary>
        public static bool IsValidPath(ModSettingKind kind, string text, string gameRoot = null)
        {
            return PathProblemKey(kind, text, gameRoot, out _, out _) == null;
        }

        /// <summary>
        /// What is wrong with a folder/file text, as the suffix of a language key ("NotFound" or the <see cref="PathTokenProblem"/> name), or null.
        /// A path written with a %NAME% is only wrong when the name is unknown: that the name resolves is what matters, not that the folders under
        /// it exist, and a name that cannot be resolved yet (no game folder set) is reported when the game is launched, never here, so the
        /// dialog can always be left. A plain path must exist. <paramref name="expanded"/> is the host path when it could be worked out.
        /// </summary>
        public static string PathProblemKey(ModSettingKind kind, string text, string gameRoot, out string expanded, out string detail)
        {
            expanded = null;
            detail = null;
            if ((kind != ModSettingKind.Folder && kind != ModSettingKind.File) || string.IsNullOrWhiteSpace(text)) return null;
            PathExpansion result = PathTokens.ExpandForHost(text, gameRoot);
            expanded = result.Ok ? result.Path : null;
            if (result.Problem == PathTokenProblem.UnknownToken) { detail = result.Detail; return nameof(PathTokenProblem.UnknownToken); }
            if (PathTokens.HasToken(text)) return null;
            return (kind == ModSettingKind.Folder ? System.IO.Directory.Exists(result.Path) : System.IO.File.Exists(result.Path)) ? null : "NotFound";
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

        /// <summary>The field's lower bound; open-ended when the schema gives none.</summary>
        public static decimal MinOf(ModSettingsField field) { return field.Min.HasValue ? (decimal)field.Min.Value : decimal.MinValue; }

        /// <summary>The field's upper bound; open-ended when the schema gives none.</summary>
        public static decimal MaxOf(ModSettingsField field) { return field.Max.HasValue ? (decimal)field.Max.Value : decimal.MaxValue; }

        /// <summary>The spinner step: a power of ten scaled to the size of the field's default (0.1 below 10 for floats, 1 for ints).</summary>
        public static decimal StepOf(ModSettingsField field)
        {
            decimal reference = 0;
            if (field.Default != null && decimal.TryParse(Convert.ToString(field.Default, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out decimal d)) reference = Math.Abs(d);
            decimal step = KindOf(field) == ModSettingKind.Integer ? 1m : 0.1m;
            while (reference >= step * 100m) step *= 10m;
            return step;
        }

        /// <summary>The stored number clamped to the field's Min/Max (open-ended when the schema omits them).</summary>
        public static decimal ToNumber(ModSettingsField field, JsonNode current)
        {
            decimal min = MinOf(field);
            decimal max = MaxOf(field);
            // Parse the JSON text rather than GetValue<double>(): that throws for integer-backed values (e.g. defaults
            // built in code), which would silently show as 0.
            decimal value = 0;
            if (current != null && decimal.TryParse(current.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out decimal parsed)) value = parsed;
            return Math.Max(min, Math.Min(max, value));
        }

        public static JsonNode FromBool(bool value) { return JsonValue.Create(value); }
        public static JsonNode FromChoice(string value) { return JsonValue.Create(value ?? ""); }
        public static JsonNode FromText(string value) { return JsonValue.Create(value ?? ""); }
        public static JsonNode FromPath(string value) { return JsonValue.Create((value ?? "").Trim()); }

        public static JsonNode FromNumber(ModSettingsField field, decimal value)
        {
            return KindOf(field) == ModSettingKind.Integer ? JsonValue.Create((long)value) : JsonValue.Create((double)value);
        }
    }
}
