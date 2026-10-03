using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using DW2ModLauncher.Avalonia.Services;
using DW2ModLauncher.Core.Diagnostics;
using System.Text.Json.Nodes;
using DW2ModLauncher.Core.Models;
using DW2ModLauncher.Core.Services.Publishing;
using DW2ModLauncher.Core.Services;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace DW2ModLauncher.Avalonia.ViewModels
{
    public class LanguageOption
    {
        public string Code { get; set; }
        public string DisplayName { get; set; }
        public string ShortCode { get { return Code.ToUpperInvariant(); } }
        public override string ToString() { return ShortCode + "  " + DisplayName; }
    }

    /// <summary>Application state and commands behind the main window. All logic that isn't about presentation lives in Core.</summary>
    public class MainViewModel : ViewModelBase
    {
        private readonly string appRoot;
        private readonly LauncherSettingsStore settingsStore;
        private readonly ProfileStore profileStore;

        private LauncherSettings settings;
        private ModOrderState modOrder = new ModOrderState();
        private List<ModInfo> managedMods = new List<ModInfo>();
        private List<ModInfo> workshopMods = new List<ModInfo>();
        private Dictionary<string, List<ModInfo>> collisions = new Dictionary<string, List<ModInfo>>(StringComparer.OrdinalIgnoreCase);
        private string statusText = "";
        private LanguageOption selectedLanguage;
        private ModRowViewModel selectedRow;
        private int sortColumn = -1;
        private bool sortAscending = true;
        private bool updateCheckRunning;
        private bool populating;
        private string detailTitle = "";
        private string detailText = "";
        private string problemsText = "";
        private bool problemsIsConflict;
        private Bitmap preview;
        private bool publishRunning;

        public MainViewModel(IDialogService dialogs, string appRoot)
        {
            Dialogs = dialogs;
            this.appRoot = appRoot;
            settingsStore = new LauncherSettingsStore(Path.Combine(appRoot, "launcher_settings.json"));
            profileStore = new ProfileStore(Path.Combine(appRoot, "Profiles"));
            settings = settingsStore.Load();
            NormalizeSettings();
            if (string.IsNullOrWhiteSpace(settings.ManagedModsRoot))
                settings.ManagedModsRoot = string.IsNullOrWhiteSpace(settings.GameRoot) ? "" : Path.Combine(settings.GameRoot, "mods");

            L.SetLanguage(settings.Language);
            Dialogs.OkText = T("OK");
            foreach (string code in Localization.AvailableLanguageCodes())
                Languages.Add(new LanguageOption { Code = code, DisplayName = Localization.DisplayNameFor(code) });
            selectedLanguage = Languages.FirstOrDefault(l => l.Code == settings.Language) ?? Languages[0];

            Settings = new SettingsViewModel(this);
            OpenSettingsCommand = new RelayCommand(() => Dialogs.ShowSettingsAsync(Settings));
            RefreshCommand = new RelayCommand(Refresh);
            ClearCommand = new RelayCommand(ClearAsync);
            EnableAllCommand = new RelayCommand(EnableAllAsync);
            PlayCommand = new RelayCommand(PlayOrStopAsync, () => gameState != GameState.Launching);
            gameWatchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            gameWatchTimer.Tick += delegate { var _ = WatchGameAsync(); };
            gameWatchTimer.Start();
            var first = WatchGameAsync();
            SortCommand = RelayCommand.WithParameter(column => SortBy(int.Parse((string)column, System.Globalization.CultureInfo.InvariantCulture)));
            OpenSelectedFolderCommand = new RelayCommand(OpenModFolder, () => selectedRow != null);
            OpenSteamPageCommand = new RelayCommand(OpenSteamPage, () => SteamPageId(selectedRow) != null);
            OpenDocsCommand = new RelayCommand(OpenDocsAsync, () => selectedRow != null && selectedRow.Mod.IncludedDocuments != null && selectedRow.Mod.IncludedDocuments.Count > 0);
            ModSettingsCommand = new RelayCommand(OpenModSettingsAsync, () => HasModSettings(selectedRow));
            PublishCommand = new RelayCommand(PublishAsync, () => selectedRow != null && !selectedRow.Mod.IsWorkshop && !publishRunning);
            CheckUpdatesCommand = new RelayCommand(() => BeginWorkshopUpdateCheck(true), () => !updateCheckRunning && workshopMods.Count > 0);
            CreateModCommand = new RelayCommand(CreateModAsync);
            DeleteModCommand = new RelayCommand(DeleteModAsync, () => selectedRow != null && LocalModManager.CanDelete(selectedRow.Mod, settings.ManagedModsRoot));

            PathDetector.Detect(settings, false);
            SaveSettings();
            Refresh();
        }

        public IDialogService Dialogs { get; }
        public LocalizedStrings L { get; } = new LocalizedStrings();
        public SettingsViewModel Settings { get; }
        public ObservableCollection<LanguageOption> Languages { get; } = new ObservableCollection<LanguageOption>();
        public ObservableCollection<ModRowViewModel> Mods { get; } = new ObservableCollection<ModRowViewModel>();

        public RelayCommand OpenSettingsCommand { get; }
        public RelayCommand RefreshCommand { get; }
        public RelayCommand ClearCommand { get; }
        public RelayCommand EnableAllCommand { get; }
        public RelayCommand PlayCommand { get; }

        // ---- game state: Play -> Launching (spinner, locked) -> Running (Stop) -> Idle

        private enum GameState { Idle, Launching, Running }

        // DW2 can take a long time to show up, so give a launch plenty of time before concluding it never started.
        private static readonly TimeSpan LaunchTimeout = TimeSpan.FromMinutes(3);
        // The process exists long before the game window does, so keep the spinner up for a while before offering Stop.
        private static readonly TimeSpan LaunchMinimum = TimeSpan.FromSeconds(10);
        private readonly DispatcherTimer gameWatchTimer;
        private GameState gameState = GameState.Idle;
        private DateTime launchStartedUtc;
        private bool watching;

        public bool IsLaunching { get { return gameState == GameState.Launching; } }
        public bool IsGameRunning { get { return gameState == GameState.Running; } }
        public bool IsGameIdle { get { return gameState == GameState.Idle; } }
        public string PlayTooltip
        {
            get { return T(gameState == GameState.Launching ? "LaunchingButton" : gameState == GameState.Running ? "StopTooltip" : "PlayTooltip"); }
        }
        public string PlayLabel
        {
            get { return T(gameState == GameState.Launching ? "LaunchingButton" : gameState == GameState.Running ? "StopButton" : "PlayButton"); }
        }

        private void SetGameState(GameState state)
        {
            if (gameState == state) return;
            gameState = state;
            Raise(nameof(IsLaunching));
            Raise(nameof(IsGameRunning));
            Raise(nameof(IsGameIdle));
            Raise(nameof(PlayLabel));
            Raise(nameof(PlayTooltip));
            PlayCommand.RaiseCanExecuteChanged();
        }

        /// <summary>Polls for the game process (off the UI thread) and moves the Play button between its states.</summary>
        private async Task WatchGameAsync()
        {
            if (watching) return;
            watching = true;
            try
            {
                bool running = await Task.Run(() => GameProcess.IsRunning());
                if (running && gameState == GameState.Launching && DateTime.UtcNow - launchStartedUtc < LaunchMinimum) return;
                if (running) SetGameState(GameState.Running);
                else if (gameState == GameState.Running) SetGameState(GameState.Idle);
                else if (gameState == GameState.Launching && DateTime.UtcNow - launchStartedUtc > LaunchTimeout)
                {
                    SetGameState(GameState.Idle);
                    SetStatus(T("GameLaunchTimedOut"));
                }
            }
            finally { watching = false; }
        }

        private async Task PlayOrStopAsync()
        {
            if (gameState == GameState.Running) await StopGameAsync();
            else await PlayAsync();
        }

        private async Task StopGameAsync()
        {
            if (!await Dialogs.ConfirmAsync(T("ConfirmStopGame"), "DW2 Mod Launcher", T("Yes"), T("No"))) return;
            await Task.Run(() => GameProcess.Kill());
            SetGameState(GameState.Idle);
            SetStatus(T("GameStopped"));
        }
        public RelayCommand SortCommand { get; }
        public RelayCommand OpenSelectedFolderCommand { get; }
        public RelayCommand OpenSteamPageCommand { get; }
        public RelayCommand OpenDocsCommand { get; }
        public RelayCommand CheckUpdatesCommand { get; }
        public RelayCommand ModSettingsCommand { get; }
        public RelayCommand PublishCommand { get; }
        public RelayCommand CreateModCommand { get; }
        public RelayCommand DeleteModCommand { get; }

        public ModRowViewModel SelectedRow
        {
            get { return selectedRow; }
            set
            {
                if (!Set(ref selectedRow, value)) return;
                ShowDetails();
                OpenSelectedFolderCommand.RaiseCanExecuteChanged();
                OpenSteamPageCommand.RaiseCanExecuteChanged();
                OpenDocsCommand.RaiseCanExecuteChanged();
                ModSettingsCommand.RaiseCanExecuteChanged();
                PublishCommand.RaiseCanExecuteChanged();
                DeleteModCommand.RaiseCanExecuteChanged();
            }
        }

        public string DetailTitle { get { return detailTitle; } private set { Set(ref detailTitle, value); } }
        public string DetailText { get { return detailText; } private set { Set(ref detailText, value); } }
        public string ProblemsText { get { return problemsText; } private set { Set(ref problemsText, value); Raise(nameof(HasProblems)); } }
        public bool HasProblems { get { return !string.IsNullOrEmpty(problemsText); } }
        public bool ProblemsIsConflict { get { return problemsIsConflict; } private set { Set(ref problemsIsConflict, value); } }
        public Bitmap Preview { get { return preview; } private set { Set(ref preview, value); } }
        public bool HasSelection { get { return selectedRow != null; } }

        public LauncherSettings LauncherSettings { get { return settings; } }
        public LauncherSettingsStore SettingsStore { get { return settingsStore; } }
        public ProfileStore Profiles { get { return profileStore; } }
        /// <summary>DW2's own named profiles, next to mods.json.</summary>
        public GameProfileStore GameProfiles { get { return new GameProfileStore(Path.GetDirectoryName(ModsJsonPath() ?? "")); } }
        public ModOrderState ModOrder { get { return modOrder; } }
        public string AppRoot { get { return appRoot; } }

        public IEnumerable<ModInfo> AllMods { get { return managedMods.Concat(workshopMods); } }

        public string StatusText { get { return statusText; } set { Set(ref statusText, value); } }

        public LanguageOption SelectedLanguage
        {
            get { return selectedLanguage; }
            set
            {
                if (value == null || !Set(ref selectedLanguage, value)) return;
                settings.Language = value.Code;
                L.SetLanguage(value.Code);
                Dialogs.OkText = T("OK");
                Raise(nameof(PlayLabel));
                Raise(nameof(PlayTooltip));
                SaveSettings();
                ApplySourceNames();
                RebuildRows();
                UpdateStatus();
                Settings.UpdateCommandPreview();
            }
        }

        public string T(string key) { return L[key]; }
        public string T(string key, params object[] args) { return L.Format(key, args); }

        public void SetStatus(string text) { StatusText = text; }

        public void NormalizeSettings()
        {
            LauncherSettingsStore.Normalize(settings);
            if (!Localization.AvailableLanguageCodes().Contains(settings.Language)) settings.Language = "en";
        }

        public void SaveSettings()
        {
            try { settingsStore.Save(settings); }
            catch (Exception ex) { SetStatus("Settings save error: " + ex.Message); }
        }

        public string ModsJsonPath() { return ModOrderStore.PathFor(settings.ManagedModsRoot, settings.GameRoot); }

        public bool IsSelected(ModInfo mod) { return modOrder.IsSelected(mod, settings); }

        public void Refresh()
        {
            NormalizeSettings();
            modOrder = ModOrderStore.Read(ModsJsonPath());
            Dictionary<string, ModInfo> workshopState = ModLibrary.IndexWorkshopById(workshopMods);
            try { managedMods = ModScanner.ScanMods(settings.ManagedModsRoot, false, key => T(key)) ?? new List<ModInfo>(); }
            catch (Exception ex) { Logger.LogException("Scan managed mods", ex); managedMods = new List<ModInfo>(); }
            try { workshopMods = ModScanner.ScanMods(settings.WorkshopRoot, true, key => T(key)) ?? new List<ModInfo>(); }
            catch (Exception ex) { Logger.LogException("Scan Workshop mods", ex); workshopMods = new List<ModInfo>(); }
            ModLibrary.RestoreWorkshopRuntimeState(workshopMods, workshopState);
            managedMods = ModLibrary.OrderForDisplay(managedMods, modOrder);
            workshopMods = ModLibrary.OrderForDisplay(workshopMods, modOrder);

            Analyze();
            RebuildRows();
            Settings.LoadFromSettings();
            UpdateStatus();
        }

        private void Analyze()
        {
            collisions = ConflictAnalyzer.Analyze(AllMods, m => modOrder.IsEnabledForConflict(m, settings));
            ConflictAnalyzer.AnalyzeDuplicates(AllMods);
        }

        private void ApplySourceNames()
        {
            foreach (ModInfo mod in managedMods) mod.SourceName = T("GameModFolder");
            foreach (ModInfo mod in workshopMods) mod.SourceName = "Steam Workshop";
        }

        private void RebuildRows()
        {
            ModInfo keepSelected = selectedRow == null ? null : selectedRow.Mod;
            populating = true;
            Mods.Clear();
            foreach (ModInfo mod in ModLibrary.OrderForDisplay(AllMods, modOrder))
                Mods.Add(new ModRowViewModel(mod, RelayCommand.WithParameter(row => { var _ = ToggleAsync((ModRowViewModel)row); })));
            populating = false;
            RefreshRowStates();
            ApplySort();
            SelectedRow = keepSelected == null ? null : Mods.FirstOrDefault(r => r.Mod == keepSelected);
        }

        /// <summary>Recomputes enabled / health / load-order for the existing rows without rebuilding the list (keeps scroll position).</summary>
        private void RefreshRowStates()
        {
            // mods.json can list mods that aren't installed (or aren't shown); the game skips those, so number only the ones we have.
            HashSet<string> shown = new HashSet<string>(Mods.Select(r => r.Mod.ActiveToken).Where(t => !string.IsNullOrEmpty(t)), StringComparer.OrdinalIgnoreCase);
            List<string> present = (modOrder.Order ?? new List<string>()).Where(shown.Contains).ToList();
            foreach (ModRowViewModel row in Mods)
            {
                bool enabled = IsSelected(row.Mod);
                row.Enabled = enabled;
                int severity = ModHealth.Severity(row.Mod, enabled);
                row.HealthSeverity = severity;
                row.Health = severity == 3 ? T("HealthConflict") : severity == 2 ? T("HealthCaution") : severity == 1 ? T("HealthOk") : T("ModDisabled");
                row.LoadOrderIndex = present.FindIndex(t => string.Equals(t, row.Mod.ActiveToken, StringComparison.OrdinalIgnoreCase));
                row.RefreshFromMod();
            }
            Settings?.RaiseDirty();
            ShowDetails();
        }

        // ---- sorting (column numbers match the header buttons: 0 name, 1 source, 2 state, 3 health, 4 load order)

        private void SortBy(int column)
        {
            sortAscending = sortColumn == column ? !sortAscending : true;
            sortColumn = column;
            ApplySort();
        }

        private void ApplySort()
        {
            if (sortColumn < 0 || Mods.Count == 0) return;
            Func<ModRowViewModel, object> key;
            switch (sortColumn)
            {
                case 1: key = r => r.Source; break;
                case 2: key = r => r.Enabled ? 1 : 0; break;
                case 3: key = r => r.HealthSeverity; break;
                case 4: key = r => r.LoadOrderIndex < 0 ? int.MaxValue : r.LoadOrderIndex; break;
                default: key = r => r.Name; break;
            }
            IComparer<object> comparer = Comparer<object>.Create((a, b) => a is string
                ? StringComparer.CurrentCultureIgnoreCase.Compare((string)a, (string)b)
                : ((int)a).CompareTo((int)b));
            IOrderedEnumerable<ModRowViewModel> ordered = sortAscending ? Mods.OrderBy(key, comparer) : Mods.OrderByDescending(key, comparer);
            List<ModRowViewModel> sorted = ordered.ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            if (!sortAscending) sorted = Mods.OrderByDescending(key, comparer).ThenByDescending(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            ModRowViewModel keep = selectedRow;
            populating = true;
            for (int i = 0; i < sorted.Count; i++)
            {
                int from = Mods.IndexOf(sorted[i]);
                if (from != i) Mods.Move(from, i);
            }
            populating = false;
            SelectedRow = keep;
        }

        // ---- enabling and load order

        private async Task ToggleAsync(ModRowViewModel row)
        {
            if (row == null || populating) return;
            bool enable = !IsSelected(row.Mod);
            SetEnabledResult result = ModLibrary.SetEnabled(row.Mod, enable, settings, modOrder);
            SaveSettings();
            switch (result.Outcome)
            {
                case SetEnabledOutcome.ModsJsonInvalid:
                    await Dialogs.ShowMessageAsync(T("ModsJsonInvalidWarning"), "DW2 Mod Launcher");
                    Refresh();
                    return;
                case SetEnabledOutcome.GameRunning:
                    await Dialogs.ShowMessageAsync(T("GameRunningWarning"), "DW2 Mod Launcher");
                    Refresh();
                    return;
                case SetEnabledOutcome.WriteFailed:
                    Logger.LogException("Write DW2 mods.json", result.Error);
                    await Dialogs.ShowMessageAsync("Failed to save mods.json.\n" + result.Error.Message, "DW2 Mod Launcher");
                    modOrder = ModOrderStore.Read(ModsJsonPath());
                    break;
                case SetEnabledOutcome.Saved:
                    SetStatus(T("DW2ModSettingsSaved"));
                    break;
            }
            Analyze();
            RefreshRowStates();
            UpdateStatus();
            Settings.UpdateCommandPreview();
        }

        /// <summary>Moves a row (drag and drop) and writes the new load order. Sorting is dropped: the list now shows the manual order.</summary>
        public void MoveRow(ModRowViewModel row, int toIndex)
        {
            int from = Mods.IndexOf(row);
            if (from < 0) return;
            sortColumn = -1;
            toIndex = Math.Max(0, Math.Min(toIndex, Mods.Count - 1));
            if (from != toIndex) Mods.Move(from, toIndex);
            SelectedRow = row;
            CommitListOrder();
        }

        private void CommitListOrder()
        {
            if (GameProcess.IsRunning()) return;
            List<string> ordered = Mods.Where(r => IsSelected(r.Mod)).Select(r => r.Mod.ActiveToken).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            try
            {
                List<string> written = ModOrderStore.Write(ModsJsonPath(), ModOrderStore.Reorder(modOrder.Order, ordered));
                if (written == null) return;
                modOrder.Order = written;
                modOrder.FileFound = true;
                SetStatus(T("LoadOrderSaved"));
                Settings.UpdateCommandPreview();
                RefreshRowStates();
            }
            catch (Exception ex)
            {
                Logger.LogException("Write load order", ex);
                var _ = Dialogs.ShowMessageAsync("Could not save load order.\n" + ex.Message, "DW2 Mod Launcher");
            }
        }

        // ---- details panel and toolbar

        private void ShowDetails()
        {
            Raise(nameof(HasSelection));
            ModInfo mod = selectedRow == null ? null : selectedRow.Mod;
            if (mod == null)
            {
                DetailTitle = "";
                DetailText = "";
                ProblemsText = "";
                Preview = null;
                return;
            }
            bool selected = IsSelected(mod);
            int severity = ModHealth.Severity(mod, selected);
            DetailTitle = (mod.DisplayName ?? "") + (string.IsNullOrWhiteSpace(mod.Version) ? "" : "  v" + mod.Version);
            DetailText = ModDetails.BuildText(mod, selected, key => T(key));
            List<string> problems = ModDetails.BuildProblems(mod, severity, key => T(key));
            ProblemsIsConflict = severity == 3;
            ProblemsText = problems.Count == 0 ? "" : (severity == 3 ? T("HealthConflict") : T("HealthCaution")) + "\n" + string.Join("\n", problems);
            Preview = ImageLoader.Load(mod.PreviewImage, 0);
        }

        /// <summary>The Workshop item id behind a row: the folder name for a Workshop copy, or the id written into mod.json by a publish.</summary>
        private static string SteamPageId(ModRowViewModel row)
        {
            if (row == null) return null;
            string id = row.Mod.IsWorkshop ? row.Mod.Id : row.Mod.WorkshopId;
            return long.TryParse(id, out long _) ? id : null;
        }

        private void OpenSteamPage()
        {
            string id = SteamPageId(selectedRow);
            if (id == null) return;
            try { PlatformShell.Create().OpenUrl("steam://url/CommunityFilePage/" + id); }
            catch
            {
                try { PlatformShell.Create().OpenUrl("https://steamcommunity.com/sharedfiles/filedetails/?id=" + id); }
                catch (Exception ex) { var _ = Dialogs.ShowMessageAsync(ex.Message, "DW2 Mod Launcher"); }
            }
        }

        private void OpenModFolder()
        {
            if (selectedRow == null) return;
            string path = selectedRow.Mod.Folder;
            try
            {
                if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) { var _ = Dialogs.ShowMessageAsync(T("FolderNotFound"), "DW2 Mod Launcher"); return; }
                PlatformShell.Create().OpenFolder(path);
            }
            catch (Exception ex) { var _ = Dialogs.ShowMessageAsync(ex.Message, "DW2 Mod Launcher"); }
        }

        private async Task OpenDocsAsync()
        {
            if (selectedRow == null) return;
            ModInfo mod = selectedRow.Mod;
            string chosen = mod.IncludedDocuments.Count == 1
                ? mod.IncludedDocuments[0]
                : await Dialogs.PickFromListAsync(T("OpenDocs"), mod.DisplayName ?? mod.Id, mod.IncludedDocuments);
            if (chosen == null) return;
            try
            {
                string full = ModDocuments.ResolveSafe(mod, chosen);
                if (full == null) { await Dialogs.ShowMessageAsync(T("DocumentMissingWarning"), "DW2 Mod Launcher"); return; }
                PlatformShell.Create().OpenFile(full);
            }
            catch (Exception ex)
            {
                Logger.LogException("Open included document", ex);
                await Dialogs.ShowMessageAsync(ex.Message, "DW2 Mod Launcher");
            }
        }

        // ---- creating and deleting local mods

        /// <summary>Disables every mod by writing an empty load order to mods.json (refused while the game runs).</summary>
        private async Task ClearAsync()
        {
            if (modOrder.Order == null || modOrder.Order.Count == 0) return;
            if (GameProcess.IsRunning()) { await Dialogs.ShowMessageAsync(T("GameRunningWarning"), "DW2 Mod Launcher"); return; }
            try
            {
                ModOrderStore.Write(ModsJsonPath(), new List<string>());
                Refresh();
                SetStatus(T("DW2ModSettingsSaved"));
            }
            catch (Exception ex)
            {
                Logger.LogException("Clear mods.json", ex);
                await Dialogs.ShowMessageAsync(ex.Message, "DW2 Mod Launcher");
            }
        }

        /// <summary>Enables every listed mod by appending the ones not yet in mods.json to the load order (existing order kept).</summary>
        private async Task EnableAllAsync()
        {
            if (modOrder.ReadFailed) { await Dialogs.ShowMessageAsync(T("ModsJsonInvalidWarning"), "DW2 Mod Launcher"); return; }
            if (GameProcess.IsRunning()) { await Dialogs.ShowMessageAsync(T("GameRunningWarning"), "DW2 Mod Launcher"); return; }
            List<string> order = new List<string>(modOrder.Order ?? new List<string>());
            foreach (ModInfo mod in Mods.Select(r => r.Mod))
                if (!string.IsNullOrWhiteSpace(mod.ActiveToken) && !order.Contains(mod.ActiveToken, StringComparer.OrdinalIgnoreCase)) order.Add(mod.ActiveToken);
            if (order.Count == (modOrder.Order?.Count ?? 0)) return;
            try
            {
                List<string> written = ModOrderStore.Write(ModsJsonPath(), order);
                if (written == null) return;
                modOrder.Order = written;
                modOrder.FileFound = true;
            }
            catch (Exception ex)
            {
                Logger.LogException("Enable all mods", ex);
                await Dialogs.ShowMessageAsync(ex.Message, "DW2 Mod Launcher");
                return;
            }
            // Everything is enabled at once; the conflict analysis that follows is slow, so it runs behind the busy overlay.
            RefreshRowStates();
            SetStatus(T("DW2ModSettingsSaved"));
            Settings.UpdateCommandPreview();
            await AnalyzeInBackgroundAsync();
        }

        private bool isBusy;
        private string busyText = "";
        /// <summary>True while a long job runs: the window shows a darkened, non-dismissable overlay with <see cref="BusyText"/>.</summary>
        public bool IsBusy { get { return isBusy; } private set { Set(ref isBusy, value); } }
        public string BusyText { get { return busyText; } private set { Set(ref busyText, value); } }

        /// <summary>Runs the conflict analysis off the UI thread behind the busy overlay, then refreshes the rows' health.</summary>
        private async Task AnalyzeInBackgroundAsync()
        {
            BusyText = T("AnalyzingConflicts");
            IsBusy = true;
            try { await Task.Run(() => Analyze()); }
            catch (Exception ex) { Logger.LogException("Analyze conflicts", ex); }
            finally { IsBusy = false; }
            RefreshRowStates();
            UpdateStatus();
        }

        private async Task CreateModAsync()
        {
            if (string.IsNullOrWhiteSpace(settings.ManagedModsRoot))
            {
                await Dialogs.ShowMessageAsync(T("CreateModNoFolder"), "DW2 Mod Launcher");
                return;
            }
            string name = await Dialogs.PromptTextAsync(T("CreateModTitle"), T("CreateModPrompt"), "", T("OK"), T("Cancel"),
                text => string.IsNullOrWhiteSpace(text) ? "" : T("CreateModFolderPreview", LocalModManager.FolderNameFor(text)));
            if (name == null) return;
            try
            {
                string folder = LocalModManager.Create(settings.ManagedModsRoot, name);
                Refresh();
                SelectedRow = Mods.FirstOrDefault(r => !r.Mod.IsWorkshop && string.Equals(r.Mod.Folder, folder, StringComparison.OrdinalIgnoreCase));
                SetStatus(T("ModCreatedStatus", name));
            }
            catch (Exception ex)
            {
                Logger.LogException("Create local Mod", ex);
                await Dialogs.ShowMessageAsync(T("CreateModFailed", ex.Message), "DW2 Mod Launcher");
            }
        }

        private async Task DeleteModAsync()
        {
            if (selectedRow == null) return;
            ModInfo mod = selectedRow.Mod;
            if (!LocalModManager.CanDelete(mod, settings.ManagedModsRoot)) return;
            string name = mod.DisplayName ?? mod.Id ?? Path.GetFileName(mod.Folder);
            string message = T("ConfirmDeleteMod", name, mod.Folder);
            if (!string.IsNullOrWhiteSpace(mod.WorkshopId)) message += "\n\n" + T("ConfirmDeleteModPublished", mod.WorkshopId);
            if (!await Dialogs.ConfirmAsync(message, T("DeleteModTitle"), T("Delete"), T("Cancel"))) return;
            if (GameProcess.IsRunning())
            {
                await Dialogs.ShowMessageAsync(T("GameRunningWarning"), "DW2 Mod Launcher");
                return;
            }
            try
            {
                // Take it out of mods.json first so DW2 is never left pointing at a folder that no longer exists.
                if (IsSelected(mod))
                {
                    SetEnabledResult result = ModLibrary.SetEnabled(mod, false, settings, modOrder);
                    if (result.Outcome != SetEnabledOutcome.Saved)
                    {
                        string reason = result.Outcome == SetEnabledOutcome.ModsJsonInvalid ? T("ModsJsonInvalidWarning")
                            : result.Error != null ? result.Error.Message : T("ModsJsonERROR");
                        await Dialogs.ShowMessageAsync(T("DeleteModFailed", reason), "DW2 Mod Launcher");
                        Refresh();
                        return;
                    }
                }
                settings.SelectedMods.Remove(mod.Key);
                SaveSettings();
                LocalModManager.Delete(mod, settings.ManagedModsRoot);
                Refresh();
                SetStatus(T("ModDeletedStatus", name));
            }
            catch (Exception ex)
            {
                Logger.LogException("Delete local Mod", ex);
                await Dialogs.ShowMessageAsync(T("DeleteModFailed", ex.Message), "DW2 Mod Launcher");
                Refresh();
            }
        }

        // ---- mod settings editor

        private static bool HasModSettings(ModRowViewModel row)
        {
            return row != null && ModSettingsSchemaReader.Read(row.Mod.ContentRoot ?? row.Mod.Folder) != null;
        }

        public async Task OpenModSettingsAsync()
        {
            if (selectedRow == null) return;
            ModInfo mod = selectedRow.Mod;
            ModSettingsSchema schema = ModSettingsSchemaReader.Read(mod.ContentRoot ?? mod.Folder);
            if (schema == null) { await Dialogs.ShowMessageAsync(T("NoConfigurableSettings"), "DW2 Mod Launcher"); return; }
            try
            {
                JsonObject values = ModSettingsStore.GetOrCreateValues(mod, schema);
                ModSettingsEditorViewModel editor = new ModSettingsEditorViewModel(
                    T("ModSettingsTitle") + (mod.DisplayName ?? mod.Id ?? Path.GetFileName(mod.Folder)), schema, values, L);
                if (!await Dialogs.EditModSettingsAsync(editor)) return;
                ModSettingsStore.SaveValues(mod, editor.Apply());
                SetStatus(T("ModSettingsSaved"));
            }
            catch (Exception ex)
            {
                Logger.LogException("Mod settings editor", ex);
                await Dialogs.ShowMessageAsync(ex.Message, "DW2 Mod Launcher");
            }
        }

        // ---- Workshop publishing

        private async Task PublishAsync()
        {
            if (selectedRow == null || publishRunning) return;
            ModInfo mod = selectedRow.Mod;
            if (mod.IsWorkshop || string.IsNullOrWhiteSpace(mod.ModJsonPath))
            {
                await Dialogs.ShowMessageAsync(T("PublishNotLocalMod"), "DW2 Mod Launcher");
                return;
            }
            ModPublishMetadata metadata;
            try { metadata = ModPublishMetadataEditor.Read(mod.ModJsonPath); }
            catch (Exception ex)
            {
                Logger.LogException("Read mod.json for publish", ex);
                await Dialogs.ShowMessageAsync(ex.Message, "DW2 Mod Launcher");
                return;
            }
            bool isUpdate = !string.IsNullOrWhiteSpace(mod.WorkshopId);
            PublishDialogViewModel editor = new PublishDialogViewModel(Dialogs, L, mod, metadata, isUpdate);
            if (!await Dialogs.EditPublishAsync(editor)) return;

            publishRunning = true;
            PublishCommand.RaiseCanExecuteChanged();
            SetStatus(T("PublishRunning"));
            string contentFolder = mod.ContentRoot ?? mod.Folder;
            ModPublishRequest request = new ModPublishRequest
            {
                ContentFolder = contentFolder,
                Title = metadata.DisplayName,
                Description = metadata.Description,
                PreviewImagePath = string.IsNullOrWhiteSpace(metadata.PreviewImage) ? null : Path.Combine(contentFolder, metadata.PreviewImage),
                ExistingWorkshopId = long.TryParse(mod.WorkshopId, out long existingId) ? existingId : (long?)null,
                Visibility = editor.SelectedVisibility
            };
            CancellationTokenSource cancel = new CancellationTokenSource();
            request.Cancel = cancel.Token;
            try
            {
                IModPublisher publisher = ModPublisherFactory.Create(uint.Parse(SteamLocator.AppId));
                ModPublishResult result;
                // Stay modal until Steam answers; Cancel only appears if that takes a while.
                using (Dialogs.ShowBusy(T("PublishRunning"), T("PublishToWorkshop"), T("Cancel"), TimeSpan.FromSeconds(15), cancel))
                    result = await Task.Run(() => publisher.Publish(request));
                if (cancel.IsCancellationRequested)
                {
                    if (result.WorkshopId.HasValue) ModJsonWorkshopIdWriter.Write(mod.ModJsonPath, result.WorkshopId.Value);
                    SetStatus(T("PublishCancelledStatus"));
                    return;
                }
                if (!result.WorkshopId.HasValue || !string.IsNullOrEmpty(result.ErrorMessage))
                {
                    // A created-but-failed upload still has an id: keep it so a retry updates instead of duplicating.
                    if (result.WorkshopId.HasValue) ModJsonWorkshopIdWriter.Write(mod.ModJsonPath, result.WorkshopId.Value);
                    await Dialogs.ShowMessageAsync(T("PublishFailed", result.ErrorMessage ?? ""), "DW2 Mod Launcher");
                    SetStatus(T("PublishFailedStatus"));
                    return;
                }
                ModJsonWorkshopIdWriter.Write(mod.ModJsonPath, result.WorkshopId.Value);
                string url = "https://steamcommunity.com/sharedfiles/filedetails/?id=" + result.WorkshopId.Value;
                string message = T("PublishCapturedIdMessage", result.WorkshopId.Value, url);
                if (result.NeedsWorkshopAgreement) message += "\n\n" + T("PublishNeedsWorkshopAgreement");
                SetStatus(T("WorkshopIdSaved"));
                if (await Dialogs.ConfirmAsync(message, T("PublishToWorkshop"), T("OpenInBrowser"), T("Close")))
                    PlatformShell.Create().OpenUrl(url);
                Refresh();
            }
            catch (Exception ex)
            {
                Logger.LogException("Publish Mod to Workshop", ex);
                await Dialogs.ShowMessageAsync(T("PublishFailed", ex.Message), "DW2 Mod Launcher");
                SetStatus(T("PublishFailedStatus"));
            }
            finally
            {
                publishRunning = false;
                PublishCommand.RaiseCanExecuteChanged();
            }
        }

        // ---- Workshop updates

        public async Task BeginWorkshopUpdateCheck(bool manual)
        {
            if (updateCheckRunning || workshopMods.Count == 0) return;
            updateCheckRunning = true;
            CheckUpdatesCommand.RaiseCanExecuteChanged();
            SetStatus(T("CheckingSteamWorkshopUpdates"));
            try
            {
                string root = settings.WorkshopRoot;
                List<ModInfo> snapshot = workshopMods.ToList();
                WorkshopUpdateCheckResult result = await Task.Run(() => WorkshopUpdateService.Check(root, snapshot));
                int updates = WorkshopUpdateService.Apply(result, workshopMods);
                settings.LastWorkshopUpdateCheckUtc = DateTime.UtcNow.ToString("o");
                SaveSettings();
                ConflictAnalyzer.AnalyzeDuplicates(AllMods);
                RefreshRowStates();
                if (!string.IsNullOrWhiteSpace(result.Error)) SetStatus(T("WorkshopCheckSteamFailed") + result.Error);
                else if (updates > 0)
                {
                    SetStatus(T("WorkshopUpdatesAvailableStatus") + updates);
                    if (manual && await Dialogs.ConfirmAsync(T("BackupBeforeUpdatePrompt"), T("PreUpdateBackup"), T("Yes"), T("No")))
                        await BackupAsync(workshopMods.Where(m => m.UpdateState == "update").ToList());
                }
                else SetStatus(T("SteamWorkshopNoUpdatesFound"));
            }
            catch (Exception ex)
            {
                Logger.LogException("Workshop update check", ex);
                SetStatus(T("WorkshopCheckFailed"));
            }
            finally
            {
                updateCheckRunning = false;
                CheckUpdatesCommand.RaiseCanExecuteChanged();
            }
        }

        private async Task BackupAsync(List<ModInfo> mods)
        {
            string root = Path.Combine(appRoot, "WorkshopBackups", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            try
            {
                int count = await Task.Run(() => WorkshopUpdateService.Backup(root, mods));
                await Dialogs.ShowMessageAsync(T("WorkshopBackupsSaved") + count + "\n" + root, "DW2 Mod Launcher");
            }
            catch (Exception ex)
            {
                Logger.LogException("Workshop backup", ex);
                await Dialogs.ShowMessageAsync(ex.Message, "DW2 Mod Launcher");
            }
        }

        private void UpdateStatus()
        {
            int updates = workshopMods.Count(m => m.UpdateState == "update");
            int selected = AllMods.Count(IsSelected);
            string conflictText = collisions.Count == 0 ? T("NoConflictsStatus") : T("ConflictFiles") + collisions.Count;
            string updateText = updates == 0 ? T("NoUpdatesUnchecked") : T("Updates") + updates;
            if (modOrder.ReadFailed) updateText = T("ModsJsonERROR");
            int duplicates = AllMods.Count(m => m.DuplicateCount > 0);
            string duplicateText = duplicates == 0 ? T("NoDuplicateInstallations") : T("DuplicateInstallations") + duplicates;
            SetStatus(string.Format(T("DW2ModsWorkshopEnabled"), managedMods.Count, workshopMods.Count, selected, conflictText, duplicateText, updateText));
        }

        public List<ModInfo> OrderedEnabledMods()
        {
            return GameLauncher.OrderedEnabled(AllMods, modOrder, settings);
        }

        public string BuildLaunchArguments(string globalLaunchArguments)
        {
            return GameLauncher.BuildArguments(globalLaunchArguments);
        }

        private async Task PlayAsync()
        {
            Settings.CommitToSettings();
            Analyze();
            RefreshRowStates();
            List<ModInfo> enabled = AllMods.Where(IsSelected).ToList();
            List<string> diagnostics = LaunchDiagnostics.Build(enabled, modOrder, GameLauncher.LoaderDllPath(),
                LoaderManifestBuilder.Build(OrderedEnabledMods()).Entries, key => T(key));
            if (diagnostics.Count > 0 && !await Dialogs.ConfirmAsync(
                T("DiagnosticsFoundIssues") + string.Join("\n", diagnostics.Take(30)) + T("LaunchAnyway"),
                T("PreLaunchDiagnostics"), T("Yes"), T("No"))) return;
            if (collisions.Count > 0 && !await Dialogs.ConfirmAsync(
                LaunchDiagnostics.BuildConflictWarning(collisions, key => T(key)), T("ModConflictWarning"), T("Yes"), T("No"))) return;
            if (GameProcess.IsRunning()) { SetGameState(GameState.Running); return; }
            if (!SteamLocator.IsGameRoot(settings.GameRoot))
            {
                await Dialogs.ShowMessageAsync(T("GameExeNotFound"), "DW2 Mod Launcher");
                return;
            }
            if (!await Settings.ConfirmSaveForLaunchAsync()) return;
            try
            {
                GameLauncher.WriteLoaderManifest(OrderedEnabledMods());
                Process.Start(GameLauncher.BuildStartInfo(settings.GameRoot, GameLauncher.BuildArguments(settings.GlobalLaunchArguments), settings.LaunchEnvironment));
                SetStatus(T("DistantWorlds2Launched"));
                launchStartedUtc = DateTime.UtcNow;
                SetGameState(GameState.Launching);
            }
            catch (Exception ex)
            {
                Logger.LogException("Launch game", ex);
                await Dialogs.ShowMessageAsync(ex.Message, "DW2 Mod Launcher");
            }
        }
    }
}
