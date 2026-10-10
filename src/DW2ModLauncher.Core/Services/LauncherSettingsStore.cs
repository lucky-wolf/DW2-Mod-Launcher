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

        /// <summary>Why the last Load() fell back to defaults, or null when it read the file (or there was none).</summary>
        public string LoadError { get; private set; }

        /// <summary>A missing file yields default settings. A file that exists but cannot be read is copied to a
        /// ".bad" file (so the next Save cannot destroy it), logged, described in LoadError, and defaults are used.</summary>
        public LauncherSettings Load()
        {
            LoadError = null;
            if (!File.Exists(path)) return new LauncherSettings();
            try
            {
                LauncherSettings s = JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(path, Encoding.UTF8));
                if (s == null) throw new InvalidDataException("The file is empty or contains only null.");
                if (s.SelectedMods == null) s.SelectedMods = new Dictionary<string, bool>();
                return s;
            }
            catch (Exception ex) when (ex is JsonException || ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException)
            {
                Diagnostics.Logger.LogException("Load launcher settings: " + path, ex);
                string detail = ex.Message;
                try
                {
                    string bad = path + ".bad";
                    File.Copy(path, bad, true);
                    LoadError = "launcher_settings.json could not be read, so default settings are in use. Your original file was kept as " + bad + ".\n" + detail;
                }
                catch (Exception copyEx)
                {
                    Diagnostics.Logger.LogException("Back up unreadable launcher settings", copyEx);
                    LoadError = "launcher_settings.json could not be read and could not be backed up (" + copyEx.Message + "); saving will overwrite it.\n" + detail;
                }
                return new LauncherSettings();
            }
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
