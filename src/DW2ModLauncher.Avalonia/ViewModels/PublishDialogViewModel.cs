using System;
using System.Collections.Generic;
using System.Linq;
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
    public class PublishDialogViewModel : ViewModelBase
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
        private string descriptionFile;
        private string bundles;
        private VisibilityOption visibility;
        private bool replaceDescription;
        private bool useDescriptionFile;

        public PublishDialogViewModel(IDialogService dialogs, LocalizedStrings l, ModInfo mod, ModPublishMetadata metadata, bool isUpdate, ModVisibility? currentVisibility = null, bool propertiesOnly = false)
        {
            this.dialogs = dialogs;
            this.metadata = metadata;
            L = l;
            modJsonPath = mod.ModJsonPath;
            contentFolder = mod.ContentRoot ?? mod.Folder;
            CanEditVisibility = !propertiesOnly;
            // A first publish has no Steam text to protect, so it sends the description; an update leaves the Steam page alone unless asked.
            replaceDescription = !isUpdate;
            PrimaryButtonText = l[propertiesOnly ? "Save" : "PublishToWorkshop"];
            WindowTitle = l[propertiesOnly ? "EditPropertiesTitle" : "PublishToWorkshop"] + " - " + (mod.DisplayName ?? mod.Id);
            // Shown as the Publish button's tooltip. Properties-only editing saves mod.json and never talks to Steam, so it has no note.
            Note = propertiesOnly ? null : l[isUpdate ? "PublishAboutToRunUpdate" : "PublishAboutToRunFirstTime"];
            WorkshopIdText = string.IsNullOrWhiteSpace(mod.WorkshopId) ? l["PublishWorkshopIdUnpublished"] : mod.WorkshopId.Trim();
            title = metadata.DisplayName;
            version = metadata.Version;
            previewImage = metadata.PreviewImage;
            description = metadata.Description;
            shortDescription = metadata.ShortDescription;
            descriptionFile = metadata.DescriptionFile;
            // The description comes from a file or from the typed text, never both. A mod.json that has a file uses it (the launcher
            // shows the file over the text too); saving drops whichever one isn't chosen.
            useDescriptionFile = !string.IsNullOrWhiteSpace(metadata.DescriptionFile);
            bundles = string.Join("\n", metadata.Bundles ?? new List<string>());
            // Read-only: what the launcher will inject for this mod (inferred, or the dw2modlauncher.json override).
            InjectedDlls = DW2ModLauncher.Core.Services.InjectionScanner.TargetsFor(mod);

            BrowsePreviewCommand = new RelayCommand(BrowsePreviewAsync);
            BrowseDescriptionFileCommand = new RelayCommand(BrowseDescriptionFileAsync);
            EditDescriptionFileCommand = new RelayCommand(EditDescriptionFileAsync);

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

        private static string LabelKey(ModVisibility v)
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
        public string WindowTitle { get; }
        public string Note { get; }
        /// <summary>"Publish to Workshop", or "Save" when the dialog only edits mod.json.</summary>
        public string PrimaryButtonText { get; }
        /// <summary>False when only editing properties: visibility is Steam's to hold, shown read-only.</summary>
        public bool CanEditVisibility { get; }
        /// <summary>The "replace the Steam description" checkbox only applies to publishing, not to editing properties.</summary>
        public bool ShowReplaceDescription { get { return CanEditVisibility; } }
        /// <summary>Whether this publish overwrites the Steam page's description. A per-publish choice, not stored in mod.json.</summary>
        public bool ReplaceDescription { get { return replaceDescription; } set { Set(ref replaceDescription, value); } }
        /// <summary>The item's Workshop id (read-only), or "Unpublished" until a first publish has written one into mod.json.</summary>
        public string WorkshopIdText { get; }
        /// <summary>The DLLs (and entry points) the launcher will inject for this mod, shown as a read-only table.</summary>
        public List<DW2ModLauncher.Core.Services.InjectionTarget> InjectedDlls { get; }
        public bool HasInjectedDlls { get { return InjectedDlls.Count > 0; } }
        public bool NoInjectedDlls { get { return InjectedDlls.Count == 0; } }
        public List<VisibilityOption> Visibilities { get; } = new List<VisibilityOption>();
        public RelayCommand BrowsePreviewCommand { get; }
        public RelayCommand BrowseDescriptionFileCommand { get; }
        public RelayCommand EditDescriptionFileCommand { get; }

        public string Title { get { return title; } set { Set(ref title, value); } }
        public string Version { get { return version; } set { Set(ref version, value); } }
        public string PreviewImage { get { return previewImage; } set { Set(ref previewImage, value); } }
        public string Description { get { return description; } set { Set(ref description, value); } }
        public bool UseDescriptionFile
        {
            get { return useDescriptionFile; }
            set { if (Set(ref useDescriptionFile, value)) Raise(nameof(UseDescriptionText)); }
        }
        public bool UseDescriptionText
        {
            get { return !useDescriptionFile; }
            set { UseDescriptionFile = !value; }
        }
        public string ShortDescription { get { return shortDescription; } set { Set(ref shortDescription, value); } }
        public string DescriptionFile { get { return descriptionFile; } set { Set(ref descriptionFile, value); } }
        public string Bundles { get { return bundles; } set { Set(ref bundles, value); } }
        public VisibilityOption Visibility { get { return visibility; } set { if (value != null) Set(ref visibility, value); } }

        public ModPublishMetadata Metadata { get { return metadata; } }
        public ModVisibility? SelectedVisibility { get { return visibility.Value; } }

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

        private async Task BrowseDescriptionFileAsync()
        {
            string picked = await dialogs.PickFileAsync(L["PublishDescriptionFile"], contentFolder, "Text files", "*.txt", "*.md", "*.markdown", "*.bbcode");
            string relative = await ResolveInsideModAsync(picked);
            if (relative != null) DescriptionFile = relative;
        }

        /// <summary>Opens the description file in the OS's associated editor; if there isn't one (yet), lets the author pick it instead.</summary>
        private async Task EditDescriptionFileAsync()
        {
            string path = string.IsNullOrWhiteSpace(descriptionFile) ? null
                : System.IO.Path.Combine(contentFolder, descriptionFile.Trim().Replace('/', System.IO.Path.DirectorySeparatorChar));
            if (path == null || !System.IO.File.Exists(path))
            {
                await BrowseDescriptionFileAsync();
                return;
            }
            try { DW2ModLauncher.Core.Services.PlatformShell.Create().OpenFile(path); }
            catch (Exception ex) { await dialogs.ShowMessageAsync(ex.Message, "DW2 Mod Launcher"); }
        }

        /// <summary>Writes the edited fields back to mod.json. Returns an error message, or null on success.</summary>
        public string Commit()
        {
            metadata.DisplayName = (title ?? "").Trim();
            metadata.Version = (version ?? "").Trim();
            metadata.PreviewImage = (previewImage ?? "").Trim();
            // Only the chosen source is kept in mod.json; a blank value removes its key (see ModPublishMetadataEditor.Write).
            metadata.Description = useDescriptionFile ? "" : description ?? "";
            metadata.ShortDescription = (shortDescription ?? "").Trim();
            metadata.DescriptionFile = useDescriptionFile ? (descriptionFile ?? "").Trim() : "";
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
