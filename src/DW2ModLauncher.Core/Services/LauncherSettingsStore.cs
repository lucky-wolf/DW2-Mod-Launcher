using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using DW2ModLauncher.Core.Models;

namespace DW2ModLauncher.Core.Services
{
    public enum SettingsProblem
    {
        None,
        GameFolderInvalid,
        WorkshopFolderMissing,
        ManagedFolderMissing
    }

    /// <summary>Reads and writes launcher_settings.json. No UI: callers decide how to report failures.</summary>
    public class LauncherSettingsStore
    {
        private readonly string path;

        public LauncherSettingsStore(string path)
        {
            this.path = path;
        }

        public string Path { get { return path; } }

        /// <summary>Never throws: a missing or unreadable file yields default settings.</summary>
        public LauncherSettings Load()
        {
            try
            {
                if (File.Exists(path))
                {
                    LauncherSettings s = JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(path, Encoding.UTF8));
                    if (s != null)
                    {
                        if (s.SelectedMods == null) s.SelectedMods = new Dictionary<string, bool>();
                        return s;
                    }
                }
            }
            catch { }
            return new LauncherSettings();
        }

        /// <summary>Throws on I/O failure.</summary>
        public void Save(LauncherSettings settings)
        {
            File.WriteAllText(path, JsonSerializer.Serialize(settings), new UTF8Encoding(true));
        }

        /// <summary>Replaces null fields with defaults. Language validity is the caller's concern.</summary>
        public static void Normalize(LauncherSettings settings)
        {
            if (settings.SelectedMods == null) settings.SelectedMods = new Dictionary<string, bool>();
            if (string.IsNullOrWhiteSpace(settings.Language)) settings.Language = "en";
            if (settings.GameRoot == null) settings.GameRoot = "";
            if (settings.WorkshopRoot == null) settings.WorkshopRoot = "";
            if (settings.ManagedModsRoot == null) settings.ManagedModsRoot = "";
            if (settings.GlobalLaunchArguments == null) settings.GlobalLaunchArguments = "";
            if (settings.LaunchEnvironment == null) settings.LaunchEnvironment = new Dictionary<string, string>();
            if (settings.LastWorkshopUpdateCheckUtc == null) settings.LastWorkshopUpdateCheckUtc = "";
            if (settings.ActiveProfile == null) settings.ActiveProfile = "";
        }

        /// <summary>Blank folders are allowed (unset); non-blank ones must exist / look right.</summary>
        public static SettingsProblem Validate(string gameRoot, string workshopRoot, string managedModsRoot)
        {
            if (!string.IsNullOrWhiteSpace(gameRoot) && !SteamLocator.IsGameRoot(gameRoot)) return SettingsProblem.GameFolderInvalid;
            if (!string.IsNullOrWhiteSpace(workshopRoot) && !Directory.Exists(workshopRoot)) return SettingsProblem.WorkshopFolderMissing;
            if (!string.IsNullOrWhiteSpace(managedModsRoot) && !Directory.Exists(managedModsRoot)) return SettingsProblem.ManagedFolderMissing;
            return SettingsProblem.None;
        }
    }
}
