using System;
using System.Collections.Generic;

namespace DW2ModLauncher.Loader
{
    // The loader's own words for the status widget and the Mods menu. English is built in, for a loader injected without the launcher
    // and for tests; the launcher writes the player's language into the manifest (Strings: the Widget* keys of its language packs) and
    // Use replaces the table with it. A key the launcher's pack lacks arrives as its own name, so a gap in a translation is visible
    // in the game, as in the launcher. Text that mods report (details, errors, summaries) never passes through here.
    public static class StatusStrings
    {
        // Keys and English text; the same keys are in the launcher's en.json (a test keeps the two equal).
        public static readonly IReadOnlyDictionary<string, string> English = new Dictionary<string, string>
        {
            ["WidgetNoneLoaded"] = "DW2 mods: none loaded",
            ["WidgetLoadedCount"] = "DW2 mods: {0} loaded",
            ["WidgetDismissedLine"] = "DW2 mods: {0} loaded (problems dismissed)",
            ["WidgetBuilt"] = "(built {0})",
            ["WidgetSingleError"] = "{0} ERROR: ",
            ["WidgetSingleWarning"] = "{0} warning: ",
            ["WidgetOneModProblem"] = "1 mod with a problem: {0}: ",
            ["WidgetOneModWarning"] = "1 mod with a warning: {0}: ",
            ["WidgetManyModsProblems"] = "{0} mods with problems: {1}: ",
            ["WidgetMoreProblems"] = " (+{0} more)",
            ["WidgetMoreMods"] = " (+{0} more mods)",
            ["WidgetStateFailed"] = "FAILED TO LOAD",
            ["WidgetStateError"] = "ERROR",
            ["WidgetStateWarning"] = "warning",
            ["WidgetStateLoaded"] = "loaded",
            ["WidgetFailedToLoad"] = "failed to load: {0}",
            ["WidgetSeeDetails"] = "see details",
            ["WidgetFeatures"] = "features: {0}",
            ["WidgetLog"] = "log: {0}",
            ["WidgetHeader"] = "DW2 Mod Launcher: code mods loaded: {0}. Click a mod for details, anywhere else to close.",
            ["WidgetDismiss"] = "[Dismiss warnings and errors]  (the line stops showing them until something new is reported)",
            ["WidgetMinimize"] = "[Minimize the status line]",
            ["WidgetShowLine"] = "[Show the full status line]",
            ["WidgetPreviousPage"] = "[Previous mods]  (page {0} of {1})",
            ["WidgetNextPage"] = "[More mods]  (page {0} of {1}, {2} in all)",
            ["WidgetCopied"] = "[Copied to clipboard]",
            ["WidgetMenuMods"] = "Mods",
            ["WidgetMenuHeader"] = "MODS",
            ["WidgetMenuClose"] = "Close",
            ["WidgetMenuSeed"] = "Random Seed",
            ["WidgetMenuCopied"] = "Copied to clipboard",
            ["WidgetLoaderName"] = "Mod Launcher (loader)",
            ["WidgetLogFolderCreate"] = "the game's log folder could not be created ({0}); the loader logs are in {1}",
            ["WidgetLogFolderMissing"] = "log folder '{0}' does not exist; the loader logs are in {1}",
            ["WidgetLogCannotBeWritten"] = "A loader log cannot be written ({0}): {1}",
            ["WidgetChannelProblem"] = "Mod data channel: {0}",
            ["WidgetLoadIncomplete"] = "incomplete manifest entry",
            ["WidgetLoadDllMissing"] = "DLL not found at {0}",
            ["WidgetLoadEntryForm"] = "entry type '{0}' is not Namespace.Type.Method form",
            ["WidgetLoadTypeMissing"] = "type '{0}' not found",
            ["WidgetLoadNoMethod"] = "no '{0}()' or 'InitWithOptions(string)' on {1}",
            ["WidgetFontNoFileSystem"] = "font hook failed: the game's file system was not found, so the mod's font will not load",
            ["WidgetFontNoFileExists"] = "font hook failed: VirtualFileSystem.FileExists not found (game update?)",
            ["WidgetFontNoOpenStream"] = "font hook failed: VirtualFileSystem.OpenStream not found (game update?)",
            ["WidgetTextNoProvider"] = "text file hook failed: the game's data provider was not found (Hints, dialog, Galactopedia stay unreplaced)",
            ["WidgetTextNoApi"] = "text file hook failed: no file API could be hooked",
            ["WidgetTitlesNoGetText"] = "translated tour/article titles are not matched: TextResolver.GetText not found",
            ["WidgetTitlesMethodMissing"] = "translated titles are not matched: {0} not found (game update?)",
            ["WidgetPatchesNoFileSystem"] = "XML patches are not applied: the game's file system was not found",
            ["WidgetPatchesNoOpenStream"] = "XML patches are not applied: OpenStream could not be hooked",
            ["WidgetToursNotPatched"] = "tutorial tours are not patched: the data provider could not be hooked",
            ["WidgetPatchProblems"] = "XML patches: {0} item(s) skipped, {1} warning(s) - see dw2modlauncher-patches.log",
            ["WidgetPatchLogUnwritable"] = "XML patch log cannot be written: {0}",
            ["WidgetMenuHandlerThrew"] = "menu handler '{0}' threw",
            ["WidgetMenuCallFailed"] = "menu call failed",
            ["WidgetAbout"] = "Version {0}\n\nCode mods loaded: {1}. The line at the lower left of the screen lists them; click it for details.\n\nLog: data/Logs/dw2modlauncher.log",
        };

        static IReadOnlyDictionary<string, string> _table = English;

        // An empty or missing table keeps English.
        public static void Use(IReadOnlyDictionary<string, string> strings)
        {
            _table = strings != null && strings.Count > 0 ? strings : English;
        }

        public static string Get(string key, params object[] args)
        {
            // a key the table lacks shows as its own name
            string format = _table.TryGetValue(key, out string text) ? text : key;
            return args.Length == 0 ? format : string.Format(format, args);
        }
    }
}
