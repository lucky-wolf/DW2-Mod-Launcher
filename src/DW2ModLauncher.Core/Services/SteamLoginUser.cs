using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>One account in Steam's config/loginusers.vdf.</summary>
    public sealed class SteamLoginAccount
    {
        public string SteamId { get; set; }
        public string PersonaName { get; set; }
        public bool MostRecent { get; set; }
        public long Timestamp { get; set; }
    }

    /// <summary>
    /// Who is logged in to Steam on this machine, read from the Steam client's own config/loginusers.vdf (no network, and Steam need not be running).
    /// The account that logged in last is the one the launcher's Author name defaults to.
    /// </summary>
    public static class SteamLoginUser
    {
        /// <summary>
        /// The most recently logged-in account in any Steam install on this machine, or null when there is none (no Steam, or nobody has logged in).
        /// Throws when a loginusers.vdf exists but cannot be read.
        /// </summary>
        public static SteamLoginAccount FindMostRecent()
        {
            List<SteamLoginAccount> all = new List<SteamLoginAccount>();
            foreach (string root in SteamLocator.FindSteamRoots())
            {
                string vdf = Path.Combine(root, "config", "loginusers.vdf");
                if (File.Exists(vdf)) all.AddRange(Parse(File.ReadAllText(vdf)));
            }
            return PickMostRecent(all);
        }

        /// <summary>The account flagged MostRecent, else the one with the newest timestamp; null for an empty list.</summary>
        public static SteamLoginAccount PickMostRecent(IEnumerable<SteamLoginAccount> accounts)
        {
            return accounts.Where(a => !string.IsNullOrWhiteSpace(a.PersonaName)).OrderByDescending(a => a.MostRecent).ThenByDescending(a => a.Timestamp).FirstOrDefault();
        }

        /// <summary>The accounts in a loginusers.vdf: a "users" block holding one block per Steam id, each with PersonaName, MostRecent and timestamp.</summary>
        public static List<SteamLoginAccount> Parse(string vdfText)
        {
            List<SteamLoginAccount> accounts = new List<SteamLoginAccount>();
            foreach (Match block in Regex.Matches(vdfText ?? "", "\"(\\d{5,})\"\\s*\\{([^{}]*)\\}"))
            {
                string body = block.Groups[2].Value;
                accounts.Add(new SteamLoginAccount
                {
                    SteamId = block.Groups[1].Value,
                    PersonaName = Field(body, "PersonaName"),
                    MostRecent = Field(body, "MostRecent") == "1",
                    Timestamp = long.TryParse(Field(body, "timestamp"), NumberStyles.None, CultureInfo.InvariantCulture, out long t) ? t : 0
                });
            }
            return accounts;
        }

        private static string Field(string body, string key)
        {
            Match m = Regex.Match(body, "\"" + key + "\"\\s+\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value.Replace("\\\"", "\"").Replace("\\\\", "\\") : null;
        }
    }
}
