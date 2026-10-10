using System.Collections.Generic;

namespace DW2ModLauncher.Core.Models
{
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
        /// <summary>Why mod.json could not be parsed (its dependencies and load order hints were then not read), or null when it parsed.</summary>
        public string ModJsonError { get; set; }
        /// <summary>Why the mod's files could not be scanned for conflicts (so none are reported for it), or null.</summary>
        public string ScanError { get; set; }
        // The "workshopId" field from mod.json, if the Mod author has published it before (see
        // ModPublishCommandBuilder/ModJsonWorkshopIdWriter) - distinct from IsWorkshop, which means
        // "this copy came from a Steam Workshop subscription," not "this Mod has ever been published."
        public string WorkshopId { get; set; }
        public List<string> IncludedTools { get; set; }
        public List<string> IncludedDocuments { get; set; }
        public List<string> RequiredMods { get; set; }
        public List<string> OptionalMods { get; set; }
        public List<string> IncompatibleMods { get; set; }
        public List<string> LoadBefore { get; set; }
        public List<string> LoadAfter { get; set; }
        /// <summary>dw2modlauncher.json "minLauncherVersion", or null/empty when the mod names no floor.</summary>
        public string MinLauncherVersion { get; set; }
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
