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
        /// <summary>Where the mod lives, as "mods\&lt;folder&gt;"; the subtitle under the source (local mods only), so same-named copies (XL, XL.bak) can be told apart.</summary>
        public string FolderPath { get { return Mod.IsWorkshop || string.IsNullOrWhiteSpace(Mod.Folder) ? "" : ModDetails.SourceText(Mod, managedModsRoot); } }
        public bool HasFolderPath { get { return FolderPath.Length > 0; } }
        public string Source { get { return Mod.SourceName ?? ""; } }

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

        private string health = "";
        public string Health { get { return health; } set { Set(ref health, value); } }

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
            set { if (Set(ref loadOrderIndex, value)) Raise(nameof(LoadOrder)); }
        }
        public string LoadOrder { get { return loadOrderIndex < 0 ? "—" : (loadOrderIndex + 1).ToString(System.Globalization.CultureInfo.InvariantCulture); } }

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
            catch { return null; }
        }
    }
}
