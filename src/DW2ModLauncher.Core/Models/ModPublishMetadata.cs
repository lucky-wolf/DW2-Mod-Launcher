using System.Collections.Generic;

namespace DW2ModLauncher.Core.Models
{
    // The mod.json fields Steam Workshop publish itself cares about (see
    // docs/workshop-publish.md) - distinct from a MOD's own settings.schema.json/launcher.json
    // data, which describe how the launcher should run or configure the MOD, not how Steam should
    // list it.
    public class ModPublishMetadata
    {
        public string DisplayName { get; set; }
        public string Description { get; set; }
        public string PreviewImage { get; set; }
        public string Version { get; set; }
        public List<string> Bundles { get; set; } = new List<string>();
    }
}
