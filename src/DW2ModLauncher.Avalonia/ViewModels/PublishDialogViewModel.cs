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
        // What description.bbcode held the last time we looked, so a watcher event only touches the box when the file really changed.
        private readonly string descriptionFileName;
        private string lastFileText;
        private FileSystemWatcher watcher;
        private Timer debounce;

        public PublishDialogViewModel(IDialogService dialogs, LocalizedStrings l, ModInfo mod, ModPublishMetadata metadata, bool isUpdate, ModVisibility? currentVisibility = null, bool propertiesOnly = false, string steamDescription = null)
        {
            this.dialogs = dialogs;
            this.metadata = metadata;
            L = l;
            modJsonPath = mod.ModJsonPath;
            contentFolder = mod.ContentRoot ?? mod.Folder;
            CanEditVisibility = !propertiesOnly;
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
            // Updating a published item proposes the next patch version; nothing is written to mod.json until the author accepts.
            version = isUpdate && !propertiesOnly ? ModVersion.BumpPatch(metadata.Version) : metadata.Version;
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
            BrowsePreviewCommand = new RelayCommand(BrowsePreviewAsync);
            EditDescriptionFileCommand = new RelayCommand(EditDescriptionFileAsync);
            StartWatchingDescriptionFile();

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
        public RelayCommand BrowsePreviewCommand { get; }
        public RelayCommand EditDescriptionFileCommand { get; }

        public string Title { get { return title; } set { Set(ref title, value); } }
        public string Version { get { return version; } set { Set(ref version, value); } }
        public string PreviewImage { get { return previewImage; } set { Set(ref previewImage, value); } }
        public string Description
        {
            get { return description; }
            set
            {
                if (!Set(ref description, value) || steamDescription == null) return;
                // Follow the text until the author decides for themselves: differing means send it, identical means there is nothing to send.
                if (!replaceTouched) Set(ref replaceDescription, DescriptionDiffersFromSteam, nameof(ReplaceDescription));
                Raise(nameof(ShowReplaceDescription));
                Raise(nameof(ShowDescriptionDiffers));
            }
        }
        public string ShortDescription { get { return shortDescription; } set { Set(ref shortDescription, value); } }
        public string Bundles { get { return bundles; } set { Set(ref bundles, value); } }
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
            Description = text;
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
