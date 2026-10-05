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

        /// <summary>
        /// The fields the settings editor shows for this mod. Fields marked localOnly are developer controls
        /// (logging, output folders, enable toggles) and are hidden for Workshop copies; they still keep their
        /// defaults in the values handed to the mod.
        /// </summary>
        public IEnumerable<ModSettingsField> VisibleFields(ModInfo mod)
        {
            bool isWorkshop = mod != null && mod.IsWorkshop;
            foreach (ModSettingsField field in Fields)
                if (!(field.LocalOnly && isWorkshop)) yield return field;
        }
    }

    public class ModSettingsField
    {
        public string Key { get; set; }
        // "bool" | "enum" | "int" | "float" | "string" | "folder" | "file"
        public string Type { get; set; }
        public string Label { get; set; }
        public string Description { get; set; }
        public object Default { get; set; }
        public double? Min { get; set; }
        public double? Max { get; set; }
        public List<string> Options { get; set; }
        // "localOnly": true - shown only for local (non-Workshop) mods; the author's own debug/dev controls.
        public bool LocalOnly { get; set; }
    }
}
