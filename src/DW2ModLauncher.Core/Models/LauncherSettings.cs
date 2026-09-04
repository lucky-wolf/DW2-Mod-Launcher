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
        public string LastWorkshopUpdateCheckUtc { get; set; }
        public Dictionary<string, bool> SelectedMods { get; set; }
        public string ActiveProfile { get; set; }

        public LauncherSettings()
        {
            Language = "ja";
            GameRoot = "";
            WorkshopRoot = "";
            ManagedModsRoot = "";
            GlobalLaunchArguments = "";
            LastWorkshopUpdateCheckUtc = "";
            SelectedMods = new Dictionary<string, bool>();
            ActiveProfile = "";
        }
    }
}
