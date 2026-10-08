namespace DW2ModLauncher.Core.Services.Publishing
{
    public enum ModVisibility
    {
        Public,
        FriendsOnly,
        Private,
        Unlisted
    }

    /// <summary>
    /// Everything an IModPublisher needs to know to publish/update a Mod, as plain explicit data
    /// rather than "go read mod.json yourself" - so every implementation works from the same
    /// shape regardless of mechanism (embedding the Steamworks API directly, some future
    /// alternative, etc).
    /// </summary>
    public class ModPublishRequest
    {
        public string ContentFolder { get; set; }
        public string Title { get; set; }
        /// <summary>Null or blank leaves the Steam page's description untouched; anything else replaces it on every publish.</summary>
        public string Description { get; set; }
        /// <summary>Sends only <see cref="Description"/> to an existing item: no title, content, preview image or visibility is touched (and none is needed).</summary>
        public bool DescriptionOnly { get; set; }
        public string PreviewImagePath { get; set; }
        public long? ExistingWorkshopId { get; set; }
        /// <summary>Null leaves visibility alone: a new item stays private (Steam's default), an existing one keeps what it has.</summary>
        public ModVisibility? Visibility { get; set; }
        /// <summary>Lets the caller stop waiting on Steam. A cancelled publish returns an error result (WorkshopId is still set if the item was already created).</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public System.Threading.CancellationToken Cancel { get; set; }
    }
}
