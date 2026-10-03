using System;
using System.Collections.Generic;
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
            SaveProfileCommand = new RelayCommand(SaveProfile, () => HasProfile && profileIsDirty);
            RevertProfileCommand = new RelayCommand(RevertProfile, () => HasProfile && profileIsDirty);
            SaveProfileAsCommand = new RelayCommand(SaveProfileAs);
            RenameProfileCommand = new RelayCommand(RenameProfile, () => HasProfile);
            NewProfileCommand = new RelayCommand(NewProfile);
            DeleteProfileCommand = new RelayCommand(DeleteProfile, () => HasProfile);
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
        public RelayCommand RevertProfileCommand { get; }
        public RelayCommand SaveProfileAsCommand { get; }
        public RelayCommand RenameProfileCommand { get; }
        public RelayCommand NewProfileCommand { get; }
        public RelayCommand DeleteProfileCommand { get; }

        public ObservableCollection<string> ProfileNames { get; } = new ObservableCollection<string>();

        public string GameRoot { get { return gameRoot; } set { if (Set(ref gameRoot, value)) Raise(nameof(CommandPreview)); } }
        public string WorkshopRoot { get { return workshopRoot; } set { Set(ref workshopRoot, value); } }
        public string ManagedRoot { get { return managedRoot; } set { Set(ref managedRoot, value); } }
        public string LaunchArguments { get { return launchArguments; } set { if (Set(ref launchArguments, value)) Raise(nameof(CommandPreview)); } }
        /// <summary>True when the launcher starts the game itself (Windows); on Linux Steam owns the environment.</summary>
        public bool EnvironmentApplies { get { return OperatingSystem.IsWindows(); } }
        public bool EnvironmentIgnored { get { return !OperatingSystem.IsWindows(); } }
        public string LaunchEnvironment { get { return launchEnvironment; } set { Set(ref launchEnvironment, value); } }
        private bool refreshingProfiles;
        /// <summary>
        /// The selected (active) profile; selecting one in the UI switches to it. With no named profile active this is the
        /// "(default profile)" entry, and selecting that detaches the live list from its named profile.
        /// </summary>
        public string ProfileName
        {
            get { return HasProfile ? profileName : DefaultProfileLabel; }
            set
            {
                if (refreshingProfiles || string.IsNullOrWhiteSpace(value)) return;
                if (IsDefaultProfileLabel(value))
                {
                    if (HasProfile) _ = DetachProfileAsync();
                    return;
                }
                if (value == profileName) return;
                _ = SwitchProfileAsync(value);
            }
        }

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

        /// <summary>Lists DW2's own profiles (mods.&lt;name&gt;.json) and selects the active one from currentProfile.txt.</summary>
        public void RefreshProfiles()
        {
            GameProfileStore store = main.GameProfiles;
            refreshingProfiles = true;
            try
            {
                // Edit the list in place: clearing it makes the ComboBox drop its selection and show a blank name.
                // "(default profile)" always leads the list; it stands for the blank current profile.
                List<string> names = store.ListNames();
                names.Insert(0, DefaultProfileLabel);
                for (int i = ProfileNames.Count - 1; i >= 0; i--)
                    if (!names.Contains(ProfileNames[i])) ProfileNames.RemoveAt(i);
                for (int i = 0; i < names.Count; i++)
                {
                    if (i < ProfileNames.Count && ProfileNames[i] == names[i]) continue;
                    ProfileNames.Remove(names[i]);
                    ProfileNames.Insert(i, names[i]);
                }
                string current = store.ReadCurrent();
                profileName = string.IsNullOrWhiteSpace(current) || IsDefaultProfileLabel(current) ? ""
                    : ProfileNames.FirstOrDefault(n => string.Equals(n, current, StringComparison.OrdinalIgnoreCase)) ?? "";
                Raise(nameof(ProfileName));
                UpdateProfileCommands();
            }
            finally { refreshingProfiles = false; }
        }

        private bool profileIsDirty;
        /// <summary>Drives the "*" next to the profile name; refreshed by <see cref="RaiseDirty"/> whenever the list or profile changes.</summary>
        public bool ProfileIsDirty { get { return profileIsDirty; } }

        public void RaiseDirty()
        {
            if (Set(ref profileIsDirty, ProfileDirty, nameof(ProfileIsDirty)))
            {
                SaveProfileCommand.RaiseCanExecuteChanged();
                RevertProfileCommand.RaiseCanExecuteChanged();
            }
        }

        /// <summary>Save, Rename and Delete need a selected profile.</summary>
        private bool HasProfile { get { return !string.IsNullOrWhiteSpace(profileName); } }

        private void UpdateProfileCommands()
        {
            SaveProfileCommand.RaiseCanExecuteChanged();
            RenameProfileCommand.RaiseCanExecuteChanged();
            DeleteProfileCommand.RaiseCanExecuteChanged();
            RaiseDirty();
        }

        private async Task<bool> GameClosedAsync()
        {
            if (!GameProcess.IsRunning()) return true;
            await main.Dialogs.ShowMessageAsync(main.T("CloseGameBeforeProfileSwitch"), "DW2 Mod Launcher");
            return false;
        }

        /// <summary>Selecting a profile makes it the active one: its order becomes mods.json and it is shown for editing.</summary>
        private async Task SwitchProfileAsync(string target)
        {
            GameProfileStore store = main.GameProfiles;
            try
            {
                ModOrderState profile = store.Read(target);
                if (!profile.FileFound) { await main.Dialogs.ShowMessageAsync(main.T("ProfileNotFound"), "DW2 Mod Launcher"); RefreshProfiles(); return; }
                if (!await GameClosedAsync()) { RefreshProfiles(); return; }
                string current = store.ReadCurrent();
                profileName = target; // the ComboBox already shows it; keep the view-model in step (a failure below refreshes it back)
                UpdateProfileCommands();
                if (store.Exists(current) && !store.Read(current).Order.SequenceEqual(main.ModOrder.Order, StringComparer.OrdinalIgnoreCase)
                    && !await main.Dialogs.ConfirmAsync(main.T("DiscardUnsavedProfileChanges") + current, main.T("ModProfiles"), main.T("Yes"), main.T("No")))
                {
                    RefreshProfiles();
                    return;
                }
                ModOrderStore.Write(main.ModsJsonPath(), profile.Order);
                store.WriteCurrent(target);
                main.Refresh();
                main.SetStatus(main.T("SwitchedModProfile") + target);
            }
            catch (Exception ex)
            {
                Logger.LogException("Switch profile", ex);
                await main.Dialogs.ShowMessageAsync(ex.Message, "DW2 Mod Launcher");
                RefreshProfiles();
            }
        }

        /// <summary>Writes the live mod order into the profile (creating it if new) and makes it the active one.</summary>
        private async Task<bool> WriteProfileAsync(string name)
        {
            GameProfileStore store = main.GameProfiles;
            name = store.CanonicalName(name);
            try
            {
                store.Write(name, main.ModOrder.Order);
                store.WriteCurrent(name);
                RefreshProfiles();
                main.SetStatus(main.T("ModProfileSaved") + name);
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogException("Save profile", ex);
                await main.Dialogs.ShowMessageAsync(ex.Message, "DW2 Mod Launcher");
                return false;
            }
        }

        /// <summary>True when a profile is selected and the live mod list (mods.json) differs from that profile's file.</summary>
        public bool ProfileDirty
        {
            get
            {
                GameProfileStore store = main.GameProfiles;
                return !string.IsNullOrWhiteSpace(profileName) && store.Exists(profileName)
                    && !store.Read(profileName).Order.SequenceEqual(main.ModOrder.Order, StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Before launching: with unsaved edits, Save changes / Don't save / cancel the launch (returns false). Don't save keeps
        /// the list but disconnects it from the profile. Otherwise the profile on screen is made the active one.
        /// </summary>
        public async Task<bool> ConfirmSaveForLaunchAsync()
        {
            if (string.IsNullOrWhiteSpace(profileName)) return true;
            if (ProfileDirty) return await AskSaveOrDisconnectAsync(string.Format(main.T("UnsavedProfileLaunch"), profileName));
            try { main.GameProfiles.WriteCurrent(profileName); }
            catch (Exception ex) { Logger.LogException("Set active profile before launch", ex); }
            return true;
        }

        /// <summary>On exit with unsaved edits: Save changes / Don't save / cancel the exit (returns false).</summary>
        public async Task<bool> ConfirmSaveForExitAsync()
        {
            if (!ProfileDirty) return true;
            return await AskSaveOrDisconnectAsync(string.Format(main.T("UnsavedProfileExit"), profileName));
        }

        /// <summary>
        /// Save changes writes the profile. Don't save keeps the live list but makes it the default profile (no active named profile), so it is
        /// no longer tied to the profile it came from. Null from the dialog cancels (returns false).
        /// </summary>
        private async Task<bool> AskSaveOrDisconnectAsync(string message)
        {
            int choice = await main.Dialogs.ChooseAsync(message, main.T("ModProfiles"), new[] { main.T("SaveChanges"), main.T("DontSave"), main.T("Cancel") });
            if (choice == 0) return await WriteProfileAsync(profileName);
            if (choice != 1) return false;
            try
            {
                Disconnect();
            }
            catch (Exception ex) { Logger.LogException("Disconnect profile", ex); }
            return true;
        }

        /// <summary>Discards unsaved edits by reloading the selected profile into mods.json (after a confirm).</summary>
        private async Task RevertProfile()
        {
            string name = profileName;
            GameProfileStore store = main.GameProfiles;
            ModOrderState profile = store.Read(name);
            if (!profile.FileFound) { await main.Dialogs.ShowMessageAsync(main.T("ProfileNotFound"), "DW2 Mod Launcher"); return; }
            if (!await GameClosedAsync()) return;
            try
            {
                ModOrderStore.Write(main.ModsJsonPath(), profile.Order);
                main.Refresh();
                main.SetStatus(main.T("SwitchedModProfile") + name);
            }
            catch (Exception ex)
            {
                Logger.LogException("Revert profile", ex);
                await main.Dialogs.ShowMessageAsync(ex.Message, "DW2 Mod Launcher");
            }
        }

        private async Task SaveProfile()
        {
            if (string.IsNullOrWhiteSpace(profileName)) { await SaveProfileAs(); return; }
            if (!await GameClosedAsync()) return;
            await WriteProfileAsync(profileName);
        }

        /// <summary>DW2's own name for "no named profile": the live list (mods.json) stays, but is tied to nothing.</summary>
        private string DefaultProfileLabel { get { return main.T("UnnamedActive"); } }

        private bool IsDefaultProfileLabel(string text) { return string.Equals((text ?? "").Trim(), DefaultProfileLabel, StringComparison.OrdinalIgnoreCase); }

        /// <summary>Picked from the drop-down: the live list carries on as the default profile, with unsaved edits kept.</summary>
        private async Task DetachProfileAsync()
        {
            if (!await GameClosedAsync()) { RefreshProfiles(); return; }
            try { Disconnect(); main.SetStatus(main.T("ProfileUnlinked")); }
            catch (Exception ex) { Logger.LogException("Disconnect profile", ex); await main.Dialogs.ShowMessageAsync(ex.Message, "DW2 Mod Launcher"); RefreshProfiles(); }
        }

        /// <summary>Makes the live list the default profile (no active named profile). The list itself and every profile file are left alone.</summary>
        private void Disconnect()
        {
            main.GameProfiles.WriteCurrent("");
            RefreshProfiles();
        }

        /// <summary>
        /// Asks for a profile name; null if cancelled. Blank to type in. With <paramref name="allowOverwrite"/> the box is also a
        /// drop-down of the existing profiles (minus <paramref name="exclude"/>) and picking or typing one warns it will be
        /// overwritten; without it, an existing name is flagged as taken. <paramref name="unnamedHintKey"/> adds the
        /// "(default profile)" entry with that explanatory note; without it that label is not a legal name.
        /// </summary>
        private async Task<string> AskNewProfileNameAsync(bool allowOverwrite = false, string exclude = null, string own = null, string unnamedHintKey = null)
        {
            GameProfileStore store = main.GameProfiles;
            List<string> choices = allowOverwrite
                ? store.ListNames().Where(n => !string.Equals(n, exclude, StringComparison.OrdinalIgnoreCase)).ToList()
                : null;
            if (choices != null && unnamedHintKey != null) choices.Insert(0, DefaultProfileLabel);
            return await main.Dialogs.PromptTextAsync(main.T("ModProfiles"), main.T("NewProfileNamePrompt"), "", main.T("OK"), main.T("Cancel"),
                typed =>
                {
                    typed = (typed ?? "").Trim();
                    if (IsDefaultProfileLabel(typed)) return unnamedHintKey == null ? "" : main.T(unnamedHintKey);
                    return store.Exists(typed) && !string.Equals(typed, own, StringComparison.OrdinalIgnoreCase)
                        ? main.T(allowOverwrite ? "ProfileWillOverwrite" : "ProfileNameExists") : "";
                }, choices,
                typed => IsDefaultProfileLabel(typed) ? unnamedHintKey == null : !allowOverwrite && store.Exists(typed));
        }

        private async Task SaveProfileAs()
        {
            if (!await GameClosedAsync()) return;
            string name = (await AskNewProfileNameAsync(true, null, profileName, "UnnamedSaveAsHint"))?.Trim();
            if (string.IsNullOrWhiteSpace(name)) return;
            if (IsDefaultProfileLabel(name))
            {
                try { Disconnect(); main.SetStatus(main.T("ProfileUnlinked")); }
                catch (Exception ex) { Logger.LogException("Disconnect profile", ex); await main.Dialogs.ShowMessageAsync(ex.Message, "DW2 Mod Launcher"); }
                return;
            }
            // Choosing the active profile's own name is just a Save: nothing to warn about.
            bool own = string.Equals(name, profileName, StringComparison.OrdinalIgnoreCase);
            if (!own && main.GameProfiles.Exists(name) && !await main.Dialogs.ConfirmAsync(main.T("OverwriteProfileConfirm") + name, main.T("ModProfiles"), main.T("Yes"), main.T("No"))) return;
            await WriteProfileAsync(name);
        }

        private async Task RenameProfile()
        {
            string old = profileName;
            GameProfileStore store = main.GameProfiles;
            if (string.IsNullOrWhiteSpace(old) || !store.Exists(old)) { await main.Dialogs.ShowMessageAsync(main.T("ProfileNotFound"), "DW2 Mod Launcher"); return; }
            if (!await GameClosedAsync()) return;
            string name = (await AskNewProfileNameAsync(true, old, old, "UnnamedRenameHint"))?.Trim();
            if (string.IsNullOrWhiteSpace(name)) return;
            if (IsDefaultProfileLabel(name))
            {
                // The profile gives up its name: its file goes, and the live list carries on as the default profile.
                if (!await main.Dialogs.ConfirmAsync(main.T("RenameToUnnamedConfirm") + old, main.T("ModProfiles"), main.T("Yes"), main.T("No"))) return;
                try
                {
                    store.Delete(old);
                    Disconnect();
                    main.SetStatus(main.T("ProfileUnlinked"));
                }
                catch (Exception ex)
                {
                    Logger.LogException("Rename profile to unnamed", ex);
                    await main.Dialogs.ShowMessageAsync(ex.Message, "DW2 Mod Launcher");
                    RefreshProfiles();
                }
                return;
            }
            // Renaming to its own name changes nothing, so it counts as a Save.
            if (name == old) { await WriteProfileAsync(old); return; }
            bool overwrites = store.Exists(name) && !string.Equals(name, old, StringComparison.OrdinalIgnoreCase);
            if (overwrites && !await main.Dialogs.ConfirmAsync(main.T("OverwriteProfileConfirm") + name, main.T("ModProfiles"), main.T("Yes"), main.T("No"))) return;
            try
            {
                string target = store.CanonicalName(name);
                bool active = string.Equals(store.ReadCurrent(), old, StringComparison.OrdinalIgnoreCase);
                store.Rename(old, name);
                if (active) store.WriteCurrent(overwrites ? target : name);
                RefreshProfiles();
            }
            catch (Exception ex)
            {
                Logger.LogException("Rename profile", ex);
                await main.Dialogs.ShowMessageAsync(ex.Message, "DW2 Mod Launcher");
                RefreshProfiles();
            }
        }

        /// <summary>Creates an empty profile and switches to it (so every mod starts disabled).</summary>
        private async Task NewProfile()
        {
            GameProfileStore store = main.GameProfiles;
            if (!await GameClosedAsync()) return;
            string name = (await AskNewProfileNameAsync())?.Trim();
            if (string.IsNullOrWhiteSpace(name)) return;
            if (store.Exists(name)) { await main.Dialogs.ShowMessageAsync(main.T("ProfileNameExists"), "DW2 Mod Launcher"); return; }
            if (ProfileDirty && !await main.Dialogs.ConfirmAsync(main.T("DiscardUnsavedProfileChanges") + profileName, main.T("ModProfiles"), main.T("Yes"), main.T("No"))) return;
            try
            {
                List<string> empty = new List<string>();
                store.Write(name, empty);
                ModOrderStore.Write(main.ModsJsonPath(), empty);
                store.WriteCurrent(name);
                main.Refresh();
                main.SetStatus(main.T("SwitchedModProfile") + name);
            }
            catch (Exception ex)
            {
                Logger.LogException("New profile", ex);
                await main.Dialogs.ShowMessageAsync(ex.Message, "DW2 Mod Launcher");
                RefreshProfiles();
            }
        }

        private async Task DeleteProfile()
        {
            string name = profileName;
            GameProfileStore store = main.GameProfiles;
            if (string.IsNullOrWhiteSpace(name) || !store.Exists(name)) { await main.Dialogs.ShowMessageAsync(main.T("ProfileNotFound"), "DW2 Mod Launcher"); return; }
            if (!await GameClosedAsync()) return;
            if (!await main.Dialogs.ConfirmAsync(main.T("DeleteProfileConfirm") + name, main.T("ModProfiles"), main.T("Yes"), main.T("No"))) return;
            try
            {
                int index = store.ListNames().IndexOf(name);
                bool wasActive = string.Equals(store.ReadCurrent(), name, StringComparison.Ordinal);
                store.Delete(name);
                List<string> remaining = store.ListNames();
                if (wasActive) store.WriteCurrent("");
                // Land on the previous profile in the list (or the next one if it was first); the default profile only when none are left.
                if (wasActive && remaining.Count > 0)
                {
                    string next = remaining[Math.Max(0, Math.Min(index - 1, remaining.Count - 1))];
                    ModOrderStore.Write(main.ModsJsonPath(), store.Read(next).Order);
                    store.WriteCurrent(next);
                    main.Refresh();
                    main.SetStatus(main.T("SwitchedModProfile") + next);
                }
                else RefreshProfiles();
            }
            catch (Exception ex) { await main.Dialogs.ShowMessageAsync(ex.Message, "DW2 Mod Launcher"); }
        }
    }
}
