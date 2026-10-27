using System;
using System.Collections.Generic;
using DW2ModLauncher.Core.Services;

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
            ["WidgetLoadedCount"] = "DW2 mods: {Count} loaded",
            ["WidgetDismissedLine"] = "DW2 mods: {Count} loaded (problems dismissed)",
            ["WidgetSingleError"] = "{Mod} ERROR: {Problem}{More}",
            ["WidgetSingleWarning"] = "{Mod} warning: {Problem}{More}",
            ["WidgetOneModProblem"] = "1 mod with a problem: {Mod}: {Problem}{More}",
            ["WidgetOneModWarning"] = "1 mod with a warning: {Mod}: {Problem}{More}",
            ["WidgetManyModsProblems"] = "{Count} mods with problems: {Mod}: {Problem}{More}",
            ["WidgetMoreProblems"] = " (+{Count} more)",
            ["WidgetMoreMods"] = " (+{Count} more mods)",
            ["WidgetFailedToLoad"] = "failed to load: {Problem}",
            ["WidgetSeeDetails"] = "see details",
            ["WidgetFeatures"] = "features: {Features}",
            ["WidgetLog"] = "log: {File}",
            ["WidgetHeader"] = "DW2 Mod Launcher: code mods loaded: {Count}. Click a mod for details, anywhere else to close.",
            ["WidgetDismiss"] = "[Dismiss warnings and errors]  (the line stops showing them until something new is reported)",
            ["WidgetMinimize"] = "[Minimize the status line]",
            ["WidgetShowLine"] = "[Show the full status line]",
            ["WidgetPreviousPage"] = "[Previous mods]  (page {Page} of {Pages})",
            ["WidgetNextPage"] = "[More mods]  (page {Page} of {Pages}, {Count} in all)",
            ["WidgetCopied"] = "[Copied to clipboard]",
            ["WidgetMenuMods"] = "Mods",
            ["WidgetMenuHeader"] = "MODS",
            ["WidgetMenuClose"] = "Close",
            ["WidgetMenuBack"] = "Back",
            ["WidgetMenuSeed"] = "Random Seed",
            ["WidgetMenuCopied"] = "Copied to clipboard",
            ["WidgetLoaderName"] = "Mod Launcher (loader)",
            ["WidgetLogFolderCreate"] = "the game's log folder could not be created ({Problem}); the loader logs are in {LogFolder}",
            ["WidgetLogFolderMissing"] = "log folder '{Folder}' does not exist; the loader logs are in {LogFolder}",
            ["WidgetLogCannotBeWritten"] = "A loader log cannot be written ({Problem}): {File}",
            ["WidgetChannelProblem"] = "Mod data channel: {Problem}",
            ["WidgetChannelVersionMismatch"] = "{Mod} uses live channel '{Channel}' v{Wanted} but {Provider} provides v{Provided}",
            ["WidgetChannelTypeMismatch"] = "{Mod} expects live channel '{Channel}' to be a {Expected} but {Provider} provides a {Provided}",
            ["WidgetObservedHooksFailed"] = "The list of patched game code could not be saved to {File} ({Problem}); the launcher cannot warn about overlapping mods",
            ["WidgetLoadIncomplete"] = "incomplete manifest entry",
            ["WidgetLoadDllMissing"] = "DLL not found at {File}",
            ["WidgetLoadEntryForm"] = "entry type '{EntryType}' is not Namespace.Type.Method form",
            ["WidgetLoadTypeMissing"] = "type '{Type}' not found",
            ["WidgetLoadNoMethod"] = "no '{Method}()' or 'InitWithOptions(string)' on {Type}",
            ["WidgetFontNoFileSystem"] = "font hook failed: the game's file system was not found, so the mod's font will not load",
            ["WidgetFontNoFileExists"] = "font hook failed: VirtualFileSystem.FileExists not found (game update?)",
            ["WidgetFontNoOpenStream"] = "font hook failed: VirtualFileSystem.OpenStream not found (game update?)",
            ["WidgetTextNoProvider"] = "text file hook failed: the game's data provider was not found (Hints, dialog, Galactopedia stay unreplaced)",
            ["WidgetTextNoApi"] = "text file hook failed: no file API could be hooked",
            ["WidgetTitlesNoGetText"] = "translated tour/article titles are not matched: TextResolver.GetText not found",
            ["WidgetTitlesMethodMissing"] = "translated titles are not matched: {Method} not found (game update?)",
            ["WidgetGameStateNotHooked"] = "the start of a game is not tracked: {Method} not found (game update?); mods that wait for a real game will not warn",
            ["WidgetGameStateNotTracked"] = "the start of a game is not tracked: {Problem}; mods that wait for a real game will not warn",
            ["WidgetListSeparator"] = ", ",
            ["WidgetPatchesNoFileSystem"] = "XML patches are not applied: the game's file system was not found",
            ["WidgetPatchesNoOpenStream"] = "XML patches are not applied: OpenStream could not be hooked",
            ["WidgetToursNotPatched"] = "tutorial tours are not patched: the data provider could not be hooked",
            ["WidgetPatchProblems"] = "XML patches: items skipped: {Skipped}, warnings: {Warnings} - see dw2modlauncher-patches.log",
            ["WidgetPatchLogUnwritable"] = "XML patch log cannot be written: {Problem}",
            ["WidgetMenuHandlerThrew"] = "menu handler '{Handler}' threw",
            ["WidgetMenuCallFailed"] = "menu call failed",
            ["WidgetAbout"] = "Version {Version}\n\nCode mods loaded: {Loaded}. The line at the lower left of the screen lists them; click it for details.\n\nLog: data/Logs/dw2modlauncher.log",
            ["WidgetHeadingPlain"] = "{Mod}",
            ["WidgetHeadingVersion"] = "{Mod} {Version}",
            ["WidgetHeadingBuilt"] = "{Mod} (built {Built})",
            ["WidgetHeadingVersionBuilt"] = "{Mod} {Version} (built {Built})",
            ["WidgetBlockFailed"] = "{Heading}  FAILED TO LOAD",
            ["WidgetBlockError"] = "{Heading}  ERROR",
            ["WidgetBlockWarning"] = "{Heading}  warning",
            ["WidgetBlockLoaded"] = "{Heading}  loaded",
        };

        static IReadOnlyDictionary<string, string> _table = English;

        // An empty or missing table keeps English.
        public static void Use(IReadOnlyDictionary<string, string> strings)
        {
            _table = strings != null && strings.Count > 0 ? strings : English;
        }

        public static string Get(string key, params (string Name, object Value)[] values)
        {
            // a key the table lacks shows as its own name
            string format = _table.TryGetValue(key, out string text) ? text : key;
            return NamedText.Fill(format, values);
        }
    }
}
