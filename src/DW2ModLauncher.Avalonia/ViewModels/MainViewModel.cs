using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using DW2ModLauncher.Avalonia.Services;
using DW2ModLauncher.Core.Diagnostics;
using System.Text.Json.Nodes;
using DW2ModLauncher.Core;
using DW2ModLauncher.Core.Models;
using DW2ModLauncher.Core.Services.Publishing;
using DW2ModLauncher.Core.Services.Updates;
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
        private string detailSource = "";
        private string detailWorkshopId = "";
        private bool verifyingPublishedIds;
        private string problemsText = "";
        private bool problemsIsConflict;
        private Bitmap preview;
        private bool publishRunning;

        public MainViewModel(IDialogService dialogs, string appRoot)
        {
            Dialogs = dialogs;
            this.appRoot = appRoot;
            settingsStore = new LauncherSettingsStore(Path.Combine(appRoot, "launcher_settings.json"));
            settings = settingsStore.Load();
            NormalizeSettings();
            sortColumn = settings.SortColumn >= 0 && settings.SortColumn <= 4 ? settings.SortColumn : -1;
            sortAscending = settings.SortAscending;
            if (string.IsNullOrWhiteSpace(settings.ManagedModsRoot))
                settings.ManagedModsRoot = string.IsNullOrWhiteSpace(settings.GameRoot) ? "" : Path.Combine(settings.GameRoot, "mods");

            L.SetLanguage(settings.Language);
            Dialogs.OkText = T("OK");
            Dialogs.CopyText = T("CopyToClipboard");
            Dialogs.OpenLogText = T("OpenLog");
            foreach (string code in Localization.AvailableLanguageCodes())
                Languages.Add(new LanguageOption { Code = code, DisplayName = Localization.DisplayNameFor(code) });
            selectedLanguage = Languages.FirstOrDefault(l => l.Code == settings.Language) ?? Languages[0];

            Settings = new SettingsViewModel(this);
            OpenSettingsCommand = new RelayCommand(() => Dialogs.ShowSettingsAsync(Settings));
            OpenAboutCommand = new RelayCommand(() => Dialogs.ShowAboutAsync(this));
            RefreshCommand = new RelayCommand(() => { Refresh(); var _ = VerifyPublishedIdsAsync(); });
            ClearCommand = new RelayCommand(ClearAsync);
            EnableAllCommand = new RelayCommand(EnableAllAsync);
            PlayCommand = new RelayCommand(PlayOrStopAsync, () => gameState != GameState.Launching);
            SetLaunchModeCommand = RelayCommand.WithParameter(mode => LaunchMode = (LaunchMode)Enum.Parse(typeof(LaunchMode), (string)mode));
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
            EditPropertiesCommand = new RelayCommand(() => EditPropertiesAsync(selectedRow.Mod), () => selectedRow != null && !selectedRow.Mod.IsWorkshop && !string.IsNullOrWhiteSpace(selectedRow.Mod.ModJsonPath));
            CheckLauncherUpdateCommand = new RelayCommand(() => CheckForLauncherUpdateAsync(true));
            CheckUpdatesCommand = new RelayCommand(() => BeginWorkshopUpdateCheck(true), () => !updateCheckRunning && workshopMods.Count > 0);
            CreateModCommand = new RelayCommand(CreateModAsync);
            DeleteModCommand = new RelayCommand(DeleteModAsync, () => selectedRow != null && LocalModManager.CanDelete(selectedRow.Mod, settings.ManagedModsRoot));

            PathDetector.Detect(settings, false);
            SaveSettings();
            Refresh();
            // Reselect what was selected last time; the Mods view scrolls it into view when it loads.
            if (!string.IsNullOrEmpty(settings.LastSelectedMod))
                SelectedRow = Mods.FirstOrDefault(r => string.Equals(r.Mod.ActiveToken, settings.LastSelectedMod, StringComparison.OrdinalIgnoreCase)) ?? selectedRow;
            var verify = VerifyPublishedIdsAsync();
        }

        public IDialogService Dialogs { get; }
        public LocalizedStrings L { get; } = new LocalizedStrings();
        public SettingsViewModel Settings { get; }
        public ObservableCollection<LanguageOption> Languages { get; } = new ObservableCollection<LanguageOption>();
        public ObservableCollection<ModRowViewModel> Mods { get; } = new ObservableCollection<ModRowViewModel>();

        public RelayCommand OpenSettingsCommand { get; }
        public RelayCommand OpenAboutCommand { get; }
        public RelayCommand CheckLauncherUpdateCommand { get; }
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
            get
            {
                if (gameState == GameState.Idle && LaunchMode == LaunchMode.Continue)
                {
                    string save = SaveGames.LatestName(settings.GameRoot);
                    return save == null ? T("PlayContinueNoSave") : T("PlayContinueTooltip", save);
                }
                return T(gameState == GameState.Launching ? "LaunchingButton" : gameState == GameState.Running ? "StopTooltip" : LaunchMode == LaunchMode.NewGame ? "PlayNewGameTooltip" : "PlayTooltip");
            }
        }
        public string PlayLabel
        {
            get { return T(gameState == GameState.Launching ? "LaunchingButton" : gameState == GameState.Running ? "StopButton" : LaunchMode == LaunchMode.Continue ? "PlayContinueButton" : LaunchMode == LaunchMode.NewGame ? "PlayNewGameButton" : "PlayButton"); }
        }

        /// <summary>The Play button's remembered mode; picking one from the drop-down changes what the button does until another is picked.</summary>
        public LaunchMode LaunchMode
        {
            get { return Enum.IsDefined(settings.LaunchMode) ? settings.LaunchMode : LaunchMode.Run; }
            set
            {
                if (settings.LaunchMode == value) return;
                settings.LaunchMode = value;
                SaveSettings();
                Raise(nameof(LaunchMode));
                Raise(nameof(PlayLabel));
                Raise(nameof(PlayTooltip));
                Settings.UpdateCommandPreview();
            }
        }
        public RelayCommand SetLaunchModeCommand { get; }

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
        public RelayCommand EditPropertiesCommand { get; }
        public RelayCommand CreateModCommand { get; }
        public RelayCommand DeleteModCommand { get; }

        public ModRowViewModel SelectedRow
        {
            get { return selectedRow; }
            set
            {
                if (!Set(ref selectedRow, value)) return;
                // A null selection is usually the list being rebuilt, not the user deselecting, so only a real pick is remembered.
                if (value != null && !string.IsNullOrEmpty(value.Mod.ActiveToken)) settings.LastSelectedMod = value.Mod.ActiveToken;
                Raise(nameof(SelectedIsWorkshop));
                Raise(nameof(SelectedHasSteamPage));
                ShowDetails();
                OpenSelectedFolderCommand.RaiseCanExecuteChanged();
                OpenSteamPageCommand.RaiseCanExecuteChanged();
                OpenDocsCommand.RaiseCanExecuteChanged();
                ModSettingsCommand.RaiseCanExecuteChanged();
                PublishCommand.RaiseCanExecuteChanged();
                EditPropertiesCommand.RaiseCanExecuteChanged();
                DeleteModCommand.RaiseCanExecuteChanged();
            }
        }

        public string DetailTitle { get { return detailTitle; } private set { Set(ref detailTitle, value); } }
        public string DetailText { get { return detailText; } private set { Set(ref detailText, value); } }
        public string DetailSource { get { return detailSource; } private set { Set(ref detailSource, value); } }
        public string DetailWorkshopId { get { return detailWorkshopId; } private set { Set(ref detailWorkshopId, value); } }
        public string ProblemsText { get { return problemsText; } private set { Set(ref problemsText, value); Raise(nameof(HasProblems)); } }
        public bool HasProblems { get { return !string.IsNullOrEmpty(problemsText); } }
        public bool ProblemsIsConflict { get { return problemsIsConflict; } private set { Set(ref problemsIsConflict, value); } }
        public Bitmap Preview { get { return preview; } private set { Set(ref preview, value); } }
        public bool HasSelection { get { return selectedRow != null; } }
        /// <summary>Update checks only apply to Steam Workshop items, so the context menu offers them for those alone.</summary>
        public bool SelectedHasSteamPage { get { return SteamPageId(selectedRow) != null; } }
        public bool SelectedIsWorkshop { get { return selectedRow != null && selectedRow.Mod.IsWorkshop; } }

        public LauncherSettings LauncherSettings { get { return settings; } }
        public LauncherSettingsStore SettingsStore { get { return settingsStore; } }
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
                Dialogs.CopyText = T("CopyToClipboard");
                Dialogs.OpenLogText = T("OpenLog");
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

        /// <summary>Stored the moment a preview image is picked, so the folder is remembered even if the publish dialog is then cancelled.</summary>
        private void RememberArtFolder(string folder)
        {
            settings.LastArtFolder = folder;
            SaveSettings();
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
            SyncEnabledModSettings();
            Settings.LoadFromSettings();
            UpdateStatus();
        }

        /// <summary>Completes the settings file of every enabled mod (keys added by a mod update appear at their defaults).</summary>
        private void SyncEnabledModSettings()
        {
            foreach (ModInfo mod in AllMods)
            {
                if (!IsSelected(mod)) continue;
                try
                {
                    ModSettingsSchema schema = ModSettingsSchemaReader.Read(mod.ContentRoot ?? mod.Folder);
                    if (schema != null) ModSettingsStore.GetOrCreateValues(mod, schema);
                }
                catch (Exception ex) { Logger.LogException("Sync mod settings", ex); }
            }
        }

        private void Analyze()
        {
            collisions = ConflictAnalyzer.Analyze(AllMods, m => modOrder.IsEnabledForConflict(m, settings));
            ConflictAnalyzer.AnalyzeEnabledCopies(AllMods, m => modOrder.IsEnabledForConflict(m, settings));
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
                Mods.Add(new ModRowViewModel(mod, RelayCommand.WithParameter(row => { var _ = ToggleAsync((ModRowViewModel)row); }), settings.ManagedModsRoot));
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
            SaveSortPreference();
        }

        private void SaveSortPreference()
        {
            settings.SortColumn = sortColumn;
            settings.SortAscending = sortAscending;
            SaveSettings();
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
            // Sorted by load order, enabling a mod gives it a number and unchecking drops it below the last enabled one (unnumbered rows
            // sort last), so it changes place either way: follow it there and keep it selected.
            if (sortColumn == 4)
            {
                ApplySort();
                SelectedRow = row;
                RevealSelectionRequested?.Invoke();
            }
            UpdateStatus();
            Settings.UpdateCommandPreview();
        }

        /// <summary>Raised when the view should scroll the selected row into view, centred where there is room.</summary>
        public event Action RevealSelectionRequested;

        /// <summary>Moves a row (drag and drop) and writes the new load order. Sorting is dropped: the list now shows the manual order.</summary>
        public void MoveRow(ModRowViewModel row, int toIndex)
        {
            int from = Mods.IndexOf(row);
            if (from < 0) return;
            sortColumn = -1;
            SaveSortPreference();
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
                DetailSource = "";
                DetailWorkshopId = "";
                ProblemsText = "";
                Preview = null;
                return;
            }
            ReloadLocalFiles(mod);
            bool selected = IsSelected(mod);
            int severity = ModHealth.Severity(mod, selected);
            DetailTitle = (mod.DisplayName ?? "") + (string.IsNullOrWhiteSpace(mod.Version) ? "" : "  v" + mod.Version);
            DetailText = ModDetails.BuildText(mod, selected, key => T(key));
            DetailSource = T("Source") + ": " + ModDetails.SourceText(mod, settings.ManagedModsRoot);
            DetailWorkshopId = T("PublishWorkshopId") + ": " + (ModDetails.WorkshopId(mod) ?? T("PublishWorkshopIdUnpublished"));
            List<string> problems = ModDetails.BuildProblems(mod, severity, key => T(key));
            ProblemsIsConflict = severity == 3;
            ProblemsText = problems.Count == 0 ? "" : (severity == 3 ? T("HealthConflict") : T("HealthCaution")) + "\n" + string.Join("\n", problems);
            Preview = ImageLoader.Load(mod.PreviewImage, 0);
        }

        /// <summary>
        /// A local mod's files are the author's to edit at any time, so the details pane re-reads what it shows (names, description,
        /// preview, included tools/documents) instead of trusting the last scan. Conflict and state fields are left alone.
        /// </summary>
        private void ReloadLocalFiles(ModInfo mod)
        {
            if (mod.IsWorkshop || string.IsNullOrWhiteSpace(mod.ModJsonPath) || !File.Exists(mod.ModJsonPath)) return;
            try
            {
                ModInfo fresh = ModScanner.ReadModInfo(mod.Folder, mod.ModJsonPath, false, key => T(key));
                mod.DisplayName = fresh.DisplayName;
                mod.Version = fresh.Version;
                mod.Description = fresh.Description;
                mod.DescriptionOverride = fresh.DescriptionOverride;
                mod.PreviewImage = fresh.PreviewImage;
                mod.IncludedTools = fresh.IncludedTools;
                mod.IncludedDocuments = fresh.IncludedDocuments;
            }
            catch (Exception ex) { Logger.LogException("Reload mod files for details", ex); }
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
            if (id != null) OpenSteamPage(id);
        }

        /// <summary>Opens the item in the Steam client, falling back to the default browser if the steam:// link can't be handled.</summary>
        private void OpenSteamPage(string id)
        {
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

        private bool launcherUpdateRunning;
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
            int kind = await Dialogs.ChooseAsync(T("CreateModKindPrompt"), T("CreateModTitle"),
                new[] { T("CreateModKindEmpty"), T("CreateModKindLocalization"), T("Cancel") });
            if (kind < 0 || kind > 1) return;
            bool localization = kind == 1;

            // Localization: translate the base game (with the enabled Mods), or just one existing Mod.
            ModInfo targetMod = null;
            if (localization)
            {
                int scope = await Dialogs.ChooseAsync(T("LocalizationScopePrompt"), T("CreateModKindLocalization"),
                    new[] { T("LocalizationScopeBaseGame"), T("LocalizationScopeMod"), T("Cancel") });
                if (scope < 0 || scope > 1) return;
                if (scope == 0 && !Directory.Exists(Path.Combine(settings.GameRoot ?? "", "data")))
                {
                    await Dialogs.ShowMessageAsync(T("LocalizationNoGameData"), "DW2 Mod Launcher");
                    return;
                }
                if (scope == 1)
                {
                    List<ModInfo> candidates = AllMods.Where(m => !string.IsNullOrWhiteSpace(m.ContentRoot ?? m.Folder)).ToList();
                    List<string> labels = candidates.Select(m => m.DisplayName ?? m.Id ?? Path.GetFileName(m.Folder)).ToList();
                    for (int i = 0; i < labels.Count; i++)
                    {
                        if (labels.Count(l => l == labels[i]) > 1) labels[i] += " (" + (candidates[i].Id ?? Path.GetFileName(candidates[i].Folder)) + ")";
                    }
                    if (labels.Count == 0)
                    {
                        await Dialogs.ShowMessageAsync(T("LocalizationNoMods"), "DW2 Mod Launcher");
                        return;
                    }
                    string picked = await Dialogs.PickFromListAsync(T("CreateModKindLocalization"), T("LocalizationPickMod"), labels);
                    if (picked == null) return;
                    int at = labels.IndexOf(picked);
                    if (at < 0) return;
                    targetMod = candidates[at];
                }
            }
            string targetLabel = targetMod == null ? null : targetMod.DisplayName ?? targetMod.Id ?? Path.GetFileName(targetMod.Folder);

            string name;
            if (localization)
            {
                Func<string, string> modName = lang => targetLabel == null ? T("LocalizationModName", lang.Trim()) : T("LocalizationModNameFor", lang.Trim(), targetLabel);
                string language = await Dialogs.PromptTextAsync(T("CreateModKindLocalization"), T("LocalizationLanguagePrompt"), "", T("OK"), T("Cancel"),
                    text => string.IsNullOrWhiteSpace(text) ? "" : T("CreateModFolderPreview", LocalModManager.FolderNameFor(modName(text))));
                if (language == null) return;
                name = modName(language);
            }
            else
            {
                name = await Dialogs.PromptTextAsync(T("CreateModTitle"), T("CreateModPrompt"), "", T("OK"), T("Cancel"),
                    text => string.IsNullOrWhiteSpace(text) ? "" : T("CreateModFolderPreview", LocalModManager.FolderNameFor(text)));
                if (name == null) return;
            }
            try
            {
                string folder = LocalModManager.Create(settings.ManagedModsRoot, name, localization ? T("LocalizationModDescription") : "");
                if (localization)
                {
                    IsBusy = true;
                    LocalizationResult built;
                    try
                    {
                        string gameRoot = settings.GameRoot;
                        List<ModInfo> enabled = OrderedEnabledMods();
                        ModInfo target = targetMod;
                        built = await Task.Run(() => target != null
                            ? LocalizationModBuilder.Build(folder, LocalizationModBuilder.CollectModDataFiles(target), LocalizationModBuilder.CollectModTextFiles(target))
                            : LocalizationModBuilder.Build(folder, LocalizationModBuilder.CollectDataFiles(gameRoot, enabled), LocalizationModBuilder.CollectTextFiles(gameRoot, enabled)));
                    }
                    finally { IsBusy = false; }
                    if (built.Files == 0 && built.TextFiles == 0)
                    {
                        // Nothing to translate (e.g. a Mod without data files): do not leave an empty Mod behind.
                        Directory.Delete(folder, true);
                        await Dialogs.ShowMessageAsync(T("LocalizationNothing", targetLabel ?? ""), "DW2 Mod Launcher");
                        return;
                    }
                    await Dialogs.ShowMessageAsync(T("LocalizationDone", built.Strings, built.Files, built.Unreadable.Count, built.TextFiles), "DW2 Mod Launcher");
                }
                Refresh();
                SelectedRow = Mods.FirstOrDefault(r => !r.Mod.IsWorkshop && string.Equals(r.Mod.Folder, folder, StringComparison.OrdinalIgnoreCase));
                SetStatus(T("ModCreatedStatus", name));
                // Straight into the properties dialog so the author can fill in the rest of mod.json.
                if (SelectedRow != null) await EditPropertiesAsync(SelectedRow.Mod);
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
            if (row == null) return false;
            ModSettingsSchema schema = ModSettingsSchemaReader.Read(row.Mod.ContentRoot ?? row.Mod.Folder);
            return schema != null && schema.VisibleFields(row.Mod).Any(f => !string.IsNullOrWhiteSpace(f.Key));
        }

        public async Task OpenModSettingsAsync()
        {
            if (selectedRow == null) return;
            ModInfo mod = selectedRow.Mod;
            ModSettingsSchema schema = ModSettingsSchemaReader.Read(mod.ContentRoot ?? mod.Folder);
            if (schema == null || !schema.VisibleFields(mod).Any()) { await Dialogs.ShowMessageAsync(T("NoConfigurableSettings"), "DW2 Mod Launcher"); return; }
            try
            {
                JsonObject values = ModSettingsStore.GetOrCreateValues(mod, schema);
                string modKey = mod.Id ?? Path.GetFileName(mod.Folder) ?? "";
                if (settings.CollapsedSettingGroups == null) settings.CollapsedSettingGroups = new Dictionary<string, List<string>>();
                List<string> collapsedGroups = settings.CollapsedSettingGroups.TryGetValue(modKey, out List<string> saved0) ? saved0 : new List<string>();
                ModSettingsEditorViewModel editor = new ModSettingsEditorViewModel(
                    T("ModSettingsTitle") + (mod.DisplayName ?? mod.Id ?? Path.GetFileName(mod.Folder)), schema, mod, values, L, Dialogs, settings.ShowHiddenSettings, collapsedGroups);
                bool saved = await Dialogs.EditModSettingsAsync(editor);
                bool dirty = false;
                if (editor.CanShowHidden && editor.ShowHidden != settings.ShowHiddenSettings)
                {
                    settings.ShowHiddenSettings = editor.ShowHidden;
                    dirty = true;
                }
                List<string> nowCollapsed = editor.CollapsedGroups;
                if (!nowCollapsed.SequenceEqual(collapsedGroups))
                {
                    if (nowCollapsed.Count == 0) settings.CollapsedSettingGroups.Remove(modKey);
                    else settings.CollapsedSettingGroups[modKey] = nowCollapsed;
                    dirty = true;
                }
                if (dirty) SaveSettings();
                if (!saved) return;
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

        /// <summary>
        /// Asks Steam whether each local Mod's Workshop item still exists and erases the id of any that were
        /// deleted there, so the list shows the truth and the Mod can be published as new. Silent if Steam can't be asked.
        /// Runs in the Steam helper process, so DW2 only looks "running" to Steam for the second or so it takes.
        /// </summary>
        private async Task VerifyPublishedIdsAsync()
        {
            if (publishRunning || verifyingPublishedIds) return;
            List<ModInfo> published = managedMods.Where(m => !string.IsNullOrWhiteSpace(m.WorkshopId) && !string.IsNullOrWhiteSpace(m.ModJsonPath)).ToList();
            List<long> ids = published.Select(m => long.TryParse(m.WorkshopId, out long id) ? id : 0).Where(id => id != 0).Distinct().ToList();
            if (ids.Count == 0) return;
            verifyingPublishedIds = true;
            try
            {
                IModPublisher publisher = ModPublisherFactory.Create(uint.Parse(SteamLocator.AppId));
                List<long> deleted = await Task.Run(() => publisher.FindDeletedItems(ids));
                if (deleted.Count == 0 || publishRunning) return;
                int cleared = 0;
                foreach (ModInfo mod in published)
                {
                    if (!long.TryParse(mod.WorkshopId, out long id) || !deleted.Contains(id)) continue;
                    try
                    {
                        Logger.Log("Workshop item deleted on Steam", "Erasing workshopId " + id + " from " + mod.ModJsonPath);
                        ModJsonWorkshopIdWriter.Clear(mod.ModJsonPath);
                        cleared++;
                    }
                    catch (Exception ex) { Logger.LogException("Clear deleted Workshop id for " + mod.Folder, ex); }
                }
                if (cleared == 0) return;
                Refresh();
                SetStatus(T("WorkshopItemsDeletedStatus", cleared));
            }
            catch (Exception ex) { Logger.LogException("Verify published Workshop ids", ex); }
            finally { verifyingPublishedIds = false; }
        }

        /// <summary>The item's visibility on Steam, or null when it isn't published or Steam can't be asked.</summary>
        private async Task<ModVisibility?> ReadVisibilityAsync(ModInfo mod)
        {
            if (!long.TryParse(mod.WorkshopId, out long publishedId)) return null;
            return await ReadVisibilityAsync(publishedId);
        }

        private async Task<ModVisibility?> ReadVisibilityAsync(long publishedId)
        {
            SetStatus(T("PublishReadingVisibility"));
            IModPublisher reader = ModPublisherFactory.Create(uint.Parse(SteamLocator.AppId));
            return await Task.Run(() => reader.GetVisibility(publishedId));
        }

        /// <summary>The publish dialog without the publishing: edits the Mod's mod.json fields and saves them. Visibility is shown read-only.</summary>
        private async Task EditPropertiesAsync(ModInfo mod)
        {
            if (mod == null || mod.IsWorkshop || string.IsNullOrWhiteSpace(mod.ModJsonPath)) return;
            ModPublishMetadata metadata;
            try { metadata = ModPublishMetadataEditor.Read(mod.ModJsonPath); }
            catch (Exception ex)
            {
                Logger.LogException("Read mod.json for properties", ex);
                await Dialogs.ShowMessageAsync(ex.Message, "DW2 Mod Launcher");
                return;
            }
            bool isPublished = !string.IsNullOrWhiteSpace(mod.WorkshopId);
            ModVisibility? currentVisibility = isPublished ? await ReadVisibilityAsync(mod) : null;
            string bumpKey = mod.Id ?? Path.GetFileName(mod.Folder) ?? "";
            PublishDialogViewModel editor = new PublishDialogViewModel(Dialogs, L, mod, metadata, isPublished, currentVisibility, propertiesOnly: true, bumpPolicy: settings.VersionBumpFor(bumpKey), lastArtFolder: settings.LastArtFolder, rememberArtFolder: RememberArtFolder);
            if (!await Dialogs.EditPublishAsync(editor)) { UpdateStatus(); return; }
            // The version policy is launcher-side (launcher_settings.json); it only takes effect at the next publish.
            settings.SetVersionBump(bumpKey, editor.BumpPolicy);
            SaveSettings();

            string folder = mod.Folder;
            Refresh();
            SelectedRow = Mods.FirstOrDefault(r => !r.Mod.IsWorkshop && string.Equals(r.Mod.Folder, folder, StringComparison.OrdinalIgnoreCase));
            SetStatus(T("PropertiesSavedStatus", mod.DisplayName ?? mod.Id));
        }

        /// <summary>
        /// Writes a smaller copy of the mod's preview image beside the other mod files (never over anything) and points mod.json's
        /// previewImage at it; <paramref name="metadata"/> follows. Returns the copy's path, or null (after saying why) when the image
        /// could not be shrunk or saved, in which case nothing has changed.
        /// </summary>
        private async Task<string> ShrinkPreviewAsync(ModInfo mod, ModPublishMetadata metadata, string previewFile, string contentRoot)
        {
            FittedPreview fitted = await Task.Run(() => PreviewImageResizer.TryFit(previewFile));
            if (fitted == null)
            {
                await Dialogs.ShowMessageAsync(T("PublishPreviewResizeFailed", Path.GetFileName(previewFile)), "DW2 Mod Launcher");
                return null;
            }
            string target = null;
            string previous = metadata.PreviewImage;
            try
            {
                target = PreviewImageResizer.Save(fitted, contentRoot, Path.GetFileName(PreviewImagePlan.TargetPath(contentRoot, previewFile, fitted.Extension)));
                metadata.PreviewImage = ModFileImporter.RelativePath(contentRoot, target);
                ModPublishMetadataEditor.Write(mod.ModJsonPath, metadata);
                return target;
            }
            catch (Exception ex)
            {
                Logger.LogException("Shrink preview image for publish", ex);
                metadata.PreviewImage = previous;
                try { if (target != null) File.Delete(target); } catch (Exception) { }
                await Dialogs.ShowMessageAsync(ex.Message, "DW2 Mod Launcher");
                return null;
            }
        }

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
            if (!string.IsNullOrWhiteSpace(mod.WorkshopId))
            {
                // The item may have been deleted on Steam since the last check; if so the id is erased and this becomes a new publish.
                string folder = mod.Folder;
                await VerifyPublishedIdsAsync();
                mod = managedMods.FirstOrDefault(m => string.Equals(m.Folder, folder, StringComparison.OrdinalIgnoreCase)) ?? mod;
            }
            bool isUpdate = !string.IsNullOrWhiteSpace(mod.WorkshopId);
            ModVisibility? currentVisibility = isUpdate ? await ReadVisibilityAsync(mod) : null;
            // On an update, ask Steam what the description currently says so the dialog only offers to replace it when it differs.
            string steamDescription = isUpdate ? await Task.Run(() => WorkshopApiClient.FetchDescription(mod.WorkshopId.Trim())) : null;
            string bumpKey = mod.Id ?? Path.GetFileName(mod.Folder) ?? "";
            PublishDialogViewModel editor = new PublishDialogViewModel(Dialogs, L, mod, metadata, isUpdate, currentVisibility, steamDescription: steamDescription, bumpPolicy: settings.VersionBumpFor(bumpKey), lastArtFolder: settings.LastArtFolder, rememberArtFolder: RememberArtFolder);
            if (!await Dialogs.EditPublishAsync(editor)) return;
            // The version policy is remembered per mod in launcher_settings.json (never in the mod itself).
            settings.SetVersionBump(bumpKey, editor.BumpPolicy);
            SaveSettings();
            // Accepting the dialog wrote the (possibly bumped) version to mod.json; any exit below that doesn't publish undoes it.
            string versionBefore = editor.OriginalVersion;
            string versionWritten = metadata.Version;

            // Steam can reject a preview image over 1 MiB: offer to fix it with a smaller copy (the original stays, mod.json is pointed at
            // the copy), or let the author try anyway.
            if (!string.IsNullOrWhiteSpace(metadata.PreviewImage))
            {
                string previewRoot = mod.ContentRoot ?? mod.Folder;
                FileInfo preview = new FileInfo(Path.Combine(previewRoot, metadata.PreviewImage));
                if (preview.Exists && PreviewImagePlan.NeedsResize(preview.Length))
                {
                    string size = (preview.Length / 1048576.0).ToString("0.##");
                    int choice = await Dialogs.ChooseAsync(T("PublishImageTooLargeFix", metadata.PreviewImage, size), "DW2 Mod Launcher",
                        new[] { T("PublishShrinkAndPublish"), T("PublishAnyway"), T("Cancel") });
                    if (choice == 0)
                    {
                        string shrunk = await ShrinkPreviewAsync(mod, metadata, preview.FullName, previewRoot);
                        if (shrunk == null && !await Dialogs.ConfirmAsync(T("PublishImageTooLarge", metadata.PreviewImage, size), "DW2 Mod Launcher", T("Yes"), T("No")))
                        {
                            RollBackVersion(mod, versionBefore, versionWritten);
                            return;
                        }
                    }
                    else if (choice != 1)
                    {
                        RollBackVersion(mod, versionBefore, versionWritten);
                        return;
                    }
                }
            }

            // Other content files (except .bundle) over 5 MiB get the same warn-and-ask treatment.
            const long MaxFileBytes = 5L * 1024 * 1024;
            string contentRoot = mod.ContentRoot ?? mod.Folder;
            string previewFull = string.IsNullOrWhiteSpace(metadata.PreviewImage) ? null : Path.GetFullPath(Path.Combine(contentRoot, metadata.PreviewImage));
            List<string> bigFiles = new List<string>();
            try
            {
                foreach (string file in Directory.EnumerateFiles(contentRoot, "*", SearchOption.AllDirectories))
                {
                    if (previewFull != null && string.Equals(Path.GetFullPath(file), previewFull, StringComparison.OrdinalIgnoreCase)) continue;
                    // Asset bundles are legitimately huge and publish fine.
                    if (string.Equals(Path.GetExtension(file), ".bundle", StringComparison.OrdinalIgnoreCase)) continue;
                    long length = new FileInfo(file).Length;
                    if (length > MaxFileBytes)
                        bigFiles.Add(file.Substring(contentRoot.Length).TrimStart('\\', '/') + " (" + (length / 1048576.0).ToString("0.##") + " MiB)");
                }
            }
            catch (Exception ex) { Logger.LogException("Scan mod for large files", ex); }
            if (bigFiles.Count > 0)
            {
                string list = string.Join("\n", bigFiles.Take(10)) + (bigFiles.Count > 10 ? "\n..." : "");
                if (!await Dialogs.ConfirmAsync(T("PublishFilesTooLarge", list), "DW2 Mod Launcher", T("Yes"), T("No")))
                {
                    RollBackVersion(mod, versionBefore, versionWritten);
                    return;
                }
            }

            publishRunning = true;
            PublishCommand.RaiseCanExecuteChanged();
            SetStatus(T("PublishRunning"));
            string contentFolder = mod.ContentRoot ?? mod.Folder;
            ModPublishRequest request = new ModPublishRequest
            {
                ContentFolder = contentFolder,
                Title = metadata.DisplayName,
                // Null leaves the Steam page's description untouched.
                Description = editor.ReplaceDescription ? ModPublishMetadataEditor.ResolveSteamDescription(contentFolder, metadata) : null,
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
                    RollBackVersion(mod, versionBefore, versionWritten);
                    SetStatus(T("PublishCancelledStatus"));
                    return;
                }
                if (!result.WorkshopId.HasValue || !string.IsNullOrEmpty(result.ErrorMessage))
                {
                    // A created-but-failed upload still has an id: keep it so a retry updates instead of duplicating.
                    if (result.WorkshopId.HasValue) ModJsonWorkshopIdWriter.Write(mod.ModJsonPath, result.WorkshopId.Value);
                    RollBackVersion(mod, versionBefore, versionWritten);
                    await Dialogs.ShowMessageAsync(T("PublishFailed", result.ErrorMessage ?? ""), "DW2 Mod Launcher");
                    SetStatus(T("PublishFailedStatus"));
                    return;
                }
                ModJsonWorkshopIdWriter.Write(mod.ModJsonPath, result.WorkshopId.Value);
                string url = "https://steamcommunity.com/sharedfiles/filedetails/?id=" + result.WorkshopId.Value;
                string message = T("PublishCapturedIdMessage", result.WorkshopId.Value, url);
                if (result.NeedsWorkshopAgreement) message += "\n\n" + T("PublishNeedsWorkshopAgreement");
                // Steam can quietly hold an item private (e.g. until the Workshop agreement is accepted), so check it took the setting.
                ModVisibility? requested = editor.SelectedVisibility;
                ModVisibility? actual = requested.HasValue ? await ReadVisibilityAsync(result.WorkshopId.Value) : null;
                if (requested.HasValue && actual.HasValue && actual != requested)
                    message += "\n\n" + T("PublishVisibilityDiffers", T(PublishDialogViewModel.LabelKey(requested.Value)), T(PublishDialogViewModel.LabelKey(actual.Value)));
                SetStatus(T("WorkshopIdSaved"));
                if (await Dialogs.ConfirmAsync(message, T("PublishToWorkshop"), T("SteamPage"), T("Close")))
                    OpenSteamPage(result.WorkshopId.Value.ToString());
                Refresh();
            }
            catch (Exception ex)
            {
                Logger.LogException("Publish Mod to Workshop", ex);
                RollBackVersion(mod, versionBefore, versionWritten);
                await Dialogs.ShowMessageAsync(T("PublishFailed", ex.Message), "DW2 Mod Launcher");
                SetStatus(T("PublishFailedStatus"));
            }
            finally
            {
                publishRunning = false;
                PublishCommand.RaiseCanExecuteChanged();
            }
        }

        /// <summary>
        /// Puts mod.json's version back after a publish that didn't happen. Only if the file still holds the value this
        /// publish wrote, so a version the author edited by hand in the meantime is never overwritten.
        /// </summary>
        private static void RollBackVersion(ModInfo mod, string before, string written)
        {
            if (string.Equals(before, written, StringComparison.Ordinal)) return;
            try
            {
                if (ModPublishMetadataEditor.Read(mod.ModJsonPath).Version == written)
                    ModPublishMetadataEditor.WriteVersion(mod.ModJsonPath, before ?? "");
            }
            catch (Exception ex) { Logger.LogException("Roll back mod.json version", ex); }
        }

        // ---- Launcher self-update

        private string UpdateWorkDir { get { return Path.Combine(appRoot, "Updates"); } }

        /// <summary>The About box's "Check for updates at startup" box.</summary>
        public bool CheckForLauncherUpdates
        {
            get { return settings.CheckForLauncherUpdates; }
            set
            {
                if (settings.CheckForLauncherUpdates == value) return;
                settings.CheckForLauncherUpdates = value;
                SaveSettings();
                Raise();
            }
        }

        /// <summary>Removes the work folder of an earlier update (its helper copy was still running when the new launcher started).</summary>
        public void CleanUpFinishedUpdate()
        {
            Task.Run(async delegate
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
                LauncherUpdater.CleanUp(UpdateWorkDir);
            });
        }

        /// <summary>Looks for a newer launcher release on GitHub and, if the user agrees, downloads it and restarts into it.</summary>
        public async Task CheckForLauncherUpdateAsync(bool manual)
        {
            if (launcherUpdateRunning) return;
            if (!manual && (!settings.CheckForLauncherUpdates || !LauncherUpdater.CanSelfUpdate)) return;
            launcherUpdateRunning = true;
            try
            {
                if (manual && !LauncherUpdater.CanSelfUpdate)
                {
                    await Dialogs.ShowMessageAsync(T("LauncherUpdateNotSupported", AppVersion.ReleasesUrl), T("LauncherUpdateTitle"));
                    return;
                }
                LauncherRelease release;
                try { release = await UpdateChecker.FindNewerAsync(AppVersion.Current, CancellationToken.None); }
                catch (Exception ex)
                {
                    Logger.LogException("Launcher update check", ex);
                    if (manual) await Dialogs.ShowMessageAsync(T("LauncherUpdateCheckFailed", ex.Message), T("LauncherUpdateTitle"));
                    return;
                }
                if (release == null)
                {
                    if (manual) await Dialogs.ShowMessageAsync(T("LauncherUpdateUpToDate", AppVersion.Display), T("LauncherUpdateTitle"));
                    return;
                }
                if (!manual && string.Equals(settings.SkippedLauncherVersion, release.Version, StringComparison.Ordinal)) return;

                string notes = release.Notes.Trim();
                if (notes.Length > 700) notes = notes.Substring(0, 700).TrimEnd() + "...";
                string message = T("LauncherUpdateAvailable", release.Version, AppVersion.Display) + (notes.Length > 0 ? "\n\n" + notes : "");
                int choice = await Dialogs.ChooseAsync(message, T("LauncherUpdateTitle"), new[] { T("LauncherUpdateNow"), T("LauncherUpdateSkip"), T("LauncherUpdateLater") });
                if (choice == 1)
                {
                    settings.SkippedLauncherVersion = release.Version;
                    SaveSettings();
                }
                if (choice != 0) return;
                await InstallLauncherUpdateAsync(release);
            }
            finally { launcherUpdateRunning = false; }
        }

        private async Task InstallLauncherUpdateAsync(LauncherRelease release)
        {
            CancellationTokenSource cancel = new CancellationTokenSource();
            string staged;
            using (IProgressHandle box = Dialogs.ShowProgress(T("LauncherUpdateDownloading", release.Version), T("LauncherUpdateTitle"), T("Cancel"), cancel))
            {
                // Progress<T> built here posts back to the UI thread, which is where the box must be touched.
                Progress<UpdateProgress> progress = new Progress<UpdateProgress>(delegate (UpdateProgress p)
                {
                    if (p.Extracting) box.Report(null, T("LauncherUpdateExtracting"));
                    else if (p.Total > 0) box.Report((double)p.Done / p.Total, ByteSize.Format(p.Done) + " / " + ByteSize.Format(p.Total));
                    else box.Report(null, ByteSize.Format(p.Done));
                });
                try { staged = await LauncherUpdater.DownloadAndStageAsync(release, UpdateWorkDir, progress, cancel.Token); }
                catch (OperationCanceledException)
                {
                    SetStatus(T("LauncherUpdateCancelled"));
                    return;
                }
                catch (Exception ex)
                {
                    Logger.LogException("Launcher update download", ex);
                    box.Dispose();
                    await Dialogs.ShowMessageAsync(T("LauncherUpdateFailed", ex.Message), T("LauncherUpdateTitle"));
                    return;
                }
            }

            // Last chance to back out: the helper starts waiting for this process to exit.
            if (!await Settings.ConfirmSaveForExitAsync())
            {
                LauncherUpdater.CleanUp(UpdateWorkDir);
                return;
            }
            try { LauncherUpdater.StartInstall(staged, UpdateWorkDir); }
            catch (Exception ex)
            {
                Logger.LogException("Launcher update start", ex);
                await Dialogs.ShowMessageAsync(T("LauncherUpdateFailed", ex.Message), T("LauncherUpdateTitle"));
                return;
            }
            Dialogs.RequestExit();
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
            SetStatus(string.Format(T("DW2ModsWorkshopEnabled"), managedMods.Count, workshopMods.Count, selected, conflictText, updateText));
        }

        public List<ModInfo> OrderedEnabledMods()
        {
            return GameLauncher.OrderedEnabled(AllMods, modOrder, settings);
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
                LoaderManifest launchManifest = GameLauncher.WriteLoaderManifest(OrderedEnabledMods(), settings.LogDirectory);
                Process.Start(GameLauncher.BuildStartInfo(settings.GameRoot, GameLauncher.BuildArguments(LaunchMode, launchManifest.Fonts.LastOrDefault()?.Name)));
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
