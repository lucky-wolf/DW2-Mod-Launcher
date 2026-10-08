using System;
using System.Collections.Generic;

namespace DW2ModLauncher.Core.Models
{
    public class LauncherSettings
    {
        public string Language { get; set; }
        public string GameRoot { get; set; }
        public string WorkshopRoot { get; set; }
        public string ManagedModsRoot { get; set; }
        /// <summary>The Play button's remembered mode (run / continue / new game).</summary>
        public LaunchMode LaunchMode { get; set; }
        public string LastWorkshopUpdateCheckUtc { get; set; }
        /// <summary>Ask GitHub for a newer launcher release at startup (and offer to install it).</summary>
        public bool CheckForLauncherUpdates { get; set; } = true;
        /// <summary>A launcher version the user chose "Skip this version" for; the startup check stays quiet about exactly that one.</summary>
        public string SkippedLauncherVersion { get; set; } = "";
        public Dictionary<string, bool> SelectedMods { get; set; }
        public string ActiveProfile { get; set; }
        /// <summary>Mod list sort column (0 name, 1 source, 2 state, 3 health, 4 load order); -1 = manual load order.</summary>
        public int SortColumn { get; set; }
        public bool SortAscending { get; set; }
        /// <summary>Main window's top-left corner when it last closed; null until first saved.</summary>
        public int? WindowX { get; set; }
        public int? WindowY { get; set; }
        /// <summary>Main window's client size in device-independent pixels when it last closed.</summary>
        public double? WindowWidth { get; set; }
        public double? WindowHeight { get; set; }
        /// <summary>The mod settings editor's "Show hidden" checkbox, restored the next time it opens.</summary>
        public bool ShowHiddenSettings { get; set; }
        /// <summary>ActiveToken of the mod selected in the list when the launcher last closed; reselected (and scrolled into view) at startup.</summary>
        public string LastSelectedMod { get; set; }
        /// <summary>Per mod (id, else folder name): headings of the settings editor groups the user collapsed.</summary>
        public Dictionary<string, List<string>> CollapsedSettingGroups { get; set; } = new Dictionary<string, List<string>>();

        /// <summary>Per mod (id, else folder name): how the publish dialog proposes the next version. Mods not listed use the defaults (auto-increment, patch).</summary>
        public Dictionary<string, VersionBumpPolicy> VersionBump { get; set; } = new Dictionary<string, VersionBumpPolicy>();

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
            SortColumn = -1;
            SortAscending = true;
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
