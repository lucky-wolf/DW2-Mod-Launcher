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
        public string GlobalLaunchArguments { get; set; }
        /// <summary>Environment variables set on the game process (e.g. imported from Steam's launch options).</summary>
        public Dictionary<string, string> LaunchEnvironment { get; set; }
        /// <summary>The Play button's remembered mode (run / continue / new game).</summary>
        public LaunchMode LaunchMode { get; set; }
        public string LastWorkshopUpdateCheckUtc { get; set; }
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
        /// <summary>Per mod (id, else folder name): headings of the settings editor groups the user collapsed.</summary>
        public Dictionary<string, List<string>> CollapsedSettingGroups { get; set; } = new Dictionary<string, List<string>>();

        public LauncherSettings()
        {
            SortColumn = -1;
            SortAscending = true;
            Language = "en";
            GameRoot = "";
            WorkshopRoot = "";
            ManagedModsRoot = "";
            GlobalLaunchArguments = "";
            LaunchEnvironment = new Dictionary<string, string>();
            LastWorkshopUpdateCheckUtc = "";
            SelectedMods = new Dictionary<string, bool>();
            ActiveProfile = "";
        }
    }
}
