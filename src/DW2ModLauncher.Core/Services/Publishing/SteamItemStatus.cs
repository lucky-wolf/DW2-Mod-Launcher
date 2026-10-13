namespace DW2ModLauncher.Core.Services.Publishing
{
    /// <summary>
    /// What one batched Steam query (<see cref="IModPublisher.QueryItems"/>) learned about a Workshop item. The query already returns
    /// much more (update time, moderation state, vote counts ...); add a field here when the launcher has a use for it and every
    /// published local mod gets it in the same single sweep.
    /// </summary>
    public class SteamItemStatus
    {
        public long Id { get; set; }
        /// <summary>Steam positively reported the item as no longer existing.</summary>
        public bool Deleted { get; set; }
        /// <summary>The item's visibility; null when it is deleted or Steam did not say.</summary>
        public ModVisibility? Visibility { get; set; }
    }
}
