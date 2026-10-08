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

        /// <summary>Font bundles declared by enabled mods (dw2modlauncher.json "font"), in load order; the last one is the one passed to the game as --font.</summary>
        public List<LoaderManifestFont> Fonts { get; set; } = new List<LoaderManifestFont>();
    }

    /// <summary>One mod's XML patch files (see docs/plans/xml-patching.md), in the order they apply.</summary>
    public class LoaderManifestPatchSet
    {
        public string DisplayName { get; set; }
        /// <summary>Paths as the game process sees them (Z:\... under Proton).</summary>
        public List<string> Files { get; set; } = new List<string>();
    }

    /// <summary>A font bundle shipped by a mod: the game's --font looks in data/db/bundles only, so the loader serves Name*.bundle from Folder.</summary>
    public class LoaderManifestFont
    {
        public string DisplayName { get; set; }
        /// <summary>Bundle name without extension, e.g. RussianFont.</summary>
        public string Name { get; set; }
        /// <summary>The mod's content folder as the game process sees it.</summary>
        public string Folder { get; set; }
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
