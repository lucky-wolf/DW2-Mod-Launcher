using System;

namespace DW2ModLauncher.Core.Services.Publishing
{
    /// <summary>Picks the IModPublisher implementation for the current OS.</summary>
    public static class ModPublisherFactory
    {
        public static IModPublisher Create(uint appId)
        {
            // Windows keeps Facepunch.Steamworks until the Steamworks.NET publisher has been verified there.
            return OperatingSystem.IsWindows() ? new SteamworksModPublisher(appId) : new SteamworksNetModPublisher(appId);
        }
    }
}
