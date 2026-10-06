using System.Collections.Generic;

namespace DW2ModLauncher.Core.Models
{
    // The mod.json fields Steam Workshop publish itself cares about (see
    // docs/workshop-publish.md) - distinct from a Mod's own settings.schema.json/dw2modlauncher.json
    // data, which describe how the launcher should run or configure the Mod, not how Steam should
    // list it.
    public class ModPublishMetadata
    {
        public string DisplayName { get; set; }
        /// <summary>mod.json "shortDescription": the one-liner the launcher shows.</summary>
        public string ShortDescription { get; set; }
        /// <summary>mod.json "descriptionFile": the mod's own name for its long-description file; blank means description.bbcode (see ModDescriptionFile).</summary>
        public string DescriptionFile { get; set; }
        /// <summary>Legacy mod.json "description". Read only to seed description.bbcode; never written (see ModDescriptionFile).</summary>
        public string Description { get; set; }
        public string PreviewImage { get; set; }
        public string Version { get; set; }
        public List<string> Bundles { get; set; } = new List<string>();
    }
}
