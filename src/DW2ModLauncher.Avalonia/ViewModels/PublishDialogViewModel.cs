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
        private string bundles;
        private VisibilityOption visibility;

        public PublishDialogViewModel(IDialogService dialogs, LocalizedStrings l, ModInfo mod, ModPublishMetadata metadata, bool isUpdate)
        {
            this.dialogs = dialogs;
            this.metadata = metadata;
            L = l;
            modJsonPath = mod.ModJsonPath;
            contentFolder = mod.ContentRoot ?? mod.Folder;
            WindowTitle = l["PublishToWorkshop"] + " - " + (mod.DisplayName ?? mod.Id);
            Note = l[isUpdate ? "PublishAboutToRunUpdate" : "PublishAboutToRunFirstTime"];
            title = metadata.DisplayName;
            version = metadata.Version;
            previewImage = metadata.PreviewImage;
            description = metadata.Description;
            bundles = string.Join("\n", metadata.Bundles ?? new List<string>());

            if (isUpdate) Visibilities.Add(new VisibilityOption { Value = null, Label = l["VisibilityUnchanged"] });
            foreach (ModVisibility v in ModPublisherFactory.SupportedVisibilities())
                Visibilities.Add(new VisibilityOption { Value = v, Label = l[LabelKey(v)] });
            // New items default to private; updates default to leaving whatever the item already has.
            visibility = Visibilities.First(o => isUpdate ? o.Value == null : o.Value == ModVisibility.Private);

            BrowsePreviewCommand = new RelayCommand(BrowsePreviewAsync);
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
        public List<VisibilityOption> Visibilities { get; } = new List<VisibilityOption>();
        public RelayCommand BrowsePreviewCommand { get; }

        public string Title { get { return title; } set { Set(ref title, value); } }
        public string Version { get { return version; } set { Set(ref version, value); } }
        public string PreviewImage { get { return previewImage; } set { Set(ref previewImage, value); } }
        public string Description { get { return description; } set { Set(ref description, value); } }
        public string Bundles { get { return bundles; } set { Set(ref bundles, value); } }
        public VisibilityOption Visibility { get { return visibility; } set { if (value != null) Set(ref visibility, value); } }

        public ModPublishMetadata Metadata { get { return metadata; } }
        public ModVisibility? SelectedVisibility { get { return visibility.Value; } }

        private async Task BrowsePreviewAsync()
        {
            string picked = await dialogs.PickFileAsync(L["PublishInfoPreviewImage"], contentFolder, "Image files", "*.jpg", "*.jpeg", "*.png");
            if (picked != null) PreviewImage = System.IO.Path.GetFileName(picked);
        }

        /// <summary>Writes the edited fields back to mod.json. Returns an error message, or null on success.</summary>
        public string Commit()
        {
            metadata.DisplayName = (title ?? "").Trim();
            metadata.Version = (version ?? "").Trim();
            metadata.PreviewImage = (previewImage ?? "").Trim();
            metadata.Description = description ?? "";
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
