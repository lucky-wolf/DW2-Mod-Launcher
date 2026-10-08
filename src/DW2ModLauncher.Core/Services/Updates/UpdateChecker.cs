using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace DW2ModLauncher.Core.Services.Updates
{
    /// <summary>Asks GitHub for the latest launcher release and says whether it is newer than the running build.</summary>
    public static class UpdateChecker
    {
        public const string LatestReleaseApiUrl = "https://api.github.com/repos/lucky-wolf/DW2-Mod-Launcher/releases/latest";

        private static readonly HttpClient Http = CreateHttp();

        private static HttpClient CreateHttp()
        {
            // No overall timeout: the package download can legitimately take minutes. The API call sets its own.
            HttpClient client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("DW2ModLauncher/" + AppVersion.Current);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return client;
        }

        internal static HttpClient Client { get { return Http; } }

        /// <summary>The package name ending the release workflow uses for this OS (see .github/workflows/release.yml).</summary>
        public static string AssetSuffix { get { return OperatingSystem.IsWindows() ? "-win-x64.zip" : "-linux-x64.tar.gz"; } }

        /// <summary>The newer release, or null when the running build is current. Throws on network or parse failure.</summary>
        public static async Task<LauncherRelease> FindNewerAsync(string currentVersion, CancellationToken token)
        {
            using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                string json = await Http.GetStringAsync(LatestReleaseApiUrl, timeout.Token);
                return ParseNewer(json, currentVersion, AssetSuffix);
            }
        }

        /// <summary>Null when the release is not newer than <paramref name="currentVersion"/>, or has no package ending in <paramref name="assetSuffix"/>.</summary>
        public static LauncherRelease ParseNewer(string json, string currentVersion, string assetSuffix)
        {
            Dictionary<string, object> root = LooseJson.Parse(json) as Dictionary<string, object>;
            if (root == null) throw new FormatException("Unexpected GitHub response.");
            string tag = Text(root, "tag_name");
            if (!IsNewer(tag, currentVersion)) return null;

            object[] assets = root.TryGetValue("assets", out object a) ? a as object[] : null;
            if (assets == null) return null;
            foreach (object item in assets)
            {
                Dictionary<string, object> asset = item as Dictionary<string, object>;
                string name = asset == null ? null : Text(asset, "name");
                string url = asset == null ? null : Text(asset, "browser_download_url");
                if (name == null || url == null || !name.EndsWith(assetSuffix, StringComparison.OrdinalIgnoreCase)) continue;
                return new LauncherRelease
                {
                    Version = Normalize(tag),
                    Notes = Text(root, "body") ?? "",
                    AssetName = name,
                    AssetUrl = url,
                    AssetSize = asset.TryGetValue("size", out object size) && size is long bytes ? bytes : 0
                };
            }
            return null;
        }

        /// <summary>True if <paramref name="candidate"/> (a tag such as "v1.2.3") is a higher MAJOR.MINOR.PATCH than <paramref name="current"/>.</summary>
        public static bool IsNewer(string candidate, string current)
        {
            return TryParse(candidate, out Version c) && TryParse(current, out Version cur) && c > cur;
        }

        private static string Normalize(string tag)
        {
            return TryParse(tag, out Version v) ? v.ToString() : tag;
        }

        private static bool TryParse(string text, out Version version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(text)) return false;
            // A leading "v" and trailing "-dev" / "-alpha.0.7" / "+sha" all fall away.
            Match m = Regex.Match(text.Trim(), @"^v?(\d+(?:\.\d+){1,3})");
            return m.Success && Version.TryParse(m.Groups[1].Value, out version);
        }

        private static string Text(Dictionary<string, object> obj, string key)
        {
            return obj.TryGetValue(key, out object v) ? v as string : null;
        }
    }
}
