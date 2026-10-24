using System;
using System.Collections.Generic;

namespace DW2ModLauncher.Core.Models
{
    public class LauncherSettings
    {
        public string Language { get; set; }
        /// <summary>True: the in-game widget's language is the one an enabled translation mod declares (dw2modlauncher.json "language"), English if none does. False: it follows <see cref="Language"/>, the launcher's own.</summary>
        public bool WidgetLanguageFromMods { get; set; } = true;
        /// <summary>Code overlaps the player said are fine (HookOverlapAnalyzer.AcknowledgementKey: target, both mods and their versions).</summary>
        public List<string> AcknowledgedHooks { get; set; } = new List<string>();
        /// <summary>Steam user id to handle, for Workshop creators looked up as the author of mods that state none (SteamAuthors).</summary>
        public Dictionary<string, string> SteamNames { get; set; } = new Dictionary<string, string>();
        public string GameRoot { get; set; }
        public string WorkshopRoot { get; set; }
        public string ManagedModsRoot { get; set; }
        /// <summary>Folder the in-game loader writes its logs to; blank = the game's data/Logs folder.</summary>
        public string LogDirectory { get; set; } = "";
        /// <summary>The Play button's remembered mode (run / continue / new game).</summary>
        public LaunchMode LaunchMode { get; set; }
        public string LastWorkshopUpdateCheckUtc { get; set; }
        /// <summary>Ask GitHub for a newer launcher release at startup (and offer to install it).</summary>
        public bool CheckForLauncherUpdates { get; set; } = true;
        /// <summary>A launcher version the user chose "Skip this version" for; the startup check stays quiet about exactly that one.</summary>
        public string SkippedLauncherVersion { get; set; } = "";
        public Dictionary<string, bool> SelectedMods { get; set; }
        public string ActiveProfile { get; set; }
        /// <summary>The user's name as a mod author; stamped into dw2modlauncher.json of mods they create or publish. Null = never set, so the launcher fills in the handle of the Steam account logged in on this machine (SteamLoginUser); blank = the user chose no stamping.</summary>
        public string AuthorName { get; set; }
        /// <summary>The mod list shows only mods from the game's mods folder, hiding Steam Workshop copies.</summary>
        public bool LocalModsOnly { get; set; }
        /// <summary>Whether the details pane's conflict list is open; one choice for every mod.</summary>
        public bool ConflictsExpanded { get; set; }
        /// <summary>Whether the details pane's caution list is open; one choice for every mod.</summary>
        public bool CautionsExpanded { get; set; }
        /// <summary>Main window's top-left corner when it last closed; null until first saved.</summary>
        public int? WindowX { get; set; }
        public int? WindowY { get; set; }
        /// <summary>Main window's client size in device-independent pixels when it last closed.</summary>
        public double? WindowWidth { get; set; }
        public double? WindowHeight { get; set; }
        /// <summary>The order of the mod list's columns (ids from ModListColumns), as the user dragged them; null = the default order.</summary>
        public List<string> ModListColumnOrder { get; set; }
        /// <summary>The details pane's share (0 to 1) of the width the mod list and details pane split; null until the user drags the splitter.</summary>
        public double? DetailsFraction { get; set; }
        /// <summary>The details pane is hidden (a state of its own: collapsing never changes <see cref="DetailsFraction"/>).</summary>
        public bool DetailsCollapsed { get; set; }
        /// <summary>The mod settings editor's "Show hidden" checkbox, restored the next time it opens.</summary>
        public bool ShowHiddenSettings { get; set; }
        /// <summary>ActiveToken of the mod selected in the list when the launcher last closed; reselected (and scrolled into view) at startup.</summary>
        public string LastSelectedMod { get; set; }
        /// <summary>Extension of the description file a mod gets when it names none of its own (".bbcode", ".txt", or anything typed); see ModDescriptionFile.</summary>
        public string DescriptionExtension { get; set; } = ".bbcode";
        /// <summary>Publishing an update sends the description to Steam whenever it differs from the Steam page. Off: the author pushes it by hand (the cloud-upload button).</summary>
        public bool AutoUpdateDescription { get; set; } = true;
        /// <summary>On launching the game with unsaved mod profile edits: save them without asking (true, default), or ask.</summary>
        public bool AutoSaveProfileOnLaunch { get; set; } = true;
        /// <summary>On exit with unsaved mod profile edits: save them without asking (true), or ask (default).</summary>
        public bool AutoSaveProfileOnExit { get; set; }
        /// <summary>After a successful publish: ask whether to open the item's Steam page (default), always open it, or never.</summary>
        public OpenSteamPageAfterPublish OpenSteamPageAfterPublish { get; set; } = OpenSteamPageAfterPublish.Prompt;
        /// <summary>The publish dialog's modified-files section is unfolded (folded by default; remembered).</summary>
        public bool PublishStatusExpanded { get; set; }
        /// <summary>The publish dialog's change-notes section is unfolded (folded by default; remembered).</summary>
        public bool PublishChangeNotesExpanded { get; set; }
        /// <summary>The folder the last preview image was picked from (outside any mod), offered when the next pick starts from a blank image field.</summary>
        public string LastArtFolder { get; set; }
        /// <summary>Per mod (id, else folder name): headings of the settings editor groups the user collapsed.</summary>
        public Dictionary<string, List<string>> CollapsedSettingGroups { get; set; } = new Dictionary<string, List<string>>();

        /// <summary>Per mod (id, else folder name): how far down the settings editor was scrolled when it was closed, so it opens there again.</summary>
        public Dictionary<string, double> SettingsScrollOffsets { get; set; } = new Dictionary<string, double>();

        /// <summary>Per mod (id, else folder name): how the publish dialog proposes the next version. Mods not listed use the defaults (auto-increment, patch).</summary>
        public Dictionary<string, VersionBumpPolicy> VersionBump { get; set; } = new Dictionary<string, VersionBumpPolicy>();

        /// <summary>Per published local mod ("local/&lt;workshop id&gt;"): the DW2 mods.json token ("mods/&lt;folder&gt;") it had when last seen,
        /// so a renamed folder is recognised as the same mod and its load-order entry and stored state follow it.</summary>
        public Dictionary<string, string> LocalModTokens { get; set; } = new Dictionary<string, string>();

        public VersionBumpPolicy VersionBumpFor(string modKey)
        {
            return VersionBump != null && modKey != null && VersionBump.TryGetValue(modKey, out VersionBumpPolicy p) && p != null ? p : new VersionBumpPolicy();
        }

        /// <summary>Stores the policy, or drops the entry when it is the default so the file stays small.</summary>
        public void SetVersionBump(string modKey, VersionBumpPolicy policy)
        {
            if (VersionBump == null) VersionBump = new Dictionary<string, VersionBumpPolicy>();
            if (string.Equals(policy.Level, "patch", StringComparison.OrdinalIgnoreCase)) VersionBump.Remove(modKey);
            else VersionBump[modKey] = policy;
        }

        public LauncherSettings()
        {
            Language = "en";
            GameRoot = "";
            WorkshopRoot = "";
            ManagedModsRoot = "";
            LastWorkshopUpdateCheckUtc = "";
            SelectedMods = new Dictionary<string, bool>();
            ActiveProfile = "";
        }
    }
}
