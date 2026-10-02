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
    }
}
