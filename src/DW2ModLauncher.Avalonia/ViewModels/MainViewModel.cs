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
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

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
        private bool showingSettings;
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
            ShowModsCommand = new RelayCommand(delegate { ShowingSettings = false; });
            ShowSettingsCommand = new RelayCommand(delegate { ShowingSettings = true; });
            RefreshCommand = new RelayCommand(Refresh);
            PlayCommand = new RelayCommand(PlayAsync);
            SortCommand = RelayCommand.WithParameter(column => SortBy(int.Parse((string)column, System.Globalization.CultureInfo.InvariantCulture)));
            OpenSelectedFolderCommand = new RelayCommand(OpenModFolder, () => selectedRow != null);
            OpenDocsCommand = new RelayCommand(OpenDocsAsync, () => selectedRow != null && selectedRow.Mod.IncludedDocuments != null && selectedRow.Mod.IncludedDocuments.Count > 0);
            ModSettingsCommand = new RelayCommand(OpenModSettingsAsync, () => HasModSettings(selectedRow));
            PublishCommand = new RelayCommand(PublishAsync, () => selectedRow != null && !selectedRow.Mod.IsWorkshop && !publishRunning);
            CheckUpdatesCommand = new RelayCommand(() => BeginWorkshopUpdateCheck(true), () => !updateCheckRunning && workshopMods.Count > 0);

            PathDetector.Detect(settings, false);
            SaveSettings();
            Refresh();
        }

        public IDialogService Dialogs { get; }
        public LocalizedStrings L { get; } = new LocalizedStrings();
        public SettingsViewModel Settings { get; }
        public ObservableCollection<LanguageOption> Languages { get; } = new ObservableCollection<LanguageOption>();
        public ObservableCollection<ModRowViewModel> Mods { get; } = new ObservableCollection<ModRowViewModel>();

        public RelayCommand ShowModsCommand { get; }
        public RelayCommand ShowSettingsCommand { get; }
        public RelayCommand RefreshCommand { get; }
        public RelayCommand PlayCommand { get; }
        public RelayCommand SortCommand { get; }
        public RelayCommand OpenSelectedFolderCommand { get; }
        public RelayCommand OpenDocsCommand { get; }
        public RelayCommand CheckUpdatesCommand { get; }
        public RelayCommand ModSettingsCommand { get; }
        public RelayCommand PublishCommand { get; }

        public ModRowViewModel SelectedRow
        {
            get { return selectedRow; }
            set
            {
                if (!Set(ref selectedRow, value)) return;
                ShowDetails();
                OpenSelectedFolderCommand.RaiseCanExecuteChanged();
                OpenDocsCommand.RaiseCanExecuteChanged();
                ModSettingsCommand.RaiseCanExecuteChanged();
                PublishCommand.RaiseCanExecuteChanged();
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
        public ModOrderState ModOrder { get { return modOrder; } }
        public string AppRoot { get { return appRoot; } }

        public IEnumerable<ModInfo> AllMods { get { return managedMods.Concat(workshopMods); } }

        public string StatusText { get { return statusText; } set { Set(ref statusText, value); } }

        public bool ShowingSettings
        {
            get { return showingSettings; }
            set { if (Set(ref showingSettings, value)) { Raise(nameof(ShowingMods)); } }
        }

        public bool ShowingMods { get { return !showingSettings; } }

        public string GamePathText { get { return "Game: " + (string.IsNullOrEmpty(settings.GameRoot) ? "Not found" : settings.GameRoot); } }
        public string WorkshopPathText { get { return "Workshop: " + (string.IsNullOrEmpty(settings.WorkshopRoot) ? "Not found" : settings.WorkshopRoot); } }

        public LanguageOption SelectedLanguage
        {
            get { return selectedLanguage; }
            set
            {
                if (value == null || !Set(ref selectedLanguage, value)) return;
                settings.Language = value.Code;
                L.SetLanguage(value.Code);
                Dialogs.OkText = T("OK");
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

        /// <summary>Replaces the settings object wholesale (after restoring a snapshot) and rescans.</summary>
        public void ReloadSettings()
        {
            settings = settingsStore.Load();
            NormalizeSettings();
            L.SetLanguage(settings.Language);
            Dialogs.OkText = T("OK");
            selectedLanguage = Languages.FirstOrDefault(l => l.Code == settings.Language) ?? Languages[0];
            Raise(nameof(SelectedLanguage));
            Refresh();
        }

        public void RaisePathsChanged()
        {
            Raise(nameof(GamePathText));
            Raise(nameof(WorkshopPathText));
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
            RaisePathsChanged();
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
            foreach (ModRowViewModel row in Mods)
            {
                bool enabled = IsSelected(row.Mod);
                row.Enabled = enabled;
                int severity = ModHealth.Severity(row.Mod, enabled);
                row.HealthSeverity = severity;
                row.Health = severity == 3 ? T("HealthConflict") : severity == 2 ? T("HealthCaution") : severity == 1 ? T("HealthOk") : T("ModDisabled");
                row.LoadOrderIndex = modOrder.IndexOf(row.Mod.ActiveToken);
                row.RefreshFromMod();
            }
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
            try
            {
                IModPublisher publisher = ModPublisherFactory.Create(uint.Parse(SteamLocator.AppId));
                ModPublishResult result = await Task.Run(() => publisher.Publish(request));
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
            if (!SteamLocator.IsGameRoot(settings.GameRoot))
            {
                await Dialogs.ShowMessageAsync(T("GameExeNotFound"), "DW2 Mod Launcher");
                return;
            }
            try
            {
                GameLauncher.WriteLoaderManifest(OrderedEnabledMods());
                Process.Start(GameLauncher.BuildStartInfo(settings.GameRoot, GameLauncher.BuildArguments(settings.GlobalLaunchArguments), settings.LaunchEnvironment));
                SetStatus(T("DistantWorlds2Launched"));
            }
            catch (Exception ex)
            {
                Logger.LogException("Launch game", ex);
                await Dialogs.ShowMessageAsync(ex.Message, "DW2 Mod Launcher");
            }
        }
    }
}
