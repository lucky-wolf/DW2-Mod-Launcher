namespace DW2ModLauncher.Core.Services.Publishing
{
    /// <summary>
    /// Publishes a local Mod to the Steam Workshop and reports back the Workshop item id, however
    /// that actually happens under the hood. <see cref="SteamworksNetModPublisher"/> (embedding the
    /// Steamworks API directly, the same way DW2 itself does) is the only implementation today, but
    /// this stays an interface on purpose - the previous implementation shelled out to DW2's own
    /// "--ugc-publish" instead, and needed a completely different mechanism (see git history /
    /// docs/workshop-publish.md) to make that work. A future alternative should only ever mean
    /// writing a new class here, not touching anything that calls Publish.
    /// </summary>
    public interface IModPublisher
    {
        ModPublishResult Publish(ModPublishRequest request);

        /// <summary>The item's current visibility on Steam, or null if it can't be determined (Steam not running, item not found, timed out).</summary>
        ModVisibility? GetVisibility(long workshopId);

        /// <summary>
        /// Asks Steam about many items in one go (one Steam round trip per 50). Items Steam did not answer for are left out, and the list is
        /// empty when Steam can't be asked, so a failure never looks like a deletion.
        /// </summary>
        System.Collections.Generic.List<SteamItemStatus> QueryItems(System.Collections.Generic.IReadOnlyList<long> workshopIds);
    }
}
