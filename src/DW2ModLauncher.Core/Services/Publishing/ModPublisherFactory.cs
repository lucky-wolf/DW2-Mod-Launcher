using System;
using System.Collections.Generic;

namespace DW2ModLauncher.Core.Services.Publishing
{
    /// <summary>Creates the IModPublisher (Steamworks.NET on every OS).</summary>
    public static class ModPublisherFactory
    {
        /// <summary>The visibility levels the publisher can set.</summary>
        public static IList<ModVisibility> SupportedVisibilities()
        {
            return new List<ModVisibility> { ModVisibility.Private, ModVisibility.FriendsOnly, ModVisibility.Unlisted, ModVisibility.Public };
        }

        public static IModPublisher Create(uint appId)
        {
            return new SteamworksNetModPublisher(appId);
        }
    }
}
