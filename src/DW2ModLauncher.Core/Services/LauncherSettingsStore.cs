using System;
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
        ManagedFolderMissing,
        LogFolderMissing
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
            if (settings.LogDirectory == null) settings.LogDirectory = "";
            if (settings.LastWorkshopUpdateCheckUtc == null) settings.LastWorkshopUpdateCheckUtc = "";
            if (settings.ActiveProfile == null) settings.ActiveProfile = "";
            settings.GameRoot = NativePath(settings.GameRoot);
            settings.WorkshopRoot = NativePath(settings.WorkshopRoot);
            settings.ManagedModsRoot = NativePath(settings.ManagedModsRoot);
        }

        /// <summary>On Windows, turns "c:/steam\steamapps" (a mix Steam's registry value produced in older settings files) into backslashes only.</summary>
        internal static string NativePath(string path)
        {
            if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(path)) return path;
            try { return System.IO.Path.GetFullPath(path); }
            catch { return path; }
        }

        /// <summary>Blank folders are allowed (unset); non-blank ones must exist / look right.</summary>
        public static SettingsProblem Validate(string gameRoot, string workshopRoot, string managedModsRoot, string logFolder = "")
        {
            if (!string.IsNullOrWhiteSpace(gameRoot) && !SteamLocator.IsGameRoot(gameRoot)) return SettingsProblem.GameFolderInvalid;
            if (!string.IsNullOrWhiteSpace(workshopRoot) && !Directory.Exists(workshopRoot)) return SettingsProblem.WorkshopFolderMissing;
            if (!string.IsNullOrWhiteSpace(managedModsRoot) && !Directory.Exists(managedModsRoot)) return SettingsProblem.ManagedFolderMissing;
            if (!string.IsNullOrWhiteSpace(logFolder) && !Directory.Exists(logFolder)) return SettingsProblem.LogFolderMissing;
            return SettingsProblem.None;
        }
    }
}
