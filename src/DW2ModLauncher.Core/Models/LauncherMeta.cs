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

        /// <summary>"replace": this mod's Galactopedia articles replace those of the game and of the mods before it (instead of being added to them). For translations.</summary>
        public string galactopedia { get; set; }

        /// <summary>The oldest launcher version this mod works with (e.g. "1.0.6"; a leading "v" and any "-dev" suffix are ignored). Older launchers flag the mod before launch.</summary>
        public string minLauncherVersion { get; set; }
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
