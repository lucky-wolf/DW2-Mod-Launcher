using System.Collections.Generic;

namespace DW2ModLauncher.Loader
{
    // Mirrors DW2ModLauncher.Core.Models.LoaderManifest field-for-field. Kept as a separate,
    // dependency-free copy on purpose: this project must not ProjectReference Core/App, since it
    // runs injected into the game process and has to stay minimal (BCL + System.Text.Json only).
    public class LoaderManifest
    {
        public List<LoaderManifestEntry> Entries { get; set; } = new List<LoaderManifestEntry>();
        public List<LoaderManifestPatchSet> Patches { get; set; } = new List<LoaderManifestPatchSet>();
    }

    public class LoaderManifestPatchSet
    {
        public string DisplayName { get; set; }
        public List<string> Files { get; set; } = new List<string>();
    }

    public class LoaderManifestEntry
    {
        public string DllPath { get; set; }
        public string EntryType { get; set; }
        public string SettingsJson { get; set; }
        public string DisplayName { get; set; }
    }
}
