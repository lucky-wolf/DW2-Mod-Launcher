namespace DW2ModLauncher.Core.Services.Publishing
{
    /// <summary>
    /// What IModPublisher.Publish hands back. WorkshopId is set on success - the new or updated
    /// item's id, straight from Steam, no guessing or console-scraping involved.
    /// ErrorMessage is set on failure (Steam not running/logged in, the call itself failing, etc).
    /// NeedsWorkshopAgreement means Steam accepted the item but the Mod's Steam account still has
    /// to accept the Workshop legal agreement (a one-time thing per account) before the item is
    /// actually visible to anyone else.
    /// </summary>
    public class ModPublishResult
    {
        public long? WorkshopId { get; set; }
        public string ErrorMessage { get; set; }
        public bool NeedsWorkshopAgreement { get; set; }
    }
}
