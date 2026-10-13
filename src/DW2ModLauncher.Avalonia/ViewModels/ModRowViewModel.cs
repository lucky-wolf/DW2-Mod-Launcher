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

        /// <summary>Re-reads fields the Workshop check or a language change can alter on the underlying mod.</summary>
        public void RefreshFromMod()
        {
            Raise(nameof(Name));
            Raise(nameof(Source));
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
