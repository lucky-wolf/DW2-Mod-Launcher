using System;
using System.Threading;
using System.Threading.Tasks;
using DW2ModLauncher.Core.Services.Updates;

namespace DW2ModLauncher.Core.Services.Catalog
{
    /// <summary>Fetches the catalog's index.json (a public static file; no account or token).</summary>
    public static class CatalogClient
    {
        /// <summary>Where the owner's catalog repository publishes index.json (rebuilt daily).</summary>
        public const string DefaultUrl = "https://raw.githubusercontent.com/lucky-wolf/Distant-Worlds-2-Mod-Catalog/main/index.json";

        /// <summary>The parsed catalog. Throws on a network failure, an unreadable file or a bad entry; the caller reports it.</summary>
        public static async Task<CatalogIndex> FetchAsync(string url, CancellationToken token)
        {
            using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(20));
                string json = await UpdateChecker.Client.GetStringAsync(url, timeout.Token);
                return CatalogParser.Parse(json);
            }
        }
    }
}
