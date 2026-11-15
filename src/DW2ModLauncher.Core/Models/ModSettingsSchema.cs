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

        /// <summary>
        /// The settings this mod gave to another mod, from the root "_moved" object ({"OldKey": {"mod": "receiver uid", "key": "NewKey"}}), by the key this mod used to have.
        /// The launcher keeps a player's value for such a key (see ModSettingsStore.RetireMoved) until the receiver, which must name this mod back, takes it. Empty when the mod gave nothing away.
        /// </summary>
        public Dictionary<string, ModSettingMoveTarget> Moved { get; set; } = new Dictionary<string, ModSettingMoveTarget>(StringComparer.Ordinal);

        /// <summary>
        /// Which switches each setting needs, from the root "_enables" object ({"Enabler": ["Setting", ...]}): the setting key maps to the keys of the bool settings that must all be on.
        /// A setting whose enabler is itself dependent is available only when the whole chain is on. Keys compare ignoring case.
        /// </summary>
        public Dictionary<string, List<string>> EnabledBy { get; set; } = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

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
                if (shown.Count > 0) result.Add(new ModSettingsGroup { Name = group.Name, Description = group.Description, Layout = group.Layout, Fields = shown });
            }
            return result;
        }
    }

    /// <summary>A named block of fields (a root key of settings.schema.json whose value is an array).</summary>
    public class ModSettingsGroup
    {
        public const string DefaultName = "fields";

        public string Name { get; set; }
        /// <summary>What the group is about, from the schema's root "_groups" object ({"GroupName": "text"}); shown under the heading. Null when the author gave none.</summary>
        public string Description { get; set; }
        public List<ModSettingsField> Fields { get; set; } = new List<ModSettingsField>();
        /// <summary>How the group is drawn, from the schema's root "_layouts" object; null draws the usual two-column field list.</summary>
        public ModSettingsLayout Layout { get; set; }

        /// <summary>The conventional "fields" group has no heading of its own, so existing schemas look as they always did.</summary>
        public bool HasHeading { get { return !string.IsNullOrWhiteSpace(Name) && !string.Equals(Name, DefaultName, StringComparison.OrdinalIgnoreCase); } }
    }

    /// <summary>
    /// A group drawn as a ladder matrix: one row per field, one column per option. Every field of the group is an enum; the columns are the longest option list, and a field with fewer options
    /// (in the same order) has the missing columns blocked, never selectable; the stored value is the row's threshold, and a cell is filled from the first column up to and including the threshold.
    /// <see cref="None"/> is the option that fills nothing; it is drawn as a column of its own, at the end of the list it is in (first or last), and the bars grow from that end.
    /// </summary>
    public class ModSettingsLayout
    {
        public const string LadderKind = "ladder";

        public string Kind { get; set; }
        /// <summary>The option meaning "no column filled"; it must be the first or last option, is a column of its own, and is filled only while the row holds it.</summary>
        public string None { get; set; }
        /// <summary>Short column headings by option ({"Close ally": "CA"}); the full option text becomes the heading's tooltip. Options without an entry show in full.</summary>
        public Dictionary<string, string> Short { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
        /// <summary>"movedFrom": [{"mod": "giver uid", "key": "OldKey"}] - where a player's value for this setting may have lived before it moved here, in the order to try. The value comes over only when that mod's own schema says it gave the key to this mod and key.</summary>
        public List<ModSettingMoveSource> MovedFrom { get; set; } = new List<ModSettingMoveSource>();
    }

    /// <summary>Where a setting used to live: the giving mod's uid and the key it had there.</summary>
    public class ModSettingMoveSource
    {
        public string Mod { get; set; }
        public string Key { get; set; }
    }

    /// <summary>Where a setting went: the receiving mod's uid and the key it has there.</summary>
    public class ModSettingMoveTarget
    {
        public string Mod { get; set; }
        public string Key { get; set; }
    }
}
