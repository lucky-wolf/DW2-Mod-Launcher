using System;
using System.Collections.Generic;
using System.Linq;

namespace DW2ModLauncher.Core.Models
{
    // Read from <mod ContentRoot>\settings.schema.json - shipped and versioned by the mod author
    // itself, describing the shape of the mod's own settings.json values (also mod-author-owned,
    // stored under the user's AppData, not here). Every root key whose value is an array of fields is
    // a group (the key is its heading); "fields" is just the conventional name of the first one.
    // No versioning, no migrations - not needed yet.
    public class ModSettingsSchema
    {
        public List<ModSettingsGroup> Groups { get; set; } = new List<ModSettingsGroup>();

        /// <summary>Every field of every group, in file order.</summary>
        public List<ModSettingsField> Fields { get { return Groups.SelectMany(g => g.Fields).ToList(); } }

        /// <summary>
        /// The fields the settings editor shows for this mod. Fields marked hidden are developer controls
        /// (logging, output folders, enable toggles) and are not shown for Workshop copies; they still keep their
        /// defaults in the values handed to the mod.
        /// </summary>
        public IEnumerable<ModSettingsField> VisibleFields(ModInfo mod)
        {
            return VisibleGroups(mod).SelectMany(g => g.Fields);
        }

        /// <summary>The groups the editor shows: each with only its visible fields, and groups left with none dropped.</summary>
        public List<ModSettingsGroup> VisibleGroups(ModInfo mod)
        {
            bool isWorkshop = mod != null && mod.IsWorkshop;
            List<ModSettingsGroup> result = new List<ModSettingsGroup>();
            foreach (ModSettingsGroup group in Groups)
            {
                List<ModSettingsField> shown = group.Fields.Where(f => !(f.Hidden && isWorkshop)).ToList();
                if (shown.Count > 0) result.Add(new ModSettingsGroup { Name = group.Name, Fields = shown });
            }
            return result;
        }
    }

    /// <summary>A named block of fields (a root key of settings.schema.json whose value is an array).</summary>
    public class ModSettingsGroup
    {
        public const string DefaultName = "fields";

        public string Name { get; set; }
        public List<ModSettingsField> Fields { get; set; } = new List<ModSettingsField>();

        /// <summary>The conventional "fields" group has no heading of its own, so existing schemas look as they always did.</summary>
        public bool HasHeading { get { return !string.IsNullOrWhiteSpace(Name) && !string.Equals(Name, DefaultName, StringComparison.OrdinalIgnoreCase); } }
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
        // "hidden": true - shown only for local (non-Workshop) mods, in place within its group; the author's own debug/dev controls.
        public bool Hidden { get; set; }
        // "localOnly": the older name for "hidden", still accepted.
        public bool LocalOnly { get { return Hidden; } set { if (value) Hidden = true; } }
    }
}
