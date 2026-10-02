using System;
using System.Collections.Generic;

namespace DW2ModLauncher.Core.Services.Publishing
{
    /// <summary>Picks the IModPublisher implementation for the current OS.</summary>
    public static class ModPublisherFactory
    {
        /// <summary>The visibility levels the publisher for this OS can set (Facepunch.Steamworks has no "unlisted").</summary>
        public static IList<ModVisibility> SupportedVisibilities()
        {
            return OperatingSystem.IsWindows()
                ? new List<ModVisibility> { ModVisibility.Private, ModVisibility.FriendsOnly, ModVisibility.Public }
                : new List<ModVisibility> { ModVisibility.Private, ModVisibility.FriendsOnly, ModVisibility.Unlisted, ModVisibility.Public };
        }

        public static IModPublisher Create(uint appId)
        {
            // Windows keeps Facepunch.Steamworks until the Steamworks.NET publisher has been verified there.
            return OperatingSystem.IsWindows() ? new SteamworksModPublisher(appId) : new SteamworksNetModPublisher(appId);
        }
    }
}
