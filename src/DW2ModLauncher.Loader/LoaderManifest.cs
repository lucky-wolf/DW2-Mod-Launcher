using System.Collections.Generic;

namespace DW2ModLauncher.Loader
{
    // Mirrors DW2ModLauncher.Core.Models.LoaderManifest field-for-field. Kept as a separate,
    // dependency-free copy on purpose: this project must not ProjectReference Core/App, since it
    // runs injected into the game process and has to stay minimal (BCL + System.Text.Json only).
    public class LoaderManifest
    {
        public List<LoaderManifestEntry> Entries { get; set; } = new List<LoaderManifestEntry>();
    }

    public class LoaderManifestEntry
    {
        public string DllPath { get; set; }
        public string EntryType { get; set; }
        public string SettingsJson { get; set; }
        public string DisplayName { get; set; }
    }
}
