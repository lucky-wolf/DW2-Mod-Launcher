using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DW2ModLauncher.Core.Diagnostics;
using DW2ModLauncher.Core.Models;
using DW2ModLauncher.Core.Services;

namespace DW2ModLauncher.App
{
    public partial class MainForm
    {
        private LauncherSettings LoadSettings() { return settingsStore.Load(); }

        private void SaveSettings()
        {
            try { settingsStore.Save(settings); }
            catch (Exception ex) { SetStatus("Settings save error: " + ex.Message); }
        }

        private void SaveSettingsFromUi()
        {
            EnsureSettingsState();
            string game = gameRootBox == null ? settings.GameRoot : (gameRootBox.Text ?? "").Trim();
            string workshop = workshopRootBox == null ? settings.WorkshopRoot : (workshopRootBox.Text ?? "").Trim();
            string managed = managedRootBox == null ? settings.ManagedModsRoot : (managedRootBox.Text ?? "").Trim();
            switch (LauncherSettingsStore.Validate(game, workshop, managed))
            {
                case SettingsProblem.GameFolderInvalid: MessageBox.Show(T("SelectGameFolderHint"), Text); return;
                case SettingsProblem.WorkshopFolderMissing: MessageBox.Show(T("WorkshopFolderMissing"), Text); return;
                case SettingsProblem.ManagedFolderMissing: MessageBox.Show(T("TheDW2ModFolderDoesNotExist"), Text); return;
            }
            settings.GameRoot = game;
            settings.WorkshopRoot = workshop;
            settings.ManagedModsRoot = managed;
            if (launchArgsBox != null) settings.GlobalLaunchArguments = (launchArgsBox.Text ?? "").Trim();
            if (launchEnvBox != null) settings.LaunchEnvironment = GameLauncher.ParseEnvironment(launchEnvBox.Text);
            SaveSettings();
            UpdatePathLabels();
            UpdateCommandPreview();
        }

        private string SafeFileName(string value) { return FileNames.Safe(value); }

        private void RefreshProfileCombo()
        {
            if (profileCombo == null) return;
            string selected = settings == null ? "" : settings.ActiveProfile ?? "";
            profileCombo.Items.Clear();
            foreach (string name in profileStore.ListNames()) profileCombo.Items.Add(name);
            if (!string.IsNullOrWhiteSpace(selected)) profileCombo.Text = selected;
        }

        private void SaveCurrentProfile()
        {
            string name = profileCombo == null ? "" : (profileCombo.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show(T("EnterAProfileName"), Text);
                return;
            }
            ModProfile profile = ProfileStore.Capture(
                name,
                modOrder.Order,
                launchArgsBox == null ? settings.GlobalLaunchArguments : launchArgsBox.Text.Trim(),
                (currentManagedMods ?? new List<ModInfo>()).Concat(currentWorkshopMods ?? new List<ModInfo>()));
            try
            {
                profileStore.Save(profile);
                settings.ActiveProfile = name;
                settings.GlobalLaunchArguments = profile.ManualLaunchArguments ?? "";
                SaveSettings();
                RefreshProfileCombo();
                SetStatus(T("ModProfileSaved") + name);
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, Text); }
        }

        private void ApplySelectedProfile()
        {
            string name = profileCombo == null ? "" : (profileCombo.Text ?? "").Trim();
            if (!profileStore.Exists(name)) { MessageBox.Show(T("ProfileNotFound"), Text); return; }
            if (IsGameRunning()) { MessageBox.Show(T("CloseGameBeforeProfileSwitch"), Text); return; }
            try
            {
                ModProfile profile = profileStore.Load(name);
                if (profile == null) return;
                List<string> versionChanges = new List<string>();
                foreach (ProfileVersionChange change in ProfileStore.CompareVersions(profile, (currentManagedMods ?? new List<ModInfo>()).Concat(currentWorkshopMods ?? new List<ModInfo>())))
                {
                    if (change.Installed == null) versionChanges.Add(T("NotInstalled") + change.Token);
                    else versionChanges.Add((change.Installed.DisplayName ?? change.Installed.Id) + ": " + change.SavedVersion + " → " + (change.Installed.Version ?? "?"));
                }
                if (versionChanges.Count > 0 && MessageBox.Show(
                    T("ProfileVersionMismatch") + string.Join("\r\n", versionChanges.Take(20).ToArray()) +
                    T("ApplyTheProfile"), T("VersionDifferences"),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                WriteModOrder(profile.Order ?? new List<string>());
                settings.ActiveProfile = profile.Name ?? name;
                settings.GlobalLaunchArguments = profile.ManualLaunchArguments ?? "";
                if (launchArgsBox != null) launchArgsBox.Text = settings.GlobalLaunchArguments;
                SaveSettings();
                RefreshAll();
                SetStatus(T("SwitchedModProfile") + settings.ActiveProfile);
            }
            catch (Exception ex) { Logger.LogException("Apply profile", ex); MessageBox.Show(ex.Message, Text); }
        }

        private void DeleteSelectedProfile()
        {
            string name = profileCombo == null ? "" : (profileCombo.Text ?? "").Trim();
            try { profileStore.Delete(name); if (settings.ActiveProfile == name) settings.ActiveProfile = ""; SaveSettings(); RefreshProfileCombo(); }
            catch (Exception ex) { MessageBox.Show(ex.Message, Text); }
        }

        private string SnapshotsRoot() { return Path.Combine(appRoot, "Snapshots"); }

        private void CreateEnvironmentSnapshot()
        {
            try
            {
                string root = SnapshotStore.Create(SnapshotsRoot(), ModsJsonPath(), settingsPath,
                    (currentManagedMods ?? new List<ModInfo>()).Concat(currentWorkshopMods ?? new List<ModInfo>()).Where(IsModSelected).ToList());
                MessageBox.Show(T("SnapshotSaved") + root, Text);
            }
            catch (Exception ex) { Logger.LogException("Create snapshot", ex); MessageBox.Show(ex.Message, Text); }
        }

        private void RestoreLatestSnapshot()
        {
            string root = SnapshotStore.FindLatest(SnapshotsRoot());
            if (string.IsNullOrWhiteSpace(root)) { MessageBox.Show(T("NoSnapshotIsAvailable"), Text); return; }
            if (IsGameRunning()) { MessageBox.Show(T("CloseDW2BeforeRestoring"), Text); return; }
            if (MessageBox.Show(T("RestoreTheLatestSnapshot") + root,
                T("RestoreSnapshot"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            try
            {
                SnapshotStore.Restore(root, ModsJsonPath(), settingsPath);
                settings = LoadSettings();
                EnsureSettingsState();
                RefreshAll();
                MessageBox.Show(T("LatestSnapshotRestored"), Text);
            }
            catch (Exception ex) { Logger.LogException("Restore snapshot", ex); MessageBox.Show(ex.Message, Text); }
        }

        private void CopyDirectory(string source, string destination) { FileNames.CopyDirectory(source, destination); }

        private void DetectPaths(bool overwrite)
        {
            PathDetector.Detect(settings, overwrite);

            SaveSettings();
            UpdatePathLabels();
            if (gameRootBox != null) gameRootBox.Text = settings.GameRoot;
            if (workshopRootBox != null) workshopRootBox.Text = settings.WorkshopRoot;
            if (managedRootBox != null) managedRootBox.Text = settings.ManagedModsRoot;
            if (launchArgsBox != null) launchArgsBox.Text = settings.GlobalLaunchArguments ?? "";
            if (launchEnvBox != null) launchEnvBox.Text = GameLauncher.FormatEnvironment(settings.LaunchEnvironment);
        }

        private void UpdatePathLabels()
        {
            if (gamePathLabel != null) gamePathLabel.Text = "Game: " + (string.IsNullOrEmpty(settings.GameRoot) ? "Not found" : settings.GameRoot);
            if (workshopPathLabel != null) workshopPathLabel.Text = "Workshop: " + (string.IsNullOrEmpty(settings.WorkshopRoot) ? "Not found" : settings.WorkshopRoot);
        }

    }
}
