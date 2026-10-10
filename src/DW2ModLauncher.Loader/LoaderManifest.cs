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
        public List<string> ModFolders { get; set; } = new List<string>();
        public List<LoaderManifestFont> Fonts { get; set; } = new List<LoaderManifestFont>();
        public List<LoaderManifestTextFile> TextFiles { get; set; } = new List<LoaderManifestTextFile>();
        public bool GalactopediaReplacesVanilla { get; set; }
        public string LogDirectory { get; set; }
    }

    public class LoaderManifestTextFile
    {
        public string Relative { get; set; }
        public string Path { get; set; }
    }

    public class LoaderManifestPatchSet
    {
        public string DisplayName { get; set; }
        public int Order { get; set; }
        public List<string> Files { get; set; } = new List<string>();
    }

    public class LoaderManifestFont
    {
        public string DisplayName { get; set; }
        public string Name { get; set; }
        public string Folder { get; set; }
    }

    public class LoaderManifestEntry
    {
        public string DllPath { get; set; }
        public string EntryType { get; set; }
        public string SettingsJson { get; set; }
        public string DisplayName { get; set; }
    }
}
