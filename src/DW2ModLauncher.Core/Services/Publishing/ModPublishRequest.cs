namespace DW2ModLauncher.Core.Services.Publishing
{
    /// <summary>
    /// Everything an IModPublisher needs to know to publish/update a MOD, as plain explicit data
    /// rather than "go read mod.json yourself" - so every implementation works from the same
    /// shape regardless of mechanism (embedding the Steamworks API directly, some future
    /// alternative, etc).
    /// </summary>
    public class ModPublishRequest
    {
        public string ContentFolder { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string PreviewImagePath { get; set; }
        public long? ExistingWorkshopId { get; set; }
    }
}
