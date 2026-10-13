using System.Collections.Generic;

namespace DW2ModLauncher.Core.Models
{
    public class LauncherMeta
    {
        public LauncherInjection injection { get; set; }

        /// <summary>
        /// Name of a font bundle the mod ships (e.g. "RussianFont" for RussianFont.bundle in the mod folder, also listed in mod.json "bundles").
        /// The launcher starts the game with --font and makes the game find the bundle in the mod folder. Used by localization mods
        /// for non-Latin text; when several enabled mods declare one, the last in load order wins.
        /// </summary>
        public string font { get; set; }

        /// <summary>The language code of a translation mod (the name of a launcher language pack, e.g. "ru"). The in-game widget and Mods menu use the pack of the last enabled mod that declares one.</summary>
        public string language { get; set; }

        /// <summary>Who made the mod, as a name shown in the launcher's Author column. The launcher stamps it from the user's Author setting when they create or publish a mod.</summary>
        public string author { get; set; }

        /// <summary>"replace": this mod's Galactopedia articles replace those of the game and of the mods before it (instead of being added to them). For translations.</summary>
        public string galactopedia { get; set; }

        /// <summary>The oldest launcher version this mod works with (e.g. "1.0.6"; a leading "v" and any "-dev" suffix are ignored). Older launchers flag the mod before launch.</summary>
        public string minLauncherVersion { get; set; }

        /// <summary>The other mods this mod depends on: whether it needs them, its load order against them, the version it needs, and the text files it wants from them. See docs/plans/mod-dependencies.md.</summary>
        public List<LauncherDependency> dependencies { get; set; }

        /// <summary>Data this mod supplies to other mods' DLLs over named channels (see ModChannels and docs/DLL Injection.md).</summary>
        public List<LauncherProvision> provides { get; set; }

        /// <summary>The channels this mod's DLL receives data on, in the second argument of InitWithOptions(string, IReadOnlyDictionary).</summary>
        public List<string> receives { get; set; }
    }

    /// <summary>One entry of dw2modlauncher.json "provides": the files this mod fills a channel's slots with.</summary>
    public class LauncherProvision
    {
        /// <summary>The channel, e.g. "coherent-research.profiles". Its owner documents the slots and their formats.</summary>
        public string channel { get; set; }

        /// <summary>Slot name (as the channel defines it) to the path of this mod's file for it, relative to the mod's folder.</summary>
        public Dictionary<string, string> files { get; set; }
    }

    /// <summary>One entry of dw2modlauncher.json "dependencies": what a mod needs from one other mod.</summary>
    public class LauncherDependency
    {
        /// <summary>The other mod, matched against its id, display name, folder name or token, ignoring case.</summary>
        public string mod { get; set; }

        /// <summary>True: the mod does not work without it (a launch warning when it is missing). False: the entry applies only when the other mod is enabled.</summary>
        public bool required { get; set; }

        /// <summary>The oldest version of the other mod that works (compared with the version in its mod.json).</summary>
        public string minVersion { get; set; }

        /// <summary>This mod loads after the other one (when the other one is enabled).</summary>
        public bool loadAfter { get; set; }

        /// <summary>This mod loads before the other one (when the other one is enabled).</summary>
        public bool loadBefore { get; set; }

    }

    // Optional manual override of injection discovery (normally inferred, see InjectionScanner):
    // dll is a path relative to the mod's own content folder; entryPoint is the
    // Namespace.Type.Method the loader should invoke on it.
    public class LauncherInjection
    {
        public string dll { get; set; }
        public string entryPoint { get; set; }
    }
}
