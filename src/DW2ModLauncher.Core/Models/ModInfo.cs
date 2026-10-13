using System.Collections.Generic;

namespace DW2ModLauncher.Core.Models
{
    /// <summary>Whether a local mod's folder differs from what its last publish uploaded.</summary>
    public enum PublishStatus
    {
        /// <summary>A Workshop copy: nothing to compare.</summary>
        NotApplicable,
        /// <summary>A local mod that was never published.</summary>
        Unpublished,
        /// <summary>Published, but no record was taken (published before change tracking existed).</summary>
        Unknown,
        Current,
        Changed
    }

    public class ModInfo
    {
        public string Id { get; set; }
        public string DisplayName { get; set; }
        public string Description { get; set; }
        /// <summary>Text of the file named by mod.json's "descriptionFile". When present it wins over the short description (and Steam's).</summary>
        public string DescriptionOverride { get; set; }
        public string Version { get; set; }
        public string PreviewImage { get; set; }
        public string Folder { get; set; }
        public string ContentRoot { get; set; }
        public bool IsWorkshop { get; set; }
        public string SourceName { get; set; }
        public string ActiveToken { get; set; }
        public string WorkshopDescription { get; set; }
        public string WorkshopTitle { get; set; }
        public string WorkshopPreviewUrl { get; set; }
        public string WorkshopCreator { get; set; }
        public long WorkshopFileSize { get; set; }
        public long WorkshopTimeCreated { get; set; }
        public string WorkshopTags { get; set; }
        public long LocalWorkshopTimeUpdated { get; set; }
        public long RemoteWorkshopTimeUpdated { get; set; }
        public string UpdateState { get; set; }
        public int ConflictCount { get; set; }
        public List<string> ConflictFiles { get; set; }
        public List<string> ConflictMods { get; set; }
        public List<string> ConflictPathCache { get; set; }
        public string ModJsonPath { get; set; }
        /// <summary>Why mod.json could not be parsed (its display name and description were then recovered loosely), or null when it parsed.</summary>
        public string ModJsonError { get; set; }
        /// <summary>Why the mod's files could not be scanned for conflicts (so none are reported for it), or null.</summary>
        public string ScanError { get; set; }
        // The "workshopId" field from mod.json, if the Mod author has published it before (see
        // ModPublishCommandBuilder/ModJsonWorkshopIdWriter) - distinct from IsWorkshop, which means
        // "this copy came from a Steam Workshop subscription," not "this Mod has ever been published."
        public string WorkshopId { get; set; }
        public PublishStatus PublishStatus { get; set; }
        /// <summary>The item's visibility on Steam, once Steam has been asked (the startup / Refresh sweep, or when the mod is selected); null until then and for mods that are not published.</summary>
        public DW2ModLauncher.Core.Services.Publishing.ModVisibility? Visibility { get; set; }
        /// <summary>When the last recorded publish happened (UTC), or null when there is no record.</summary>
        public System.DateTime? PublishedUtc { get; set; }
        /// <summary>When <see cref="PublishStatus"/> is Changed, the changed files, each marked + (added), - (removed) or ~ (modified).</summary>
        public List<string> UnpublishedChanges { get; set; } = new List<string>();
        public List<string> IncludedTools { get; set; }
        public List<string> IncludedDocuments { get; set; }
        /// <summary>dw2modlauncher.json "minLauncherVersion", or null/empty when the mod names no floor.</summary>
        public string MinLauncherVersion { get; set; }
        /// <summary>dw2modlauncher.json "author", or null/empty when the mod names none.</summary>
        public string Author { get; set; }
        /// <summary>dw2modlauncher.json "dependencies" (empty when the mod names none).</summary>
        public List<LauncherDependency> Dependencies { get; set; } = new List<LauncherDependency>();
        /// <summary>dw2modlauncher.json "provides" (empty when the mod supplies nothing).</summary>
        public List<LauncherProvision> Provides { get; set; } = new List<LauncherProvision>();
        /// <summary>dw2modlauncher.json "receives" (empty when the mod's DLL takes no channels).</summary>
        public List<string> Receives { get; set; } = new List<string>();
        public int IdenticalFileCount { get; set; }
        public int LowRiskConflictCount { get; set; }
        public int HighRiskConflictCount { get; set; }
        /// <summary>How many other enabled copies of this same mod (same Workshop item id) are enabled alongside it.</summary>
        public int EnabledCopyCount { get; set; }

        public string Key
        {
            get
            {
                return (IsWorkshop ? "workshop:" : "managed:") + (Id ?? Folder ?? DisplayName ?? "unknown");
            }
        }
    }
}
