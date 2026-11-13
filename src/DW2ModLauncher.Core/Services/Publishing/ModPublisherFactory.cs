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
        public static bool UseHelperProcess { get; set; } = true;

        /// <summary>
        /// The master Steam switch (from the launcher settings). False: <see cref="Create"/> refuses. Every caller checks the switch first, so a throw
        /// here means a code path forgot to, and it is reported rather than letting the launcher reach for Steam.
        /// </summary>
        public static bool Enabled { get; set; } = true;

        public static IModPublisher Create(uint appId)
        {
            if (!Enabled) throw new InvalidOperationException("Steam is turned off in the launcher settings, so nothing may ask Steam.");
            if (UseHelperProcess) return new OutOfProcessModPublisher(appId);
            return new SteamworksNetModPublisher(appId, true);
        }
    }
}
