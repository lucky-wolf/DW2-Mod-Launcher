using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DW2ModLauncher.Core.Models
{
    // Written by the launcher next to the shipped loader DLL before each launch, and read back by
    // DW2ModLauncher.Loader.Entry.Init() at --low-level-inject time. Kept in sync manually with
    // the loader project's own copy of this shape (see DW2ModLauncher.Loader/LoaderManifest.cs) -
    // the loader intentionally has no ProjectReference to Core.
    public class LoaderManifest
    {
        public List<LoaderManifestEntry> Entries { get; set; } = new List<LoaderManifestEntry>();

        /// <summary>The XML patch files of every enabled mod, in load order (a mod needs no DLL to have patches).</summary>
        public List<LoaderManifestPatchSet> Patches { get; set; } = new List<LoaderManifestPatchSet>();
    }

    /// <summary>One mod's XML patch files (see docs/plans/xml-patching.md), in the order they apply.</summary>
    public class LoaderManifestPatchSet
    {
        public string DisplayName { get; set; }
        /// <summary>Paths as the game process sees them (Z:\... under Proton).</summary>
        public List<string> Files { get; set; } = new List<string>();
    }

    public class LoaderManifestEntry
    {
        /// <summary>The path as the game process sees it (Z:\... under Proton); this is what the loader reads.</summary>
        public string DllPath { get; set; }
        /// <summary>The same file as the launcher itself sees it; not written to the manifest.</summary>
        [JsonIgnore]
        public string HostDllPath { get; set; }
        public string EntryType { get; set; }
        public string SettingsJson { get; set; }
        public string DisplayName { get; set; }
    }
}
