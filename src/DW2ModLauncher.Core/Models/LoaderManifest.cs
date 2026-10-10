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

        /// <summary>Folder names (the game's /mods/Name/ or /steam/Id/) of every enabled mod, in load order; a patch set's Order is an index into it.</summary>
        public List<string> ModFolders { get; set; } = new List<string>();

        /// <summary>Font bundles declared by enabled mods (dw2modlauncher.json "font"), in load order; the last one is the one passed to the game as --font.</summary>
        public List<LoaderManifestFont> Fonts { get; set; } = new List<LoaderManifestFont>();

        /// <summary>Text files the game reads straight from its data folder (Hints.txt, dialog, Galactopedia), which the loader serves from the mods instead.</summary>
        public List<LoaderManifestTextFile> TextFiles { get; set; } = new List<LoaderManifestTextFile>();

        /// <summary>True when a mod asked (dw2modlauncher.json "galactopedia": "replace") for the game's own Galactopedia articles to be dropped.</summary>
        public bool GalactopediaReplacesVanilla { get; set; }

        /// <summary>Where the loader writes its logs (as the game process sees the path); blank = the game's data/Logs folder.</summary>
        public string LogDirectory { get; set; } = "";
    }

    /// <summary>One mod's XML patch files (see docs/plans/xml-patching.md), in the order they apply.</summary>
    public class LoaderManifestPatchSet
    {
        public string DisplayName { get; set; }
        /// <summary>The mod's position in <see cref="LoaderManifest.ModFolders"/>: the patches apply to data of this mod and of the ones before it.</summary>
        public int Order { get; set; }
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

    /// <summary>A mod's replacement for a text file the game reads from its data folder; the last mod in load order wins per Relative.</summary>
    public class LoaderManifestTextFile
    {
        /// <summary>Path below the data folder with forward slashes, e.g. Hints.txt or dialog/zenox.txt.</summary>
        public string Relative { get; set; }
        /// <summary>The mod's file as the game process sees it.</summary>
        public string Path { get; set; }
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
