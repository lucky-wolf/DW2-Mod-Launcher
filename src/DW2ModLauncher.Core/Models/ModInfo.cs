using System.Collections.Generic;

namespace DW2ModLauncher.Core.Models
{
    public class ModInfo
    {
        public string Id { get; set; }
        public string DisplayName { get; set; }
        public string Description { get; set; }
        public string Version { get; set; }
        public string PreviewImage { get; set; }
        public string Folder { get; set; }
        public string ContentRoot { get; set; }
        public bool IsWorkshop { get; set; }
        public string SourceName { get; set; }
        public string ActiveToken { get; set; }
        public int DuplicateCount { get; set; }
        public List<string> DuplicateLocations { get; set; }
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
        public string ModJsonLaunchArguments { get; set; }
        public List<string> IncludedTools { get; set; }
        public List<string> IncludedDocuments { get; set; }
        public List<string> RequiredMods { get; set; }
        public List<string> OptionalMods { get; set; }
        public List<string> IncompatibleMods { get; set; }
        public List<string> LoadBefore { get; set; }
        public List<string> LoadAfter { get; set; }
        public int IdenticalFileCount { get; set; }
        public int LowRiskConflictCount { get; set; }
        public int HighRiskConflictCount { get; set; }

        public string Key
        {
            get
            {
                return (IsWorkshop ? "workshop:" : "managed:") + (Id ?? Folder ?? DisplayName ?? "unknown");
            }
        }
    }
}
