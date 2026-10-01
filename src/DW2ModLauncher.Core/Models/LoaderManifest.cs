using System.Collections.Generic;

namespace DW2ModLauncher.Core.Models
{
    // Written by the launcher next to the shipped loader DLL before each launch, and read back by
    // DW2ModLauncher.Loader.Entry.Init() at --low-level-inject time. Kept in sync manually with
    // the loader project's own copy of this shape (see DW2ModLauncher.Loader/LoaderManifest.cs) -
    // the loader intentionally has no ProjectReference to Core.
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
