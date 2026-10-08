namespace DW2ModLauncher.Core.Models
{
    /// <summary>How the publish dialog proposes the next version for one mod. Stored per mod in launcher_settings.json, not in the mod.</summary>
    public class VersionBumpPolicy
    {
        /// <summary>"patch" (default), "minor", "major", or "none" to leave the version alone when updating; anything else is treated as patch.</summary>
        public string Level { get; set; } = "patch";
    }
}
