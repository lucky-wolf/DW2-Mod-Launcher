using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>Where a GOG install of Distant Worlds 2 might be (Windows): GOG Galaxy's registry entry, then the usual folders.</summary>
    public static class GogLocator
    {
        /// <summary>DW2's id in GOG's catalog.</summary>
        public const string GameId = "1562671603";

        public static List<string> Candidates()
        {
            List<string> found = new List<string>();
            if (!OperatingSystem.IsWindows()) return found;

            string[] keys = new string[]
            {
                @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\GOG.com\Games\" + GameId,
                @"HKEY_LOCAL_MACHINE\SOFTWARE\GOG.com\Games\" + GameId,
                @"HKEY_CURRENT_USER\SOFTWARE\GOG.com\Games\" + GameId
            };
            foreach (string key in keys)
            {
                object v = Registry.GetValue(key, "path", null);
                if (v != null && !string.IsNullOrWhiteSpace(v.ToString())) found.Add(v.ToString());
            }

            foreach (string name in new[] { "Distant Worlds 2", "Distant Worlds - 2" })
            {
                for (char d = 'C'; d <= 'Z'; d++)
                {
                    found.Add(d + @":\GOG Games\" + name);
                    found.Add(d + @":\Program Files (x86)\GOG Galaxy\Games\" + name);
                }
            }
            return found;
        }
    }
}
