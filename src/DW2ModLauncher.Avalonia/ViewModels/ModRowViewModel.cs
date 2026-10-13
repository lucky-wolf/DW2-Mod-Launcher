using System.IO;
using System.Windows.Input;
using Avalonia.Media.Imaging;
using DW2ModLauncher.Core.Models;
using DW2ModLauncher.Core.Services;

namespace DW2ModLauncher.Avalonia.ViewModels
{
    /// <summary>One row of the mod list.</summary>
    public class ModRowViewModel : ViewModelBase
    {
        private Bitmap thumbnail;
        private bool thumbnailLoaded;

        private readonly string managedModsRoot;

        public ModRowViewModel(ModInfo mod, ICommand toggleCommand, string managedModsRoot)
        {
            Mod = mod;
            ToggleCommand = toggleCommand;
            this.managedModsRoot = managedModsRoot;
        }

        public ModInfo Mod { get; }
        public ICommand ToggleCommand { get; }

        public string Name { get { return Mod.DisplayName ?? Mod.Id ?? "Unknown"; } }
        /// <summary>Where the mod lives, as the folder name under the mods folder;the subtitle under the source (local mods only), so same-named copies (XL, XL.bak) can be told apart.</summary>
        public string FolderPath { get { return Mod.IsWorkshop || string.IsNullOrWhiteSpace(Mod.Folder) ? "" : ModDetails.SourceText(Mod, managedModsRoot); } }
        public bool HasFolderPath { get { return FolderPath.Length > 0; } }
        public string Source { get { return Mod.SourceName ?? ""; } }
        public string Author { get { return Mod.Author ?? ""; } }

        /// <summary>Whether a published local mod's folder differs from its last publish: the dot beside its name.</summary>
        public bool HasUnpublishedChanges { get { return Mod.PublishStatus == PublishStatus.Changed; } }
        public string UnpublishedTip { get { return HasUnpublishedChanges && Strings != null ? Strings("PublishStateBadgeTip") : null; } }
        public System.Func<string, string> Strings { get; set; }

        /// <summary>The visibility icon beside the source, once looked up (see MainViewModel.StartVisibilityLookup).</summary>
        public bool IsVisPublic { get { return Mod.Visibility == DW2ModLauncher.Core.Services.Publishing.ModVisibility.Public; } }
        public bool IsVisPrivate { get { return Mod.Visibility == DW2ModLauncher.Core.Services.Publishing.ModVisibility.Private; } }
        public bool IsVisFriends { get { return Mod.Visibility == DW2ModLauncher.Core.Services.Publishing.ModVisibility.FriendsOnly; } }
        public bool IsVisUnlisted { get { return Mod.Visibility == DW2ModLauncher.Core.Services.Publishing.ModVisibility.Unlisted; } }
        /// <summary>A local mod that was never published gets a cloud-with-slash instead, so the column is never empty for a mod we know about.</summary>
        public bool IsNotPublished { get { return Mod.PublishStatus == PublishStatus.Unpublished; } }
        public bool HasVisibility { get { return Mod.Visibility.HasValue || IsNotPublished; } }
        public string VisibilityTip
        {
            get
            {
                if (Strings == null) return null;
                if (IsNotPublished) return Strings("PublishStateUnpublishedDetail");
                return Mod.Visibility.HasValue ? Strings(PublishDialogViewModel.LabelKey(Mod.Visibility.Value)) : null;
            }
        }

        public void RefreshPublishState()
        {
            Raise(nameof(IsVisPublic));
            Raise(nameof(IsVisPrivate));
            Raise(nameof(IsVisFriends));
            Raise(nameof(IsVisUnlisted));
            Raise(nameof(IsNotPublished));
            Raise(nameof(HasVisibility));
            Raise(nameof(VisibilityTip));
            Raise(nameof(HasUnpublishedChanges));
            Raise(nameof(UnpublishedTip));
        }

        /// <summary>Re-reads fields the Workshop check or a language change can alter on the underlying mod.</summary>
        public void RefreshFromMod()
        {
            Raise(nameof(Name));
            Raise(nameof(Source));
            RefreshPublishState();
        }

        private bool enabled;
        public bool Enabled
        {
            get { return enabled; }
            set { if (Set(ref enabled, value)) Raise(nameof(StateGlyph)); }
        }
        public string StateGlyph { get { return enabled ? "☑" : "☐"; } }

        private string healthTip;
        /// <summary>What is wrong with an enabled mod, for the tooltip on its checkbox: Caution or Conflict, then the problems. Null when there is nothing to say.</summary>
        public string HealthTip { get { return healthTip; } set { Set(ref healthTip, value); } }

        private int healthSeverity;
        public int HealthSeverity
        {
            get { return healthSeverity; }
            set
            {
                if (!Set(ref healthSeverity, value)) return;
                Raise(nameof(IsConflict));
                Raise(nameof(IsCaution));
                Raise(nameof(IsOk));
            }
        }
        public bool IsConflict { get { return healthSeverity == 3; } }
        public bool IsCaution { get { return healthSeverity == 2; } }
        public bool IsOk { get { return healthSeverity == 1; } }

        private int loadOrderIndex = -1;
        public int LoadOrderIndex
        {
            get { return loadOrderIndex; }
            set { if (Set(ref loadOrderIndex, value)) Raise(nameof(LoadOrderNumber)); }
        }
        /// <summary>The load-order number next to the checkbox; blank for a mod that is not enabled.</summary>
        public string LoadOrderNumber { get { return loadOrderIndex < 0 ? "" : (loadOrderIndex + 1).ToString(System.Globalization.CultureInfo.InvariantCulture); } }

        /// <summary>Small preview for the list; loaded on first use (rows are only realised when scrolled into view).</summary>
        public Bitmap Thumbnail
        {
            get
            {
                if (!thumbnailLoaded)
                {
                    thumbnailLoaded = true;
                    thumbnail = ImageLoader.Load(Mod.PreviewImage, 96);
                }
                return thumbnail;
            }
        }
    }

    public static class ImageLoader
    {
        /// <summary>Loads an image without keeping the file open (so Steam can still replace it). Null if missing or unreadable.</summary>
        public static Bitmap Load(string path, int decodeWidth)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try
            {
                using (MemoryStream ms = new MemoryStream(File.ReadAllBytes(path)))
                    return decodeWidth > 0 ? Bitmap.DecodeToWidth(ms, decodeWidth) : new Bitmap(ms);
            }
            catch (System.Exception ex)
            {
                // Shows no image; the log says why, for the mod author.
                DW2ModLauncher.Core.Diagnostics.Logger.LogException("Load preview image: " + path, ex);
                return null;
            }
        }
    }
}
