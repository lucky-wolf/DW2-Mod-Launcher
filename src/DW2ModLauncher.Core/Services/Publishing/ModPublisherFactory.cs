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

        /// <summary>
        /// False (default): the launcher connects to Steam as DW2 on the first call and stays connected until it exits, so Steam
        /// sees DW2 running as long as the launcher is open. True: every call runs in a short-lived helper process, so DW2 only
        /// looks running to Steam during the call. Set from the launcher settings at startup and when they are saved.
        /// </summary>
        public static bool UseHelperProcess { get; set; }

        public static IModPublisher Create(uint appId)
        {
            if (UseHelperProcess) return new OutOfProcessModPublisher(appId);
            return new SteamworksNetModPublisher(appId, true);
        }
    }
}
