using System.Collections.Generic;

namespace DW2ModLauncher.Core.Models
{
    // Read from <mod ContentRoot>\settings.schema.json - shipped and versioned by the mod author
    // itself, describing the shape of the mod's own settings.json values (also mod-author-owned,
    // stored under the user's AppData, not here). Deliberately flat: no groups, no versioning, no
    // migrations - not needed yet.
    public class ModSettingsSchema
    {
        public List<ModSettingsField> Fields { get; set; } = new List<ModSettingsField>();
    }

    public class ModSettingsField
    {
        public string Key { get; set; }
        // "bool" | "enum" | "int" | "float" | "string"
        public string Type { get; set; }
        public string Label { get; set; }
        public string Description { get; set; }
        public object Default { get; set; }
        public double? Min { get; set; }
        public double? Max { get; set; }
        public List<string> Options { get; set; }
    }
}
