using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DW2ModLauncher.Avalonia.Services;
using DW2ModLauncher.Core.Models;
using DW2ModLauncher.Core.Services;
using DW2ModLauncher.Core.Services.Publishing;

namespace DW2ModLauncher.Avalonia.ViewModels
{
    public class VisibilityOption
    {
        /// <summary>Null means "leave as it is" (only offered when updating an existing item).</summary>
        public ModVisibility? Value { get; set; }
        public string Label { get; set; }
        public override string ToString() { return Label; }
    }

    /// <summary>One line of the read-only Injected DLLs table: a loadable target, or a DLL with no valid entry point (flagged, never injected).</summary>
    public class InjectedDllRow
    {
        public string Dll { get; set; }
        public string EntryPoint { get; set; }
        public bool IsInvalid { get; set; }
        /// <summary>Loads fine, but something doesn't add up (for example the mod has settings and this DLL can't receive them).</summary>
        public bool IsWarning { get; set; }
        public bool IsValid { get { return !IsInvalid && !IsWarning; } }
        public string Tooltip { get; set; }
    }

    /// <summary>One line of the read-only Bundles table: the bundle found in the mod folder (or listed in mod.json but missing) and its size.</summary>
    public class BundleRow
    {
        public string Name { get; set; }
        public string Size { get; set; }
        /// <summary>Listed in mod.json but not in the mod folder: shown with an error mark, and dropped from mod.json on save.</summary>
        public bool IsMissing { get; set; }
        public bool IsPresent { get { return !IsMissing; } }
        public string Tooltip { get; set; }
    }

    public class BumpLevelOption
    {
        public string Value { get; set; }
        public string Label { get; set; }
        public override string ToString() { return Label; }
    }

    /// <summary>Edits the mod.json fields Steam Workshop publish reads, plus how visible the item should be.</summary>
    public class PublishDialogViewModel : ViewModelBase, IDisposable
    {
        private readonly IDialogService dialogs;
        private readonly string modJsonPath;
        private readonly string contentFolder;
        private readonly ModPublishMetadata metadata;
        private string title;
        private string version;
        private string previewImage;
        private string description;
        private string shortDescription;
        private VisibilityOption visibility;
        private readonly bool isUpdate;
        private readonly bool autoUpdateDescription;
        private readonly string workshopId;
        // What Steam's page says now (null: not asked or unreachable) and what we last sent it (null: never recorded); a difference means it was edited on Steam.
        private string steamDescription;
        private string sentDescription;
        private bool pullingDescription;
        private bool canPushVisibility;
        private bool revertingVisibility;
        // The item's current Steam description when this is an update and Steam could be asked; null otherwise.
        // Properties dialog: the field values as they were when it opened, so Save is only live once something differs.
        private readonly bool propertiesOnly;
        private string savedFields;
        private string savedDescription;
        // What description.bbcode held the last time we looked, so a watcher event only touches the box when the file really changed.
        private readonly string descriptionFileName;
        private string lastFileText;
        private FileSystemWatcher watcher;
        private Timer debounce;
        // Version proposal: what the dialog last proposed (so a policy change only replaces an untouched proposal), and the X.Y.Z split.
        private readonly bool proposesBump;
        private BumpLevelOption bumpLevel;
        private string proposedVersion;
        private readonly List<string> detectedBundles;
        private readonly List<string> missingBundles = new List<string>();
        private List<string> strayImages = new List<string>();
        // Where the last preview image came from (outside the mod), and how to store a new one; see BrowsePreviewAsync.
        private string lastArtFolder;
        private readonly Action<string> rememberArtFolder;

        public PublishDialogViewModel(IDialogService dialogs, LocalizedStrings l, ModInfo mod, ModPublishMetadata metadata, bool isUpdate, ModVisibility? currentVisibility = null, bool propertiesOnly = false, string steamDescription = null, VersionBumpPolicy bumpPolicy = null, string lastArtFolder = null, Action<string> rememberArtFolder = null, string descriptionExtension = null, bool statusExpanded = false, Action<bool> rememberStatusExpanded = null, bool changeNotesExpanded = false, Action<bool> rememberChangeNotesExpanded = null, bool autoUpdateDescription = true)
        {
            this.dialogs = dialogs;
            this.lastArtFolder = lastArtFolder;
            this.rememberArtFolder = rememberArtFolder;
            this.metadata = metadata;
            L = l;
            modJsonPath = mod.ModJsonPath;
            contentFolder = mod.ContentRoot ?? mod.Folder;
            CanEditVisibility = !propertiesOnly;
            this.mod = mod;
            ComputeStatus();
            statusOpen = statusExpanded;
            this.rememberStatusExpanded = rememberStatusExpanded;
            notesOpen = changeNotesExpanded;
            this.rememberChangeNotesExpanded = rememberChangeNotesExpanded;
            ShowChangeNotes = true;
            notesVersion = (metadata.Version ?? "").Trim();
            savedChangeNotes = ModChangelog.NotesText(contentFolder, notesVersion);
            changeNotes = savedChangeNotes;
            this.propertiesOnly = propertiesOnly;
            if (isUpdate)
            {
                this.steamDescription = steamDescription;
                try { sentDescription = PublishedState.Load(ModDetails.WorkshopId(mod))?.Description; }
                catch (Exception ex) { DW2ModLauncher.Core.Diagnostics.Logger.LogException("Read the recorded description", ex); }
            }
            this.isUpdate = isUpdate;
            this.autoUpdateDescription = autoUpdateDescription;
            WindowTitle = l[propertiesOnly ? "EditPropertiesTitle" : "PublishToWorkshop"] + " - " + (mod.DisplayName ?? mod.Id);
            // Shown as the Publish button's tooltip. Properties-only editing saves mod.json and never talks to Steam, so it has no note.
            Note = propertiesOnly ? null : l[isUpdate ? "PublishAboutToRunUpdate" : "PublishAboutToRunFirstTime"];
            WorkshopIdText = string.IsNullOrWhiteSpace(mod.WorkshopId) ? l["PublishWorkshopIdUnpublished"] : mod.WorkshopId.Trim();
            title = metadata.DisplayName;
            OriginalVersion = metadata.Version;
            // Updating a published item proposes the next version per the mod's launcher-side policy (default: patch); nothing is
            // written to mod.json until the author accepts.
            bumpPolicy = bumpPolicy ?? new VersionBumpPolicy();
            proposesBump = isUpdate;
            BumpLevels.Add(new BumpLevelOption { Value = "none", Label = l["VersionLevelNone"] });
            foreach (VersionBumpLevel level in new[] { VersionBumpLevel.Patch, VersionBumpLevel.Minor, VersionBumpLevel.Major })
                BumpLevels.Add(new BumpLevelOption { Value = ModVersion.LevelName(level), Label = l["VersionLevel" + level] });
            string wanted = ModVersion.IsNoBump(bumpPolicy.Level) ? "none" : ModVersion.LevelName(ModVersion.ParseLevel(bumpPolicy.Level));
            bumpLevel = BumpLevels.First(o => o.Value == wanted);
            proposedVersion = ProposeVersion();
            version = proposedVersion;
            LoadChangeNotesFor(version);
            AdjustVersionCommand = RelayCommand.WithParameter(AdjustVersion);
            IsSemver = ModVersion.TryParseSemver(version, out _, out _, out _);
            previewImage = metadata.PreviewImage;
            // The long description is always description.bbcode (seeded from the legacy mod.json fields if the file doesn't exist yet).
            // A mod that already names its own description file keeps it; otherwise the existing description file, or one with the preferred extension (default .bbcode).
            descriptionFileName = ModDescriptionFile.NameFor(contentFolder, metadata.DescriptionFile, descriptionExtension);
            description = ModDescriptionFile.Load(contentFolder, metadata, descriptionExtension);
            lastFileText = ModDescriptionFile.ReadFile(contentFolder, descriptionFileName);
            shortDescription = metadata.ShortDescription;
            // Bundles are detected from the mod folder (never typed); the list is written back to mod.json on save.
            detectedBundles = BundleSet.Detect(contentFolder);
            BundleRows = detectedBundles.Select(b => BundleRowFor(b, l)).ToList();
            // Safety net: bundles mod.json lists that are gone from the folder stay visible (flagged) until the author saves.
            foreach (string listed in (metadata.Bundles ?? new List<string>()).Select(b => (b ?? "").Trim()).Where(b => b.Length > 0))
            {
                if (detectedBundles.Contains(listed, StringComparer.OrdinalIgnoreCase) || BundleSet.TotalBytes(contentFolder, listed).HasValue) continue;
                missingBundles.Add(listed);
                BundleRows.Add(new BundleRow { Name = listed, Size = l["PublishBundlesMissing"], IsMissing = true, Tooltip = l["PublishBundlesMissingTooltip"] });
            }
            // Read-only: what the launcher will inject for this mod (inferred, or the dw2modlauncher.json override).
            InjectedDlls = BuildInjectedRows(mod, l);

            workshopId = long.TryParse(mod.WorkshopId, out long _) ? mod.WorkshopId.Trim() : null;
            // On a published item the visibility box acts at once, like the description upload: choosing a value sends it to Steam and nothing else.
            canPushVisibility = workshopId != null && (propertiesOnly ? currentVisibility.HasValue : isUpdate);
            VisibilityEnabled = !propertiesOnly || canPushVisibility;
            OpenSteamPageCommand = new RelayCommand(OpenSteamPage);
            MarkUpToDateCommand = new RelayCommand(MarkUpToDateAsync);
            SaveCommand = new RelayCommand(SaveAsync);
            PullSteamDescriptionCommand = new RelayCommand(PullSteamDescriptionAsync);
            PushSteamDescriptionCommand = new RelayCommand(PushSteamDescriptionAsync);
            BrowsePreviewCommand = new RelayCommand(BrowsePreviewAsync);
            CleanStrayImagesCommand = new RelayCommand(OfferStrayImageCleanupAsync);
            strayImages = StrayImages.Find(contentFolder, previewImage);
            EditDescriptionFileCommand = new RelayCommand(EditDescriptionFileAsync);
            StartWatchingDescriptionFile();
            savedFields = FieldsSnapshot();
            savedDescription = description;

            if (propertiesOnly)
            {
                // Visibility lives on Steam: show what it is when we could ask (changing it applies at once, see the Visibility setter).
                if (currentVisibility.HasValue)
                {
                    foreach (ModVisibility v in ModPublisherFactory.SupportedVisibilities())
                        Visibilities.Add(new VisibilityOption { Value = v, Label = l[LabelKey(v)] });
                    visibility = Visibilities.First(o => o.Value == currentVisibility);
                }
                else
                {
                    Visibilities.Add(new VisibilityOption { Value = null, Label = l[isUpdate ? "VisibilityUnknown" : "VisibilityNotPublished"] });
                    visibility = Visibilities[0];
                }
                return;
            }

            // "Unchanged" is only needed when Steam couldn't tell us what the item currently is.
            if (isUpdate && currentVisibility == null) Visibilities.Add(new VisibilityOption { Value = null, Label = l["VisibilityUnchanged"] });
            foreach (ModVisibility v in ModPublisherFactory.SupportedVisibilities())
                Visibilities.Add(new VisibilityOption { Value = v, Label = v == currentVisibility ? l.Format("VisibilityCurrent", ("Visibility", l[LabelKey(v)])) : l[LabelKey(v)] });
            // New items default to private; updates default to what the item already has on Steam (or unchanged if unknown).
            visibility = Visibilities.First(o => isUpdate ? o.Value == currentVisibility : o.Value == ModVisibility.Private);

        }

        public static string LabelKey(ModVisibility v)
        {
            switch (v)
            {
                case ModVisibility.Public: return "VisibilityPublic";
                case ModVisibility.FriendsOnly: return "VisibilityFriendsOnly";
                case ModVisibility.Unlisted: return "VisibilityUnlisted";
                default: return "VisibilityPrivate";
            }
        }

        public LocalizedStrings L { get; }

        /// <summary>The mod's change-since-publish status, shown at the top of the dialog: the short status, a sentence of detail and the changed files. Empty for a mod that was never published.</summary>
        /// <summary>"Up to date (version, when it was published)"; just "Up to date" when neither is known.</summary>
        private static string UpToDate(ModInfo mod, ModPublishMetadata metadata, LocalizedStrings l)
        {
            string detail = string.Join(l["ListSeparator"], new[] { (metadata.Version ?? "").Trim(), ModDetails.PublishedWhen(mod) }.Where(x => x.Length > 0));
            return detail.Length == 0 ? l["PublishStateUpToDateBare"] : l.Format("PublishStateUpToDate", ("When", detail));
        }
        private readonly ModInfo mod;
        private string publishStatusLine = "";
        private string publishStatusDetail = "";

        /// <summary>The folded header: just the state - "Modified: 3", "Up to date (1.0.13, 14/10/2026)", "Not yet published" or "Not tracked".</summary>
        public string PublishStatusLine { get { return publishStatusLine; } private set { if (Set(ref publishStatusLine, value)) Raise(nameof(HasPublishStatus)); } }

        private void ComputeStatus()
        {
            string statusShort = ModDetails.PublishStatusShort(mod, key => L[key]);
            PublishStatusLine = mod.PublishStatus == PublishStatus.Changed ? L.Format("PublishStateChangedCount", ("Count", mod.UnpublishedChanges.Count))
                : mod.PublishStatus == PublishStatus.Current ? UpToDate(mod, metadata, L)
                : mod.PublishStatus == PublishStatus.Unpublished ? L["PublishStateUnpublishedTitle"]
                : mod.PublishStatus == PublishStatus.Unknown ? L["PublishStateUntracked"]
                : statusShort;
            // Modified: just the file names (the header has the count; being listed is what "modified" means). Other states keep their one-sentence explanation.
            PublishStatusDetail = mod.PublishStatus == PublishStatus.Changed ? string.Join("\n", mod.UnpublishedChanges.Select(c => c.Substring(2)))
                : statusShort.Length == 0 ? "" : ModDetails.PublishStatusSentence(mod, key => L[key]);
            LastPublished = ModDetails.PublishedWhen(mod);
            Raise(nameof(ShowDescriptionDiffers)); Raise(nameof(ShowDescriptionEditedOnSteam)); Raise(nameof(DescriptionTooltip)); Raise(nameof(PublishTooltip));
            Raise(nameof(CanMarkUpToDate));
            Raise(nameof(PrimaryButtonText));
        }

        /// <summary>Whether the folder differs from the last publish, so "Mark Up to Date" has something to accept.</summary>
        public bool CanMarkUpToDate { get { return (mod.PublishStatus == PublishStatus.Changed || HasUnsavedEdits) && ModDetails.WorkshopId(mod) != null; } }

        /// <summary>The owner's override: the folder as it is now counts as what Steam has. Local records only; nothing is sent to Steam.</summary>
        private async Task MarkUpToDateAsync()
        {
            if (!CanMarkUpToDate) return;
            string name = mod.DisplayName ?? mod.Id ?? Path.GetFileName(mod.Folder);
            if (!await dialogs.ConfirmAsync(L.Format("ConfirmMarkUpToDate", ("Mod", name), ("Count", mod.UnpublishedChanges.Count)), L["MarkUpToDate"], L["MarkUpToDate"], L["Cancel"])) return;
            // What is typed in this dialog (change notes included) is written first, so the record covers the files as they now are.
            if (HasUnsavedEdits)
            {
                string error = Commit();
                if (error != null)
                {
                    await dialogs.ShowMessageAsync(error, "DW2 Mod Launcher");
                    return;
                }
                savedFields = FieldsSnapshot();
                savedDescription = description;
                RaiseModified();
            }
            try { PublishedState.MarkCurrent(ModDetails.WorkshopId(mod), contentFolder); }
            catch (Exception ex)
            {
                DW2ModLauncher.Core.Diagnostics.Logger.LogException("Mark " + name + " up to date", ex);
                await dialogs.ShowMessageAsync(L.Format("MarkUpToDateFailed", ("Problem", ex.Message)), "DW2 Mod Launcher");
                return;
            }
            RefreshStatus();
        }
        public RelayCommand MarkUpToDateCommand { get; private set; }

        /// <summary>Compares the folder with its record again, for after something changed what counts as published (the description pushed on its own).</summary>
        private void RefreshStatus()
        {
            try { PublishedState.Evaluate(mod); }
            catch (Exception ex) { DW2ModLauncher.Core.Diagnostics.Logger.LogException("Compare with the last publish", ex); return; }
            ComputeStatus();
        }
        /// <summary>The sentence under the folded status line, then every changed file.</summary>
        public string PublishStatusDetail { get { return publishStatusDetail; } private set { Set(ref publishStatusDetail, value); } }

        private string lastPublished = "";
        /// <summary>When the last publish happened (date, plus time if today); empty when the mod was never published.</summary>
        public string LastPublished { get { return lastPublished; } private set { Set(ref lastPublished, value); } }
        private bool statusOpen;
        private readonly Action<bool> rememberStatusExpanded;
        /// <summary>Whether the modified-files section is unfolded; the choice is remembered for the next time.</summary>
        public bool StatusExpanded { get { return statusOpen; } set { if (Set(ref statusOpen, value)) rememberStatusExpanded?.Invoke(value); } }

        private bool notesOpen;
        private readonly Action<bool> rememberChangeNotesExpanded;
        /// <summary>Whether the change-notes section is unfolded; the choice is remembered for the next time.</summary>
        public bool ChangeNotesExpanded { get { return notesOpen; } set { if (Set(ref notesOpen, value)) rememberChangeNotesExpanded?.Invoke(value); } }
        public bool ShowChangeNotes { get; }
        private string changeNotes;
        /// <summary>The text last taken from the Steam page: if it is saved unchanged, the file matches what Steam has and is not a change since the last publish.</summary>
        private string pulledDescription;
        private string savedChangeNotes;
        // The version whose section the box was loaded from, and is written back to (see ModChangelog.NotesText).
        private string notesVersion;

        /// <summary>
        /// Points the notes box at the section a publish as <paramref name="forVersion"/> sends: Unreleased, or that version's own section when
        /// the changelog's newest section is the version about to go out. Text the author already edited is left alone.
        /// </summary>
        private void LoadChangeNotesFor(string forVersion)
        {
            if (changeNotes != null && ChangeNotesEdited) return;
            notesVersion = (forVersion ?? "").Trim();
            savedChangeNotes = ModChangelog.NotesText(contentFolder, notesVersion);
            if (Set(ref changeNotes, savedChangeNotes)) { Raise(nameof(ChangeNotes)); Raise(nameof(ChangeNotesHeader)); }
        }
        /// <summary>The Unreleased lines of changelog.txt: what the update will send as its change note. Written back by <see cref="Commit"/>.</summary>
        public string ChangeNotes
        {
            get { return changeNotes; }
            set { if (Set(ref changeNotes, value)) { Raise(nameof(ChangeNotesHeader)); RaiseModified(); } }
        }
        public string ChangeNotesHeader
        {
            get { return L["PublishChangeNotes"] + ": " + (changeNotes ?? "").Split('\n').Count(line => line.Trim().Length > 0); }
        }
        public bool HasPublishStatus { get { return PublishStatusLine.Length > 0; } }
        /// <summary>The mod.json version before this dialog proposed anything, for rolling back a publish that did not happen.</summary>
        public string OriginalVersion { get; }
        public string WindowTitle { get; }
        public string Note { get; }
        /// <summary>"Publish to Workshop", or "Save" when the dialog only edits mod.json.</summary>
        public string PrimaryButtonText { get { return L[propertiesOnly ? "Save" : NothingToPublish ? "Close" : "PublishAction"]; } }
        /// <summary>False when only editing properties (nothing is being published, so no description-replacement offer). The visibility box has its own <see cref="VisibilityEnabled"/>.</summary>
        public bool CanEditVisibility { get; }
        /// <summary>Whether the visibility box can be changed: always in the publish dialog; in edit properties only for a published item whose visibility Steam told us.</summary>
        public bool VisibilityEnabled { get; }
        /// <summary>True when Steam's text is known and differs from the box; the dialog says so next to the checkbox.</summary>
        public bool ShowDescriptionDiffers { get { return isUpdate && !ShowDescriptionEditedOnSteam && DescriptionDiffersFromSteam; } }
        /// <summary>Steam's page no longer says what we last sent it: someone edited the description there. Shown as a red dot, ahead of the gold one.</summary>
        public bool ShowDescriptionEditedOnSteam { get { return isUpdate && steamDescription != null && sentDescription != null && !ModDescriptionFile.SameText(sentDescription, steamDescription); } }
        /// <summary>Steam's description as fetched when the dialog opened (or as last pushed or pulled here); null when unknown.</summary>
        public string SteamDescriptionText { get { return steamDescription; } }
        /// <summary>The description file's name inside the mod folder.</summary>
        public string DescriptionFileName { get { return descriptionFileName; } }
        /// <summary>The Description header's tooltip: the usual hint, or that the description was modified on Steam, or that it differs from what was last sent.</summary>
        public string DescriptionTooltip { get { return L[ShowDescriptionEditedOnSteam ? "PublishDescriptionEditedOnSteam" : ShowDescriptionDiffers ? "PublishDescriptionDiffers" : "PublishDescriptionHint"]; } }
        // Same tracking as every other file: the description is "different" when it is typed over what is saved, or when its file is among the changes since the last publish (a push records it).
        private bool DescriptionDiffersFromSteam
        {
            get
            {
                if (!ModDescriptionFile.SameText(description, savedDescription)) return true;
                string file = descriptionFileName.Replace('\\', '/');
                return mod.UnpublishedChanges.Any(c => string.Equals(c.Substring(2), file, StringComparison.OrdinalIgnoreCase));
            }
        }
        // A publish also refreshes the {version}/{date} stamps, so a description that has them goes out every time.
        private bool PublishWouldChangeSteamDescription { get { return DescriptionDiffersFromSteam || DescriptionTokens.HasTokens(description); } }
        /// <summary>
        /// Whether this publish overwrites the Steam page's description: always for a first publish; for an update only when the launcher setting
        /// "update the description automatically" is on and the text differs from Steam's.
        /// </summary>
        public bool ReplaceDescription { get { return !isUpdate || (autoUpdateDescription && PublishWouldChangeSteamDescription); } }

        /// <summary>The Publish button's tooltip: what is about to happen, and plainly whether the description goes to Steam (and if not, why).</summary>
        public string PublishTooltip
        {
            get
            {
                if (Note == null) return null;
                string line = ReplaceDescription ? L["PublishNoteDescriptionSent"]
                    : !autoUpdateDescription ? L["PublishNoteDescriptionOff"]
                    : L["PublishNoteDescriptionSame"];
                return Note + "\n\n" + line;
            }
        }
        /// <summary>The item's Workshop id (read-only), or "Unpublished" until a first publish has written one into mod.json.</summary>
        public string WorkshopIdText { get; }
        /// <summary>The DLLs (and entry points) the launcher will inject for this mod, shown as a read-only table.</summary>
        public List<InjectedDllRow> InjectedDlls { get; }

        private static List<InjectedDllRow> BuildInjectedRows(ModInfo mod, LocalizedStrings l)
        {
            try { return ScanInjectedRows(mod, l); }
            catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException)
            {
                // An unreadable mod folder or DLL must not look like "this mod injects nothing".
                DW2ModLauncher.Core.Diagnostics.Logger.LogException("Scan injected DLLs: " + mod.Folder, ex);
                return new List<InjectedDllRow> { new InjectedDllRow { Dll = mod.ContentRoot ?? mod.Folder, EntryPoint = l["PublishInjectedDllsInvalid"], IsInvalid = true, Tooltip = ex.Message } };
            }
        }

        private static List<InjectedDllRow> ScanInjectedRows(ModInfo mod, LocalizedStrings l)
        {
            List<InjectedDllRow> rows = DW2ModLauncher.Core.Services.InjectionScanner.TargetsFor(mod)
                .Select(t => new InjectedDllRow { Dll = t.Dll, EntryPoint = t.EntryPoint }).ToList();
            // Incongruous: the mod ships a settings schema, yet none of its DLLs can receive settings (they only have Init()).
            string root = mod.ContentRoot ?? mod.Folder;
            bool hasSchema;
            try { hasSchema = DW2ModLauncher.Core.Services.ModSettingsSchemaReader.Read(root) != null; }
            catch (System.IO.InvalidDataException ex)
            {
                // A schema that exists but is broken must not look like "no schema": flag every DLL row with the reason.
                foreach (InjectedDllRow row in rows)
                {
                    row.IsWarning = true;
                    row.Tooltip = ex.Message;
                }
                return rows;
            }
            if (rows.Count > 0 && hasSchema
                && !rows.Any(r => DW2ModLauncher.Core.Services.InjectionScanner.AcceptsOptions(System.IO.Path.Combine(root, r.Dll.Replace('/', System.IO.Path.DirectorySeparatorChar)))))
            {
                foreach (InjectedDllRow row in rows)
                {
                    row.IsWarning = true;
                    row.Tooltip = l["PublishInjectedDllsWarnNoOptions"];
                }
            }
            // DLLs with an unusable Entry type are listed too, flagged; they are display-only and never reach the loader manifest.
            foreach (DW2ModLauncher.Core.Services.InvalidInjectionDll bad in DW2ModLauncher.Core.Services.InjectionScanner.InvalidFor(mod))
                rows.Add(new InjectedDllRow { Dll = bad.Dll, EntryPoint = l["PublishInjectedDllsInvalid"], IsInvalid = true, Tooltip = l["PublishInjectedDllsProblem" + bad.Problem] });
            return rows.OrderBy(r => r.Dll, StringComparer.Ordinal).ToList();
        }
        /// <summary>Whether the mod lists any bundles; the Bundles section starts collapsed when it does not.</summary>
        public bool HasBundles { get { return BundleRows.Count > 0; } }
        public bool NoBundles { get { return BundleRows.Count == 0; } }
        /// <summary>The bundles found in the mod folder with their total size in MiB (head file plus its hashed part files).</summary>
        public List<BundleRow> BundleRows { get; }

        /// <summary>Null when saving loses nothing; otherwise the OK/Cancel question about the missing bundles that saving removes from mod.json.</summary>
        public string MissingBundlesQuestion { get { return missingBundles.Count == 0 ? null : L.Format("PublishBundlesRemoveConfirm", ("Bundles", string.Join("\n", missingBundles))); } }

        private BundleRow BundleRowFor(string name, LocalizedStrings l)
        {
            long? bytes = BundleSet.TotalBytes(contentFolder, name);
            string size = bytes.HasValue ? DW2ModLauncher.Core.Services.ByteSize.Format(bytes.Value) : l["PublishBundlesMissing"];
            return new BundleRow { Name = name, Size = size };
        }
        public bool HasInjectedDlls { get { return InjectedDlls.Count > 0; } }
        public bool NoInjectedDlls { get { return InjectedDlls.Count == 0; } }
        public List<VisibilityOption> Visibilities { get; } = new List<VisibilityOption>();
        /// <summary>The Steam page and "pull description" buttons only make sense once the item has a Workshop id.</summary>
        public bool HasWorkshopId { get { return workshopId != null; } }
        public RelayCommand OpenSteamPageCommand { get; }
        public RelayCommand PullSteamDescriptionCommand { get; }
        public RelayCommand PushSteamDescriptionCommand { get; }
        public RelayCommand BrowsePreviewCommand { get; }
        public RelayCommand EditDescriptionFileCommand { get; }

        private string FieldsSnapshot()
        {
            return string.Join("", new[] { title, version, previewImage, shortDescription, bumpLevel.Value }.Select(x => (x ?? "").Trim()));
        }

        /// <summary>Something in the dialog differs from what it opened with.</summary>
        public bool IsModified { get { return FieldsSnapshot() != savedFields || !ModDescriptionFile.SameText(description, savedDescription) || HasStrayImages || MissingDescriptionFileKey || ChangeNotesEdited; } }
        /// <summary>The change notes box differs from the Unreleased lines on disk (erasing them all counts).</summary>
        private bool ChangeNotesEdited { get { return ShowChangeNotes && (changeNotes ?? "").Trim() != savedChangeNotes; } }

        /// <summary>
        /// mod.json has no "descriptionFile" yet the mod has (or, once saved, will have) a description file: saving writes the key, so the
        /// dialog counts it as a pending change. This is what brings older mods up to the explicit contract.
        /// </summary>
        private bool MissingDescriptionFileKey
        {
            get
            {
                return string.IsNullOrWhiteSpace(metadata.DescriptionFile)
                    && (!string.IsNullOrWhiteSpace(description) || ModDescriptionFile.ReadFile(contentFolder, descriptionFileName) != null);
            }
        }
        /// <summary>The primary button: publishing is always allowed; the Properties "Save" only once something changed.</summary>
        /// <summary>The publish dialog has nothing to send: the mod is recorded as up to date and nothing here was edited, so its one button reads Close.</summary>
        public bool NothingToPublish { get { return !propertiesOnly && mod.PublishStatus == PublishStatus.Current && !HasUnsavedEdits; } }
        /// <summary>Edit Properties keeps a separate Cancel/Close next to Save; the publish dialog has just its one button (Esc or the window's X leave).</summary>
        public bool ShowCancel { get { return propertiesOnly; } }
        public bool CanPrimary { get { return !propertiesOnly || IsModified; } }
        /// <summary>Something typed or chosen in this dialog has not been saved: leaving now would throw it away.</summary>
        private bool HasUnsavedEdits
        {
            get
            {
                return FieldsSnapshot() != savedFields || !ModDescriptionFile.SameText(description, savedDescription)
                    || ChangeNotesEdited;
            }
        }
        /// <summary>The dialog's way out: "Cancel" while edits would be discarded, "Close" when what is shown is what is on disk.</summary>
        public string CancelText { get { return L[HasUnsavedEdits ? "Cancel" : "Close"]; } }
        /// <summary>The publish dialog's Save button (writing mod.json, description and change notes without publishing); the Properties dialog's primary button is its Save.</summary>
        public bool ShowSave { get { return !propertiesOnly; } }
        public bool CanSave { get { return HasUnsavedEdits || MissingDescriptionFileKey; } }
        public RelayCommand SaveCommand { get; private set; }

        /// <summary>Writes what is typed here to disk and sends nothing to Steam.</summary>
        private async Task SaveAsync()
        {
            if (!CanSave) return;
            string question = MissingBundlesQuestion;
            if (question != null && !await dialogs.ConfirmAsync(question, "DW2 Mod Launcher", L["OK"], L["Cancel"])) return;
            string error = Commit();
            if (error != null)
            {
                await dialogs.ShowMessageAsync(error, "DW2 Mod Launcher");
                return;
            }
            // Commit dropped the missing bundles from mod.json, so they are not asked about again.
            missingBundles.Clear();
            savedFields = FieldsSnapshot();
            savedDescription = description;
            RaiseModified();
            RefreshStatus();
        }

        private void RaiseModified() { Raise(nameof(CanSave)); Raise(nameof(IsModified)); Raise(nameof(CanMarkUpToDate)); Raise(nameof(CanPrimary)); Raise(nameof(PrimaryButtonText)); Raise(nameof(CancelText)); }

        public string Title { get { return title; } set { if (Set(ref title, value)) RaiseModified(); } }
        public string Version
        {
            get { return version; }
            set
            {
                if (!Set(ref version, value)) return;
                LoadChangeNotesFor(value);
                UpdateIsSemver();
                RaiseModified();
            }
        }

        /// <summary>The version is plain X.Y.Z, so the Major/Minor/Patch +/- buttons are live; any other shape (1.2, 1.2.3-beta) is edited as text only.</summary>
        public bool IsSemver { get; private set; }

        /// <summary>Parameter is "+" or "-" followed by "major", "minor" or "patch": changes that one part only (never below 0).</summary>
        private void AdjustVersion(object how)
        {
            string text = how as string ?? "";
            if (text.Length < 2 || !ModVersion.TryParseSemver(version, out int major, out int minor, out int patch)) return;
            int delta = text[0] == '-' ? -1 : 1;
            switch (text.Substring(1))
            {
                case "major": major = Math.Max(0, major + delta); break;
                case "minor": minor = Math.Max(0, minor + delta); break;
                case "patch": patch = Math.Max(0, patch + delta); break;
            }
            Version = major + "." + minor + "." + patch;
        }

        public RelayCommand AdjustVersionCommand { get; }

        /// <summary>The choices for how the next version is proposed; shown only when publishing.</summary>
        public List<BumpLevelOption> BumpLevels { get; } = new List<BumpLevelOption>();
        public BumpLevelOption BumpLevel
        {
            get { return bumpLevel; }
            set { if (value != null && Set(ref bumpLevel, value)) { Repropose(); RaiseModified(); } }
        }
        /// <summary>The policy as the author left it, for the caller to store per mod in launcher_settings.json.</summary>
        public VersionBumpPolicy BumpPolicy { get { return new VersionBumpPolicy { Level = bumpLevel.Value }; } }

        private void UpdateIsSemver()
        {
            bool semver = ModVersion.TryParseSemver(version, out _, out _, out _);
            if (IsSemver == semver) return;
            IsSemver = semver;
            Raise(nameof(IsSemver));
        }

        /// <summary>
        /// The next version, per the mod's bump policy, when the version in mod.json already went out (the last publish's record holds it, or its
        /// changelog section is stamped); a version that was never published is the one being worked on and is proposed as it is. Both dialogs propose the same.
        /// </summary>
        private string ProposeVersion()
        {
            bool bump = proposesBump && !ModVersion.IsNoBump(bumpLevel.Value) && VersionWentOut();
            return bump ? ModVersion.Bump(OriginalVersion, ModVersion.ParseLevel(bumpLevel.Value)) : OriginalVersion;
        }

        private bool VersionWentOut()
        {
            if (ModChangelog.IsStamped(contentFolder, OriginalVersion)) return true;
            try { return PublishedState.VersionWentOut(ModDetails.WorkshopId(mod), OriginalVersion); }
            catch (Exception ex)
            {
                DW2ModLauncher.Core.Diagnostics.Logger.LogException("Read the recorded version", ex);
                return false;
            }
        }

        /// <summary>A policy change replaces the proposal, unless the author already typed their own version.</summary>
        private void Repropose()
        {
            string next = ProposeVersion();
            if (version == proposedVersion) Version = next;
            proposedVersion = next;
        }
        public string PreviewImage { get { return previewImage; } set { if (Set(ref previewImage, value)) { Raise(nameof(PreviewImageSize)); RefreshStrayImages(); RaiseModified(); } } }

        /// <summary>Loose images in the mod folder other than the preview: unused by the mod, yet uploaded with it.</summary>
        public bool HasStrayImages { get { return strayImages.Count > 0; } }
        public string StrayImagesWarning { get { return L.Format("PublishStrayImagesWarning", ("Count", strayImages.Count)); } }
        public string StrayImagesTooltip { get { return string.Join("\n", strayImages); } }
        public RelayCommand CleanStrayImagesCommand { get; }

        private void RefreshStrayImages()
        {
            strayImages = StrayImages.Find(contentFolder, previewImage);
            Raise(nameof(HasStrayImages));
            Raise(nameof(StrayImagesWarning));
            Raise(nameof(StrayImagesTooltip));
        }

        /// <summary>
        /// Offers to move the stray images to the recycle bin. Declining (or a failure) changes nothing and is not an error: the author
        /// has the last say, so saving or publishing carries on either way. Returns whether anything was removed.
        /// </summary>
        public async Task<bool> OfferStrayImageCleanupAsync()
        {
            RefreshStrayImages();
            if (strayImages.Count == 0) return false;
            if (!await dialogs.ConfirmAsync(L.Format("PublishStrayImagesQuestion", ("Files", string.Join("\n", strayImages))), "DW2 Mod Launcher", L["Yes"], L["No"])) return false;
            try
            {
                foreach (string name in strayImages) DW2ModLauncher.Core.Services.RecycleBin.Send(System.IO.Path.Combine(contentFolder, name));
            }
            catch (Exception ex)
            {
                DW2ModLauncher.Core.Diagnostics.Logger.LogException("Clean stray images", ex);
                await dialogs.ShowMessageAsync(ex.Message, "DW2 Mod Launcher");
            }
            RefreshStrayImages();
            RaiseModified();
            return true;
        }
        /// <summary>The preview file's size once the box names a file that exists (Steam rejects previews over 1 MiB); blank otherwise.</summary>
        public string PreviewImageSize
        {
            get
            {
                try
                {
                    string name = (previewImage ?? "").Trim();
                    FileInfo file = name.Length == 0 ? null : new FileInfo(System.IO.Path.Combine(contentFolder, name));
                    if (file != null && file.Exists) return DW2ModLauncher.Core.Services.ByteSize.Format(file.Length);
                    if (file != null) return L["PublishPreviewMissing"];
                }
                catch (Exception) { }
                return "";
            }
        }
        public string Description
        {
            get { return description; }
            set
            {
                if (!Set(ref description, value)) return;
                RaiseModified();
                Raise(nameof(PublishTooltip));
                Raise(nameof(ShowDescriptionDiffers));
                Raise(nameof(DescriptionTooltip));
            }
        }
        public string ShortDescription { get { return shortDescription; } set { if (Set(ref shortDescription, value)) RaiseModified(); } }
        public VisibilityOption Visibility
        {
            get { return visibility; }
            set
            {
                if (value == null || value == visibility) return;
                VisibilityOption previous = visibility;
                Set(ref visibility, value);
                if (!revertingVisibility && canPushVisibility && value.Value.HasValue) { var _ = PushVisibilityAsync(previous); }
            }
        }

        public ModPublishMetadata Metadata { get { return metadata; } }
        public ModVisibility? SelectedVisibility { get { return visibility.Value; } }

        /// <summary>Opens the item in the Steam client, falling back to the default browser if the steam:// link can't be handled.</summary>
        private void OpenSteamPage()
        {
            if (workshopId == null) return;
            try { DW2ModLauncher.Core.Services.PlatformShell.Create().OpenUrl("steam://url/CommunityFilePage/" + workshopId); }
            catch
            {
                try { DW2ModLauncher.Core.Services.PlatformShell.Create().OpenUrl("https://steamcommunity.com/sharedfiles/filedetails/?id=" + workshopId); }
                catch (Exception ex) { var _ = dialogs.ShowMessageAsync(ex.Message, "DW2 Mod Launcher"); }
            }
        }

        /// <summary>Replaces the typed description with the one currently on the item's Steam page (switching to typed text if a file was in use).</summary>
        private async Task PullSteamDescriptionAsync()
        {
            if (workshopId == null || pullingDescription) return;
            pullingDescription = true;
            try
            {
                WorkshopRemoteDetail detail = await Task.Run(() =>
                {
                    DW2ModLauncher.Core.Services.WorkshopApiClient.FetchRemoteTimes(new List<string> { workshopId }, out Dictionary<string, WorkshopRemoteDetail> details);
                    return details.TryGetValue(workshopId, out WorkshopRemoteDetail d) ? d : null;
                });
                if (detail == null)
                {
                    await dialogs.ShowMessageAsync(L["PublishPullSteamDescriptionNotFound"], "DW2 Mod Launcher");
                    return;
                }
                // Steam's text replaces the box: ask first when that throws away a local description Steam does not have.
                if (DescriptionDiffersFromSteam && !ModDescriptionFile.SameText(description, detail.Description)
                    && !await dialogs.ConfirmAsync(L["PublishPullSteamDescriptionConfirm"], "DW2 Mod Launcher", L["Yes"], L["No"])) return;
                pulledDescription = detail.Description ?? "";
                // Taking Steam's text accepts any edit made there.
                steamDescription = sentDescription = pulledDescription;
                Description = detail.Description ?? "";
                ComputeStatus();
            }
            catch (Exception ex)
            {
                DW2ModLauncher.Core.Diagnostics.Logger.LogException("Pull Steam description", ex);
                await dialogs.ShowMessageAsync(L.Format("PublishPullSteamDescriptionFailed", ("Problem", ex.Message)), "DW2 Mod Launcher");
            }
            finally { pullingDescription = false; }
        }

        /// <summary>What {changes} lists: the change notes as typed in this dialog, else what changelog.txt holds (see <see cref="ModChangelog.LatestChanges"/>).</summary>
        private List<string> LatestChangesForDescription()
        {
            List<string> typed = ModChangelog.Entries(changeNotes);
            return typed.Count > 0 ? typed : ModChangelog.LatestChanges(contentFolder, (version ?? "").Trim());
        }

        /// <summary>Sends the description box to the item's Steam page and nothing else (no content, title, preview or visibility).</summary>
        private async Task PushSteamDescriptionAsync()
        {
            if (workshopId == null || pullingDescription) return;
            if (string.IsNullOrWhiteSpace(description))
            {
                await dialogs.ShowMessageAsync(L["PublishPushSteamDescriptionBlank"], "DW2 Mod Launcher");
                return;
            }
            if (!await dialogs.ConfirmAsync(L[ShowDescriptionEditedOnSteam ? "PublishPushSteamDescriptionConfirmEdited" : "PublishPushSteamDescriptionConfirm"], "DW2 Mod Launcher", L["Yes"], L["No"])) return;
            pullingDescription = true;
            try
            {
                // Make the backing file match what is being uploaded, so the file and the Steam page never disagree (written first, like Save).
                ModDescriptionFile.Save(contentFolder, descriptionFileName, description);
                lastFileText = ModDescriptionFile.ReadFile(contentFolder, descriptionFileName);
                savedDescription = description;
                RaiseModified();
                ModPublishRequest request = new ModPublishRequest
                {
                    // the placeholders ({version}, {date}) are filled in on the way to Steam, as in a full publish
                    Description = DescriptionTokens.Expand(description, (version ?? "").Trim(), DateTime.Now, LatestChangesForDescription()),
                    DescriptionOnly = true,
                    ExistingWorkshopId = long.Parse(workshopId)
                };
                CancellationTokenSource cancel = new CancellationTokenSource();
                request.Cancel = cancel.Token;
                ModPublishResult result;
                using (dialogs.ShowBusy(L["PublishPushSteamDescriptionRunning"], "DW2 Mod Launcher", L["Cancel"], TimeSpan.FromSeconds(15), cancel))
                    result = await Task.Run(() => ModPublisherFactory.Create(uint.Parse(DW2ModLauncher.Core.Services.SteamLocator.AppId)).Publish(request));
                if (cancel.IsCancellationRequested) return;
                string recordProblem = null;
                if (string.IsNullOrEmpty(result.ErrorMessage))
                {
                    // The page now matches the file, so the file is no longer a change since the last publish.
                    steamDescription = sentDescription = request.Description;
                    try { PublishedState.RecordFile(workshopId, contentFolder, descriptionFileName, sentDescription: request.Description); RefreshStatus(); }
                    catch (Exception ex)
                    {
                        DW2ModLauncher.Core.Diagnostics.Logger.LogException("Record pushed description", ex);
                        recordProblem = L.Format("PublishStateRecordFailed", ("Problem", ex.Message));
                    }
                }
                await dialogs.ShowMessageAsync(string.IsNullOrEmpty(result.ErrorMessage)
                    ? L["PublishPushSteamDescriptionDone"] + (recordProblem == null ? "" : "\n\n" + recordProblem)
                    : L.Format("PublishPushSteamDescriptionFailed", ("Problem", result.ErrorMessage)), "DW2 Mod Launcher");
            }
            catch (Exception ex)
            {
                DW2ModLauncher.Core.Diagnostics.Logger.LogException("Push Steam description", ex);
                await dialogs.ShowMessageAsync(L.Format("PublishPushSteamDescriptionFailed", ("Problem", ex.Message)), "DW2 Mod Launcher");
            }
            finally { pullingDescription = false; }
        }

        /// <summary>Sends the chosen visibility to the item's Steam page and nothing else (no content, title, preview or description). Puts the box back if Steam does not take it.</summary>
        private async Task PushVisibilityAsync(VisibilityOption previous)
        {
            ModVisibility chosen = visibility.Value.Value;
            string problem = null;
            try
            {
                ModPublishRequest request = new ModPublishRequest
                {
                    VisibilityOnly = true,
                    Visibility = chosen,
                    ExistingWorkshopId = long.Parse(workshopId)
                };
                CancellationTokenSource cancel = new CancellationTokenSource();
                request.Cancel = cancel.Token;
                ModPublishResult result;
                using (dialogs.ShowBusy(L["PublishPushVisibilityRunning"], "DW2 Mod Launcher", L["Cancel"], TimeSpan.FromSeconds(15), cancel))
                    result = await Task.Run(() => ModPublisherFactory.Create(uint.Parse(DW2ModLauncher.Core.Services.SteamLocator.AppId)).Publish(request));
                if (cancel.IsCancellationRequested) problem = "";
                else if (!string.IsNullOrEmpty(result.ErrorMessage)) problem = result.ErrorMessage;
            }
            catch (Exception ex)
            {
                DW2ModLauncher.Core.Diagnostics.Logger.LogException("Push Steam visibility", ex);
                problem = ex.Message;
            }
            if (problem == null) return;
            revertingVisibility = true;
            Visibility = previous;
            revertingVisibility = false;
            if (problem.Length > 0) await dialogs.ShowMessageAsync(L.Format("PublishPushVisibilityFailed", ("Problem", problem)), "DW2 Mod Launcher");
        }

        private async Task BrowsePreviewAsync()
        {
            // Always opens where art was last picked from, whether or not the field already names a file: the button is for changing the
            // art, and the current art's own folder (the mod's) is not where new art comes from. Until something has been picked from outside
            // the mod it opens in the Downloads folder (the mod folder if the system has none).
            string start = !string.IsNullOrWhiteSpace(lastArtFolder) && Directory.Exists(lastArtFolder)
                ? lastArtFolder
                : await dialogs.DownloadsFolderAsync() ?? contentFolder;
            string picked = await dialogs.PickFileAsync(L["PublishInfoPreviewImage"], start, L["FilterImageFiles"], "*.jpg", "*.jpeg", "*.png");
            if (picked == null) return;
            if (!DW2ModLauncher.Core.Services.ModFileImporter.IsInside(contentFolder, picked))
            {
                lastArtFolder = System.IO.Path.GetDirectoryName(picked);
                rememberArtFolder?.Invoke(lastArtFolder);
            }
            string local = await MakeLocalPreviewAsync(picked);
            if (local != null) PreviewImage = DW2ModLauncher.Core.Services.ModFileImporter.RelativePath(contentFolder, local);
        }

        /// <summary>
        /// mod.json can only point at files inside the mod, and Steam rejects previews over 1 MiB. Turns the picked file into a conformant
        /// one in the mod folder and returns its full path, or null when the author cancelled (nothing is written then) or saving failed.
        /// A file already inside the mod and small enough is used as it is. Otherwise the author names the file in the mod folder (the
        /// picked file's name is offered, selected, so Enter accepts it) and gets either a plain copy or, for a too-big image, a smaller
        /// copy. The picked file is never changed.
        /// </summary>
        private async Task<string> MakeLocalPreviewAsync(string picked)
        {
            string fileName = System.IO.Path.GetFileName(picked);
            long bytes;
            try { bytes = new FileInfo(picked).Length; }
            catch (Exception) { bytes = 0; }
            bool inside = DW2ModLauncher.Core.Services.ModFileImporter.IsInside(contentFolder, picked);

            FittedPreview fitted = null;
            if (PreviewImagePlan.NeedsResize(bytes))
            {
                fitted = await Task.Run(() => PreviewImageResizer.TryFit(picked));
                if (fitted == null)
                    await dialogs.ShowMessageAsync(L.Format("PublishPreviewResizeFailed", ("File", fileName)), "DW2 Mod Launcher");
            }

            try
            {
                if (fitted != null && inside)
                {
                    // Already in the mod: replace it in place (original to the recycle bin) so no oversized copy is left to be uploaded.
                    bool replace = await dialogs.ConfirmAsync(
                        L.Format("PublishPreviewReplaceLabel", ("File", fileName), ("Size", DW2ModLauncher.Core.Services.ByteSize.Format(bytes)), ("Width", fitted.Width), ("Height", fitted.Height), ("NewSize", DW2ModLauncher.Core.Services.ByteSize.Format(fitted.Data.LongLength))),
                        "DW2 Mod Launcher", L["Yes"], L["Cancel"]);
                    return replace ? PreviewImageResizer.Replace(fitted, picked) : null;
                }
                if (fitted != null)
                {
                    string name = await PromptFileNameAsync(
                        L.Format("PublishPreviewResizeNameLabel", ("File", fileName), ("Size", DW2ModLauncher.Core.Services.ByteSize.Format(bytes)), ("Width", fitted.Width), ("Height", fitted.Height), ("NewSize", DW2ModLauncher.Core.Services.ByteSize.Format(fitted.Data.LongLength))),
                        CoverFileName(fitted.Extension), fitted.Extension);
                    return name == null ? null : PreviewImageResizer.Save(fitted, contentFolder, name);
                }

                // No (usable) resize: the file goes in as it is.
                if (inside) return picked;
                string identical = DW2ModLauncher.Core.Services.ModFileImporter.FindIdentical(contentFolder, picked);
                if (identical != null) return identical;
                string extension = System.IO.Path.GetExtension(picked);
                string copyName = await PromptFileNameAsync(L.Format("PublishImportFileNameLabel", ("File", fileName)),
                    CoverFileName(extension), extension);
                if (copyName == null) return null;
                string target = System.IO.Path.Combine(contentFolder, copyName);
                File.Copy(picked, target, overwrite: true);
                return target;
            }
            catch (Exception ex)
            {
                DW2ModLauncher.Core.Diagnostics.Logger.LogException("Save preview image into mod", ex);
                await dialogs.ShowMessageAsync(ex.Message, "DW2 Mod Launcher");
                return null;
            }
        }

        /// <summary>
        /// The name offered for a preview image brought in from outside the mod: the mod's own name (it is the mod's cover art). If that
        /// file already exists it is offered anyway, since replacing the old cover art is the usual intent.
        /// </summary>
        private string CoverFileName(string extension)
        {
            string stem = DW2ModLauncher.Core.Services.LocalModManager.FolderNameFor(string.IsNullOrWhiteSpace(title) ? System.IO.Path.GetFileName(contentFolder.TrimEnd('/', (char)92)) : title);
            return stem + extension;
        }

        /// <summary>
        /// Asks what to call a file about to be saved in the mod folder. The extension is fixed (a typed image extension is replaced), and
        /// a name that is already taken is allowed (the dialog warns that the file will be replaced). Returns the file name, or null on Cancel.
        /// </summary>
        private async Task<string> PromptFileNameAsync(string label, string defaultName, string extension)
        {
            string typed = await dialogs.PromptTextAsync("DW2 Mod Launcher", label, defaultName, L["OK"], L["Cancel"],
                text =>
                {
                    if (string.IsNullOrWhiteSpace(text)) return "";
                    string name = PreviewImagePlan.FileNameFor(text, extension);
                    if (name == null) return L["PublishPreviewResizeNameBad"];
                    return File.Exists(System.IO.Path.Combine(contentFolder, name)) ? L.Format("PublishPreviewResizeNameTaken", ("File", name)) : L.Format("PublishPreviewResizeNameHint", ("File", name));
                },
                isInvalid: text => PreviewImagePlan.FileNameFor(text, extension) == null);
            return typed == null ? null : PreviewImagePlan.FileNameFor(typed, extension);
        }

        /// <summary>Opens description.bbcode in the OS's associated editor, first creating it from the box if it doesn't exist yet.</summary>
        private async Task EditDescriptionFileAsync()
        {
            try
            {
                if (ModDescriptionFile.ReadFile(contentFolder, descriptionFileName) == null)
                {
                    ModDescriptionFile.Save(contentFolder, descriptionFileName, description);
                    lastFileText = ModDescriptionFile.ReadFile(contentFolder, descriptionFileName);
                    savedDescription = description;
                    RaiseModified();
                }
                DW2ModLauncher.Core.Services.PlatformShell.Create().OpenFile(ModDescriptionFile.PathFor(contentFolder, descriptionFileName));
            }
            catch (Exception ex) { await dialogs.ShowMessageAsync(ex.Message, "DW2 Mod Launcher"); }
        }

        /// <summary>Watches description.bbcode so edits made in an external editor show up in the box.</summary>
        private void StartWatchingDescriptionFile()
        {
            if (!Directory.Exists(System.IO.Path.GetDirectoryName(ModDescriptionFile.PathFor(contentFolder, descriptionFileName)))) return;
            try
            {
                debounce = new Timer(delegate { global::Avalonia.Threading.Dispatcher.UIThread.Post(ReloadDescriptionFromFile); });
                watcher = new FileSystemWatcher(System.IO.Path.GetDirectoryName(ModDescriptionFile.PathFor(contentFolder, descriptionFileName)), System.IO.Path.GetFileName(descriptionFileName))
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.CreationTime
                };
                FileSystemEventHandler changed = delegate { debounce?.Change(300, Timeout.Infinite); };
                watcher.Changed += changed;
                watcher.Created += changed;
                watcher.Renamed += delegate { debounce?.Change(300, Timeout.Infinite); };
                watcher.EnableRaisingEvents = true;
            }
            catch (Exception ex) { DW2ModLauncher.Core.Diagnostics.Logger.LogException("Watch description.bbcode", ex); }
        }

        private void ReloadDescriptionFromFile()
        {
            string text = ModDescriptionFile.ReadFile(contentFolder, descriptionFileName);
            if (text == null || text == lastFileText) return;
            lastFileText = text;
            // The file already holds this text, so it is not an unsaved change.
            savedDescription = text;
            Description = text;
            RaiseModified();
        }

        public void Dispose()
        {
            watcher?.Dispose();
            debounce?.Dispose();
            watcher = null;
            debounce = null;
        }

        /// <summary>Writes the edited fields back to mod.json . Returns an error message, or null on success.</summary>
        public string Commit()
        {
            metadata.DisplayName = (title ?? "").Trim();
            metadata.Version = (version ?? "").Trim();
            metadata.PreviewImage = (previewImage ?? "").Trim();
            metadata.ShortDescription = (shortDescription ?? "").Trim();
            // The long description goes to description.bbcode first; only then does Write drop the legacy mod.json keys, so a failed
            // file write can't lose the text. An untouched, never-written description (nothing in the box, no file) creates no file.
            metadata.Description = "";
            try
            {
                if (!string.IsNullOrWhiteSpace(description) || ModDescriptionFile.ReadFile(contentFolder, descriptionFileName) != null)
                    ModDescriptionFile.Save(contentFolder, descriptionFileName, description);
                // Named in mod.json whenever the file exists (the default name too); no file, no key.
                metadata.DescriptionFile = ModDescriptionFile.ReadFile(contentFolder, descriptionFileName) != null ? descriptionFileName : "";
                if (pulledDescription != null && workshopId != null && ModDescriptionFile.SameText(description, pulledDescription))
                    PublishedState.RecordFile(workshopId, contentFolder, descriptionFileName, sentDescription: pulledDescription);
            }
            catch (Exception ex)
            {
                DW2ModLauncher.Core.Diagnostics.Logger.LogException("Write description.bbcode", ex);
                return ex.Message;
            }
            // The change notes go to the Unreleased section of changelog.txt; the file is only touched when the text changed.
            if (ChangeNotesEdited)
            {
                try { ModChangelog.SetNotes(contentFolder, notesVersion, (changeNotes ?? "").Trim()); savedChangeNotes = (changeNotes ?? "").Trim(); }
                catch (Exception ex)
                {
                    DW2ModLauncher.Core.Diagnostics.Logger.LogException("Write changelog.txt", ex);
                    return ex.Message;
                }
            }
            metadata.Bundles = detectedBundles;
            try
            {
                ModPublishMetadataEditor.Write(modJsonPath, metadata);
                return null;
            }
            catch (Exception ex)
            {
                DW2ModLauncher.Core.Diagnostics.Logger.LogException("Write mod.json publish info", ex);
                return ex.Message;
            }
        }
    }
}
