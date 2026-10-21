using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Linq;
using DW2ModLauncher.Core.Models;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>
    /// The author of a Workshop mod that states none (no "author" in its dw2modlauncher.json) is the Steam user who created the Workshop item.
    /// Steam's item details name that user only by id (<see cref="ModInfo.WorkshopCreator"/>); the handle comes from the user's public profile
    /// and is remembered by id in the launcher's settings. It is used for the Author column and, with the id, to tell mods by one author apart
    /// from mods by different ones. A mod that states an author keeps it. Blocking calls: run off the UI thread.
    /// </summary>
    public static class SteamAuthors
    {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };

        /// <summary>The user's handle from the public profile document (steamcommunity.com/profiles/id?xml=1). Throws when Steam cannot be reached or the profile gives no name (private, or gone).</summary>
        public static string FetchPersonaName(string steamId)
        {
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "https://steamcommunity.com/profiles/" + Uri.EscapeDataString(steamId) + "/?xml=1"))
            {
                request.Headers.UserAgent.ParseAdd("DW2ModLauncher/" + AppVersion.Current);
                using (HttpResponseMessage response = Http.Send(request))
                {
                    response.EnsureSuccessStatusCode();
                    string name = ParsePersonaName(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                    return name ?? throw new InvalidOperationException("Steam profile " + steamId + " names no user (the profile may be private)");
                }
            }
        }

        /// <summary>The handle in a profile document, or null when it has none.</summary>
        public static string ParsePersonaName(string profileXml)
        {
            string name = XDocument.Parse(profileXml).Root?.Element("steamID")?.Value?.Trim();
            return string.IsNullOrEmpty(name) ? null : name;
        }

        /// <summary>
        /// Looks up the handle of every creator in <paramref name="mods"/> that is missing from <paramref name="names"/> (id to handle), but only for mods
        /// that state no author of their own. Returns one line per lookup that failed; those creators stay unnamed and are tried again next time.
        /// </summary>
        public static List<string> Resolve(IEnumerable<ModInfo> mods, Dictionary<string, string> names, Func<string, string> fetch = null)
        {
            fetch = fetch ?? FetchPersonaName;
            List<string> failures = new List<string>();
            foreach (string id in mods.Where(NeedsHandle).Select(m => m.WorkshopCreator.Trim()).Distinct().Where(id => !names.ContainsKey(id)).ToList())
            {
                try { names[id] = fetch(id); }
                catch (Exception ex) when (ex is HttpRequestException || ex is InvalidOperationException || ex is System.Xml.XmlException || ex is TaskCanceledException)
                {
                    failures.Add("The Steam user " + id + " could not be named: " + ex.Message);
                }
            }
            return failures;
        }

        /// <summary>Gives every mod that states no author the handle of its Workshop creator, when it is known (marked by <see cref="ModInfo.AuthorFromSteam"/>).</summary>
        public static void Apply(IEnumerable<ModInfo> mods, Dictionary<string, string> names)
        {
            foreach (ModInfo mod in mods.Where(NeedsHandle))
            {
                if (names.TryGetValue(mod.WorkshopCreator.Trim(), out string handle)) { mod.Author = handle; mod.AuthorFromSteam = true; }
            }
        }

        private static bool NeedsHandle(ModInfo mod)
        {
            return mod != null && !string.IsNullOrWhiteSpace(mod.WorkshopCreator) && (string.IsNullOrWhiteSpace(mod.Author) || mod.AuthorFromSteam);
        }
    }
}
