using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DW2ModLauncher.Core.Diagnostics;
using DW2ModLauncher.Core.Services.Updates;

namespace DW2ModLauncher.Core.Services.Catalog
{
    /// <summary>What a catalog load produced: the index, and when it came from the saved copy instead of the network, when that copy was saved and why the network failed.</summary>
    public class CatalogLoad
    {
        public CatalogIndex Index { get; set; }
        /// <summary>Null for a fresh fetch; else when the copy shown was saved (UTC).</summary>
        public DateTime? SavedUtc { get; set; }
        /// <summary>Why the fresh fetch failed; null for a fresh fetch.</summary>
        public string Problem { get; set; }
        public bool FromSavedCopy { get { return SavedUtc.HasValue; } }
    }

    /// <summary>Fetches the catalog's index.json (a public static file; no account or token).</summary>
    public static class CatalogClient
    {
        /// <summary>Where the owner's catalog repository publishes index.json (rebuilt daily).</summary>
        public const string DefaultUrl = "https://raw.githubusercontent.com/lucky-wolf/Distant-Worlds-2-Mod-Catalog/main/index.json";

        /// <summary>The address to use for a configured one: blank (never set) means <see cref="DefaultUrl"/>.</summary>
        public static string ResolveUrl(string configured)
        {
            return string.IsNullOrWhiteSpace(configured) ? DefaultUrl : configured.Trim();
        }

        /// <summary>The parsed catalog. Throws on a network failure, an unreadable file or a bad entry; the caller reports it.</summary>
        public static async Task<CatalogIndex> FetchAsync(string url, CancellationToken token)
        {
            return CatalogParser.Parse(await FetchTextAsync(url, token));
        }

        private static async Task<string> FetchTextAsync(string url, CancellationToken token)
        {
            using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(20));
                return await UpdateChecker.Client.GetStringAsync(url, timeout.Token);
            }
        }

        /// <summary>The file that keeps the last good copy of the catalog at <paramref name="url"/> (one per address, so a fork's copy never stands in for another's).</summary>
        public static string SavedCopyPath(string folder, string url)
        {
            byte[] hash;
            using (SHA256 sha = SHA256.Create()) hash = sha.ComputeHash(Encoding.UTF8.GetBytes(url));
            return Path.Combine(folder, "index-" + BitConverter.ToString(hash, 0, 6).Replace("-", "").ToLowerInvariant() + ".json");
        }

        /// <summary>
        /// Fetches the catalog and saves what was fetched (only a copy that parsed) in <paramref name="folder"/>. When the fetch fails, the
        /// last saved copy for this address is returned instead, marked as such with the reason, so the caller can say so; with no saved copy
        /// the original failure is thrown. A saved copy that no longer parses is logged and the original failure is thrown.
        /// </summary>
        public static async Task<CatalogLoad> LoadAsync(string url, string folder, CancellationToken token)
        {
            string path = SavedCopyPath(folder, url);
            try
            {
                string json = await FetchTextAsync(url, token);
                CatalogIndex index = CatalogParser.Parse(json);
                Save(path, json);
                return new CatalogLoad { Index = index };
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && token.IsCancellationRequested))
            {
                if (!File.Exists(path)) throw;
                Logger.LogException("Load mod catalog " + url, ex);
                try
                {
                    CatalogIndex saved = CatalogParser.Parse(File.ReadAllText(path, Encoding.UTF8));
                    return new CatalogLoad { Index = saved, SavedUtc = File.GetLastWriteTimeUtc(path), Problem = ex.Message };
                }
                catch (Exception copyProblem) when (copyProblem is FormatException || copyProblem is IOException || copyProblem is UnauthorizedAccessException || copyProblem is System.Text.Json.JsonException)
                {
                    Logger.LogException("Read saved mod catalog " + path, copyProblem);
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex).Throw(); // the fetch failure is what the player needs to hear about
                    throw;
                }
            }
        }

        private static void Save(string path, string json)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, json, new UTF8Encoding(false));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // The catalog itself loaded fine; only the offline copy is lost, and the log says so.
                Logger.LogException("Save mod catalog copy " + path, ex);
            }
        }
    }
}
