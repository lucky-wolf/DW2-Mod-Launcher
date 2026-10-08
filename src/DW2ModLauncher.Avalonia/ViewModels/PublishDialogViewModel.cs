using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DW2ModLauncher.Avalonia.Services;
using DW2ModLauncher.Core.Models;
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
        private string bundles;
        private VisibilityOption visibility;
        private bool replaceDescription;
        private readonly string workshopId;
        private bool pullingDescription;
        // The item's current Steam description when this is an update and Steam could be asked; null otherwise.
        private readonly string steamDescription;
        private bool replaceTouched;
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

        public PublishDialogViewModel(IDialogService dialogs, LocalizedStrings l, ModInfo mod, ModPublishMetadata metadata, bool isUpdate, ModVisibility? currentVisibility = null, bool propertiesOnly = false, string steamDescription = null, VersionBumpPolicy bumpPolicy = null)
        {
            this.dialogs = dialogs;
            this.metadata = metadata;
            L = l;
            modJsonPath = mod.ModJsonPath;
            contentFolder = mod.ContentRoot ?? mod.Folder;
            CanEditVisibility = !propertiesOnly;
            this.propertiesOnly = propertiesOnly;
            // A first publish has no Steam text to protect, so it sends the description; an update leaves the Steam page alone unless asked.
            this.steamDescription = isUpdate && !propertiesOnly ? steamDescription : null;
            replaceDescription = !isUpdate;
            PrimaryButtonText = l[propertiesOnly ? "Save" : "PublishToWorkshop"];
            WindowTitle = l[propertiesOnly ? "EditPropertiesTitle" : "PublishToWorkshop"] + " - " + (mod.DisplayName ?? mod.Id);
            // Shown as the Publish button's tooltip. Properties-only editing saves mod.json and never talks to Steam, so it has no note.
            Note = propertiesOnly ? null : l[isUpdate ? "PublishAboutToRunUpdate" : "PublishAboutToRunFirstTime"];
            WorkshopIdText = string.IsNullOrWhiteSpace(mod.WorkshopId) ? l["PublishWorkshopIdUnpublished"] : mod.WorkshopId.Trim();
            title = metadata.DisplayName;
            OriginalVersion = metadata.Version;
            // Updating a published item proposes the next version per the mod's launcher-side policy (default: patch); nothing is
            // written to mod.json until the author accepts.
            bumpPolicy = bumpPolicy ?? new VersionBumpPolicy();
            proposesBump = isUpdate && !propertiesOnly;
            BumpLevels.Add(new BumpLevelOption { Value = "none", Label = l["VersionLevelNone"] });
            foreach (VersionBumpLevel level in new[] { VersionBumpLevel.Patch, VersionBumpLevel.Minor, VersionBumpLevel.Major })
                BumpLevels.Add(new BumpLevelOption { Value = ModVersion.LevelName(level), Label = l["VersionLevel" + level] });
            string wanted = ModVersion.IsNoBump(bumpPolicy.Level) ? "none" : ModVersion.LevelName(ModVersion.ParseLevel(bumpPolicy.Level));
            bumpLevel = BumpLevels.First(o => o.Value == wanted);
            proposedVersion = ProposeVersion();
            version = proposedVersion;
            AdjustVersionCommand = RelayCommand.WithParameter(AdjustVersion);
            IsSemver = ModVersion.TryParseSemver(version, out _, out _, out _);
            previewImage = metadata.PreviewImage;
            // The long description is always description.bbcode (seeded from the legacy mod.json fields if the file doesn't exist yet).
            // A mod that already names its own description file keeps it; otherwise it is description.bbcode.
            descriptionFileName = ModDescriptionFile.NameFor(contentFolder, metadata.DescriptionFile);
            description = ModDescriptionFile.Load(contentFolder, metadata);
            // When we know what Steam has, only offer (and by default do) the replacement if the text really differs.
            if (this.steamDescription != null) replaceDescription = DescriptionDiffersFromSteam;
            lastFileText = ModDescriptionFile.ReadFile(contentFolder, descriptionFileName);
            shortDescription = metadata.ShortDescription;
            bundles = string.Join("\n", metadata.Bundles ?? new List<string>());
            // Read-only: what the launcher will inject for this mod (inferred, or the dw2modlauncher.json override).
            InjectedDlls = DW2ModLauncher.Core.Services.InjectionScanner.TargetsFor(mod);

            workshopId = long.TryParse(mod.WorkshopId, out long _) ? mod.WorkshopId.Trim() : null;
            OpenSteamPageCommand = new RelayCommand(OpenSteamPage);
            PullSteamDescriptionCommand = new RelayCommand(PullSteamDescriptionAsync);
            PushSteamDescriptionCommand = new RelayCommand(PushSteamDescriptionAsync);
            BrowsePreviewCommand = new RelayCommand(BrowsePreviewAsync);
            EditDescriptionFileCommand = new RelayCommand(EditDescriptionFileAsync);
            StartWatchingDescriptionFile();
            savedFields = FieldsSnapshot();
            savedDescription = description;

            if (propertiesOnly)
            {
                // Visibility lives on Steam and is only changed by publishing: show what it is (read-only) when we could ask.
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
                Visibilities.Add(new VisibilityOption { Value = v, Label = l[LabelKey(v)] + (v == currentVisibility ? l["VisibilityCurrentSuffix"] : "") });
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
        /// <summary>The mod.json version before this dialog proposed anything, for rolling back a publish that did not happen.</summary>
        public string OriginalVersion { get; }
        public string WindowTitle { get; }
        public string Note { get; }
        /// <summary>"Publish to Workshop", or "Save" when the dialog only edits mod.json.</summary>
        public string PrimaryButtonText { get; }
        /// <summary>False when only editing properties: visibility is Steam's to hold, shown read-only.</summary>
        public bool CanEditVisibility { get; }
        /// <summary>The "replace the Steam description" checkbox only applies to publishing, not to editing properties.</summary>
        public bool ShowReplaceDescription { get { return CanEditVisibility && (steamDescription == null || DescriptionDiffersFromSteam); } }
        /// <summary>True when Steam's text is known and differs from the box; the dialog says so next to the checkbox.</summary>
        public bool ShowDescriptionDiffers { get { return CanEditVisibility && steamDescription != null && DescriptionDiffersFromSteam; } }
        private bool DescriptionDiffersFromSteam { get { return steamDescription != null && !ModDescriptionFile.SameText(description, steamDescription); } }
        /// <summary>Whether this publish overwrites the Steam page's description. A per-publish choice, not stored in mod.json.</summary>
        public bool ReplaceDescription { get { return replaceDescription; } set { replaceTouched = true; Set(ref replaceDescription, value); } }
        /// <summary>The item's Workshop id (read-only), or "Unpublished" until a first publish has written one into mod.json.</summary>
        public string WorkshopIdText { get; }
        /// <summary>The DLLs (and entry points) the launcher will inject for this mod, shown as a read-only table.</summary>
        public List<DW2ModLauncher.Core.Services.InjectionTarget> InjectedDlls { get; }
        /// <summary>Whether the mod lists any bundles; the Bundles section starts collapsed when it does not.</summary>
        public bool HasBundles { get { return !string.IsNullOrWhiteSpace(bundles); } }
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
            return string.Join("", new[] { title, version, previewImage, shortDescription, bundles, bumpLevel.Value }.Select(x => (x ?? "").Trim()));
        }

        /// <summary>Something in the dialog differs from what it opened with.</summary>
        public bool IsModified { get { return FieldsSnapshot() != savedFields || !ModDescriptionFile.SameText(description, savedDescription); } }
        /// <summary>The primary button: publishing is always allowed; the Properties "Save" only once something changed.</summary>
        public bool CanPrimary { get { return !propertiesOnly || IsModified; } }
        private void RaiseModified() { Raise(nameof(IsModified)); Raise(nameof(CanPrimary)); }

        public string Title { get { return title; } set { if (Set(ref title, value)) RaiseModified(); } }
        public string Version
        {
            get { return version; }
            set
            {
                if (!Set(ref version, value)) return;
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

        private string ProposeVersion()
        {
            return proposesBump && !ModVersion.IsNoBump(bumpLevel.Value) ? ModVersion.Bump(OriginalVersion, ModVersion.ParseLevel(bumpLevel.Value)) : OriginalVersion;
        }

        /// <summary>A policy change replaces the proposal, unless the author already typed their own version.</summary>
        private void Repropose()
        {
            string next = ProposeVersion();
            if (version == proposedVersion) Version = next;
            proposedVersion = next;
        }
        public string PreviewImage { get { return previewImage; } set { if (Set(ref previewImage, value)) RaiseModified(); } }
        public string Description
        {
            get { return description; }
            set
            {
                if (!Set(ref description, value)) return;
                RaiseModified();
                if (steamDescription == null) return;
                // Follow the text until the author decides for themselves: differing means send it, identical means there is nothing to send.
                if (!replaceTouched) Set(ref replaceDescription, DescriptionDiffersFromSteam, nameof(ReplaceDescription));
                Raise(nameof(ShowReplaceDescription));
                Raise(nameof(ShowDescriptionDiffers));
            }
        }
        public string ShortDescription { get { return shortDescription; } set { if (Set(ref shortDescription, value)) RaiseModified(); } }
        public string Bundles { get { return bundles; } set { if (Set(ref bundles, value)) RaiseModified(); } }
        public VisibilityOption Visibility { get { return visibility; } set { if (value != null) Set(ref visibility, value); } }

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
                Description = detail.Description ?? "";
            }
            catch (Exception ex)
            {
                DW2ModLauncher.Core.Diagnostics.Logger.LogException("Pull Steam description", ex);
                await dialogs.ShowMessageAsync(L.Format("PublishPullSteamDescriptionFailed", ex.Message), "DW2 Mod Launcher");
            }
            finally { pullingDescription = false; }
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
                    Description = description,
                    DescriptionOnly = true,
                    ExistingWorkshopId = long.Parse(workshopId)
                };
                CancellationTokenSource cancel = new CancellationTokenSource();
                request.Cancel = cancel.Token;
                ModPublishResult result;
                using (dialogs.ShowBusy(L["PublishPushSteamDescriptionRunning"], "DW2 Mod Launcher", L["Cancel"], TimeSpan.FromSeconds(15), cancel))
                    result = await Task.Run(() => ModPublisherFactory.Create(uint.Parse(DW2ModLauncher.Core.Services.SteamLocator.AppId)).Publish(request));
                if (cancel.IsCancellationRequested) return;
                await dialogs.ShowMessageAsync(string.IsNullOrEmpty(result.ErrorMessage)
                    ? L["PublishPushSteamDescriptionDone"]
                    : L.Format("PublishPushSteamDescriptionFailed", result.ErrorMessage), "DW2 Mod Launcher");
            }
            catch (Exception ex)
            {
                DW2ModLauncher.Core.Diagnostics.Logger.LogException("Push Steam description", ex);
                await dialogs.ShowMessageAsync(L.Format("PublishPushSteamDescriptionFailed", ex.Message), "DW2 Mod Launcher");
            }
            finally { pullingDescription = false; }
        }

        private async Task BrowsePreviewAsync()
        {
            string picked = await dialogs.PickFileAsync(L["PublishInfoPreviewImage"], contentFolder, "Image files", "*.jpg", "*.jpeg", "*.png");
            string relative = await ResolveInsideModAsync(picked);
            if (relative != null) PreviewImage = relative;
        }

        /// <summary>
        /// mod.json can only point at files inside the mod, so a file picked from elsewhere is copied in (after asking; Yes or Cancel).
        /// Returns the mod-relative path, or null when nothing was picked, the author declined, or the copy failed.
        /// </summary>
        private async Task<string> ResolveInsideModAsync(string picked)
        {
            if (picked == null) return null;
            if (DW2ModLauncher.Core.Services.ModFileImporter.IsInside(contentFolder, picked))
                return DW2ModLauncher.Core.Services.ModFileImporter.RelativePath(contentFolder, picked);
            if (!await dialogs.ConfirmAsync(L.Format("PublishImportFileConfirm", System.IO.Path.GetFileName(picked)), "DW2 Mod Launcher", L["Yes"], L["Cancel"]))
                return null;
            try { return DW2ModLauncher.Core.Services.ModFileImporter.CopyIntoMod(contentFolder, picked); }
            catch (Exception ex)
            {
                DW2ModLauncher.Core.Diagnostics.Logger.LogException("Copy file into mod", ex);
                await dialogs.ShowMessageAsync(ex.Message, "DW2 Mod Launcher");
                return null;
            }
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

        /// <summary>Writes the edited fields back to mod.json. Returns an error message, or null on success.</summary>
        public string Commit()
        {
            metadata.DisplayName = (title ?? "").Trim();
            metadata.Version = (version ?? "").Trim();
            metadata.PreviewImage = (previewImage ?? "").Trim();
            metadata.ShortDescription = (shortDescription ?? "").Trim();
            // The long description goes to description.bbcode first; only then does Write drop the legacy mod.json keys, so a failed
            // file write can't lose the text. An untouched, never-written description (nothing in the box, no file) creates no file.
            metadata.Description = "";
            metadata.DescriptionFile = descriptionFileName == ModDescriptionFile.FileName ? "" : descriptionFileName;
            try
            {
                if (!string.IsNullOrWhiteSpace(description) || ModDescriptionFile.ReadFile(contentFolder, descriptionFileName) != null)
                    ModDescriptionFile.Save(contentFolder, descriptionFileName, description);
            }
            catch (Exception ex)
            {
                DW2ModLauncher.Core.Diagnostics.Logger.LogException("Write description.bbcode", ex);
                return ex.Message;
            }
            metadata.Bundles = (bundles ?? "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
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
