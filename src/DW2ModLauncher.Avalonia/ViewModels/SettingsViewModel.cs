using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DW2ModLauncher.Core.Diagnostics;
using DW2ModLauncher.Core.Models;
using DW2ModLauncher.Core.Services;

namespace DW2ModLauncher.Avalonia.ViewModels
{
    /// <summary>The Settings tab: folders, launch arguments, profiles and snapshots.</summary>
    public class SettingsViewModel : ViewModelBase
    {
        private readonly MainViewModel main;
        private string gameRoot = "";
        private string workshopRoot = "";
        private string managedRoot = "";
        private string launchArguments = "";
        private string launchEnvironment = "";
        private string profileName = "";

        public SettingsViewModel(MainViewModel main)
        {
            this.main = main;
            AutoDetectCommand = new RelayCommand(delegate
            {
                PathDetector.Detect(main.LauncherSettings, true);
                main.SaveSettings();
                main.Refresh();
            });
            SaveCommand = new RelayCommand(SaveAsync);
            ImportSteamLaunchOptionsCommand = new RelayCommand(ImportSteamLaunchOptionsAsync);
            ResetLaunchOptionsCommand = new RelayCommand(delegate { LaunchArguments = ""; LaunchEnvironment = ""; });
            BrowseGameCommand = new RelayCommand(async delegate { string p = await Browse(gameRoot); if (p != null) GameRoot = p; });
            BrowseWorkshopCommand = new RelayCommand(async delegate { string p = await Browse(workshopRoot); if (p != null) WorkshopRoot = p; });
            BrowseManagedCommand = new RelayCommand(async delegate { string p = await Browse(managedRoot); if (p != null) ManagedRoot = p; });
            OpenGameCommand = new RelayCommand(delegate { Open(main.LauncherSettings.GameRoot); });
            OpenWorkshopCommand = new RelayCommand(delegate { Open(main.LauncherSettings.WorkshopRoot); });
            OpenManagedCommand = new RelayCommand(delegate { Open(main.LauncherSettings.ManagedModsRoot); });
            SaveProfileCommand = new RelayCommand(SaveProfile);
            ApplyProfileCommand = new RelayCommand(ApplyProfileAsync);
            DeleteProfileCommand = new RelayCommand(DeleteProfile);
            SnapshotCommand = new RelayCommand(CreateSnapshotAsync);
            RestoreSnapshotCommand = new RelayCommand(RestoreSnapshotAsync);
        }

        public LocalizedStrings L { get { return main.L; } }

        public RelayCommand AutoDetectCommand { get; }
        public RelayCommand SaveCommand { get; }
        public RelayCommand ImportSteamLaunchOptionsCommand { get; }
        public RelayCommand ResetLaunchOptionsCommand { get; }
        public RelayCommand BrowseGameCommand { get; }
        public RelayCommand BrowseWorkshopCommand { get; }
        public RelayCommand BrowseManagedCommand { get; }
        public RelayCommand OpenGameCommand { get; }
        public RelayCommand OpenWorkshopCommand { get; }
        public RelayCommand OpenManagedCommand { get; }
        public RelayCommand SaveProfileCommand { get; }
        public RelayCommand ApplyProfileCommand { get; }
        public RelayCommand DeleteProfileCommand { get; }
        public RelayCommand SnapshotCommand { get; }
        public RelayCommand RestoreSnapshotCommand { get; }

        public ObservableCollection<string> ProfileNames { get; } = new ObservableCollection<string>();

        public string GameRoot { get { return gameRoot; } set { if (Set(ref gameRoot, value)) Raise(nameof(CommandPreview)); } }
        public string WorkshopRoot { get { return workshopRoot; } set { Set(ref workshopRoot, value); } }
        public string ManagedRoot { get { return managedRoot; } set { Set(ref managedRoot, value); } }
        public string LaunchArguments { get { return launchArguments; } set { if (Set(ref launchArguments, value)) Raise(nameof(CommandPreview)); } }
        /// <summary>True when the launcher starts the game itself (Windows); on Linux Steam owns the environment.</summary>
        public bool EnvironmentApplies { get { return OperatingSystem.IsWindows(); } }
        public bool EnvironmentIgnored { get { return !OperatingSystem.IsWindows(); } }
        public string LaunchEnvironment { get { return launchEnvironment; } set { Set(ref launchEnvironment, value); } }
        public string ProfileName { get { return profileName; } set { Set(ref profileName, value); } }

        public string CommandPreview
        {
            get
            {
                ProcessStartInfo psi = GameLauncher.BuildStartInfo(gameRoot, GameLauncher.BuildArguments((launchArguments ?? "").Trim()));
                return "\"" + psi.FileName + "\"" + (string.IsNullOrWhiteSpace(psi.Arguments) ? "" : " " + psi.Arguments);
            }
        }

        public void UpdateCommandPreview() { Raise(nameof(CommandPreview)); }

        /// <summary>Pulls the current settings into the editable fields.</summary>
        public void LoadFromSettings()
        {
            LauncherSettings s = main.LauncherSettings;
            GameRoot = s.GameRoot ?? "";
            WorkshopRoot = s.WorkshopRoot ?? "";
            ManagedRoot = s.ManagedModsRoot ?? "";
            LaunchArguments = s.GlobalLaunchArguments ?? "";
            LaunchEnvironment = GameLauncher.FormatEnvironment(s.LaunchEnvironment);
            RefreshProfiles();
            UpdateCommandPreview();
        }

        /// <summary>Writes the editable fields into the settings object without validating (used right before launch).</summary>
        public void CommitToSettings()
        {
            main.LauncherSettings.GlobalLaunchArguments = (launchArguments ?? "").Trim();
            main.LauncherSettings.LaunchEnvironment = GameLauncher.ParseEnvironment(launchEnvironment);
            main.SaveSettings();
        }

        private async Task<string> Browse(string start)
        {
            return await main.Dialogs.PickFolderAsync(main.T("SelectGameFolderHint"), start);
        }

        private async void Open(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
                {
                    await main.Dialogs.ShowMessageAsync(main.T("FolderNotFound"), "DW2 Mod Launcher");
                    return;
                }
                PlatformShell.Create().OpenFolder(path);
            }
            catch (Exception ex) { await main.Dialogs.ShowMessageAsync(ex.Message, "DW2 Mod Launcher"); }
        }

        private async Task ImportSteamLaunchOptionsAsync()
        {
            SteamLaunchOptions options = SteamLaunchOptions.ReadForApp(SteamLocator.AppId);
            if (options == null)
            {
                await main.Dialogs.ShowMessageAsync(main.T("SteamLaunchOptionsNone"), "DW2 Mod Launcher");
                return;
            }
            LaunchArguments = options.Arguments;
            LaunchEnvironment = GameLauncher.FormatEnvironment(options.Environment);
            string env = options.Environment.Count == 0 ? "-" : string.Join(" ", options.Environment.Keys);
            string note = string.Format(main.T("SteamLaunchOptionsImported"), string.IsNullOrEmpty(options.Arguments) ? "-" : options.Arguments, env);
            if (!string.IsNullOrEmpty(options.Wrapper)) note += " | " + string.Format(main.T("SteamLaunchOptionsWrapperIgnored"), options.Wrapper);
            main.SetStatus(note);
        }

        private async Task SaveAsync()
        {
            string game = (gameRoot ?? "").Trim();
            string workshop = (workshopRoot ?? "").Trim();
            string managed = (managedRoot ?? "").Trim();
            switch (LauncherSettingsStore.Validate(game, workshop, managed))
            {
                case SettingsProblem.GameFolderInvalid: await main.Dialogs.ShowMessageAsync(main.T("SelectGameFolderHint"), "DW2 Mod Launcher"); return;
                case SettingsProblem.WorkshopFolderMissing: await main.Dialogs.ShowMessageAsync(main.T("WorkshopFolderMissing"), "DW2 Mod Launcher"); return;
                case SettingsProblem.ManagedFolderMissing: await main.Dialogs.ShowMessageAsync(main.T("TheDW2ModFolderDoesNotExist"), "DW2 Mod Launcher"); return;
            }
            LauncherSettings s = main.LauncherSettings;
            s.GameRoot = game;
            s.WorkshopRoot = workshop;
            s.ManagedModsRoot = managed;
            s.GlobalLaunchArguments = (launchArguments ?? "").Trim();
            s.LaunchEnvironment = GameLauncher.ParseEnvironment(launchEnvironment);
            main.SaveSettings();
            main.Refresh();
        }

        public void RefreshProfiles()
        {
            ProfileNames.Clear();
            foreach (string name in main.Profiles.ListNames()) ProfileNames.Add(name);
            if (string.IsNullOrWhiteSpace(profileName) && !string.IsNullOrWhiteSpace(main.LauncherSettings.ActiveProfile))
                ProfileName = main.LauncherSettings.ActiveProfile;
        }

        private async Task SaveProfile()
        {
            string name = (profileName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                await main.Dialogs.ShowMessageAsync(main.T("EnterAProfileName"), "DW2 Mod Launcher");
                return;
            }
            LauncherSettings s = main.LauncherSettings;
            ModProfile profile = ProfileStore.Capture(name, main.ModOrder.Order, (launchArguments ?? "").Trim(), main.AllMods);
            try
            {
                main.Profiles.Save(profile);
                s.ActiveProfile = name;
                s.GlobalLaunchArguments = profile.ManualLaunchArguments ?? "";
                main.SaveSettings();
                RefreshProfiles();
                main.SetStatus(main.T("ModProfileSaved") + name);
            }
            catch (Exception ex) { await main.Dialogs.ShowMessageAsync(ex.Message, "DW2 Mod Launcher"); }
        }

        private async Task ApplyProfileAsync()
        {
            string name = (profileName ?? "").Trim();
            if (!main.Profiles.Exists(name)) { await main.Dialogs.ShowMessageAsync(main.T("ProfileNotFound"), "DW2 Mod Launcher"); return; }
            if (GameProcess.IsRunning()) { await main.Dialogs.ShowMessageAsync(main.T("CloseGameBeforeProfileSwitch"), "DW2 Mod Launcher"); return; }
            try
            {
                ModProfile profile = main.Profiles.Load(name);
                if (profile == null) return;
                var changes = ProfileStore.CompareVersions(profile, main.AllMods).Select(c => c.Installed == null
                    ? main.T("NotInstalled") + c.Token
                    : (c.Installed.DisplayName ?? c.Installed.Id) + ": " + c.SavedVersion + " → " + (c.Installed.Version ?? "?")).ToList();
                if (changes.Count > 0 && !await main.Dialogs.ConfirmAsync(
                    main.T("ProfileVersionMismatch") + string.Join("\n", changes.Take(20)) + main.T("ApplyTheProfile"),
                    main.T("VersionDifferences"), main.T("Yes"), main.T("No"))) return;

                LauncherSettings s = main.LauncherSettings;
                ModOrderStore.Write(main.ModsJsonPath(), profile.Order ?? new System.Collections.Generic.List<string>());
                s.ActiveProfile = profile.Name ?? name;
                s.GlobalLaunchArguments = profile.ManualLaunchArguments ?? "";
                main.SaveSettings();
                main.Refresh();
                main.SetStatus(main.T("SwitchedModProfile") + s.ActiveProfile);
            }
            catch (Exception ex)
            {
                Logger.LogException("Apply profile", ex);
                await main.Dialogs.ShowMessageAsync(ex.Message, "DW2 Mod Launcher");
            }
        }

        private async Task DeleteProfile()
        {
            string name = (profileName ?? "").Trim();
            try
            {
                main.Profiles.Delete(name);
                if (main.LauncherSettings.ActiveProfile == name) main.LauncherSettings.ActiveProfile = "";
                main.SaveSettings();
                ProfileName = "";
                RefreshProfiles();
            }
            catch (Exception ex) { await main.Dialogs.ShowMessageAsync(ex.Message, "DW2 Mod Launcher"); }
        }

        private string SnapshotsRoot() { return Path.Combine(main.AppRoot, "Snapshots"); }

        private async Task CreateSnapshotAsync()
        {
            try
            {
                string root = SnapshotStore.Create(SnapshotsRoot(), main.ModsJsonPath(), main.SettingsStore.Path,
                    main.AllMods.Where(main.IsSelected).ToList());
                await main.Dialogs.ShowMessageAsync(main.T("SnapshotSaved") + root, "DW2 Mod Launcher");
            }
            catch (Exception ex)
            {
                Logger.LogException("Create snapshot", ex);
                await main.Dialogs.ShowMessageAsync(ex.Message, "DW2 Mod Launcher");
            }
        }

        private async Task RestoreSnapshotAsync()
        {
            string root = SnapshotStore.FindLatest(SnapshotsRoot());
            if (string.IsNullOrWhiteSpace(root)) { await main.Dialogs.ShowMessageAsync(main.T("NoSnapshotIsAvailable"), "DW2 Mod Launcher"); return; }
            if (GameProcess.IsRunning()) { await main.Dialogs.ShowMessageAsync(main.T("CloseDW2BeforeRestoring"), "DW2 Mod Launcher"); return; }
            if (!await main.Dialogs.ConfirmAsync(main.T("RestoreTheLatestSnapshot") + root, main.T("RestoreSnapshot"), main.T("Yes"), main.T("No"))) return;
            try
            {
                SnapshotStore.Restore(root, main.ModsJsonPath(), main.SettingsStore.Path);
                main.ReloadSettings();
                await main.Dialogs.ShowMessageAsync(main.T("LatestSnapshotRestored"), "DW2 Mod Launcher");
            }
            catch (Exception ex)
            {
                Logger.LogException("Restore snapshot", ex);
                await main.Dialogs.ShowMessageAsync(ex.Message, "DW2 Mod Launcher");
            }
        }
    }
}
