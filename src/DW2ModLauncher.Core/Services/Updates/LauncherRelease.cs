namespace DW2ModLauncher.Core.Services.Updates
{
    /// <summary>A newer launcher release on GitHub, with the package for this OS.</summary>
    public class LauncherRelease
    {
        /// <summary>Plain MAJOR.MINOR.PATCH, without the leading "v".</summary>
        public string Version { get; set; }
        public string Notes { get; set; }
        public string AssetName { get; set; }
        public string AssetUrl { get; set; }
        public long AssetSize { get; set; }
    }
}
