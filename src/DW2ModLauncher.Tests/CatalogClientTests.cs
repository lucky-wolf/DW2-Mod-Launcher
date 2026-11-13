using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DW2ModLauncher.Core.Models;
using DW2ModLauncher.Core.Services;
using DW2ModLauncher.Core.Services.Catalog;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class CatalogClientTests
    {
        [Fact]
        public void ResolveUrl_BlankMeansTheBuiltInAddress_AndAnythingElseIsKept()
        {
            Assert.Equal(CatalogClient.DefaultUrl, CatalogClient.ResolveUrl(null));
            Assert.Equal(CatalogClient.DefaultUrl, CatalogClient.ResolveUrl("  "));
            Assert.Equal("https://example.com/index.json", CatalogClient.ResolveUrl(" https://example.com/index.json "));
        }

        [Fact]
        public void Settings_DefaultToTheCatalogOn_AndTheBuiltInAddress()
        {
            LauncherSettings settings = new LauncherSettings();

            Assert.True(settings.CatalogEnabled);
            Assert.Null(settings.CatalogUrl);
        }

        [Fact]
        public void Cache_IsNotTheSettingsFolder()
        {
            Assert.NotEqual(UserDataRoot.Get(), UserDataRoot.GetCache());
        }

        [Fact]
        public void SavedCopyPath_IsPerAddress()
        {
            string a = CatalogClient.SavedCopyPath("/data", "https://example.com/a.json");

            Assert.Equal(a, CatalogClient.SavedCopyPath("/data", "https://example.com/a.json"));
            Assert.NotEqual(a, CatalogClient.SavedCopyPath("/data", "https://example.com/b.json"));
        }

        [Fact]
        public async Task Load_WhenTheNetworkFails_ShowsTheSavedCopy_AndSaysWhy()
        {
            string folder = Path.Combine(Path.GetTempPath(), "dw2-catalogcopy-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                // An address that cannot be reached: nothing listens on port 1 of the loopback.
                string url = "https://127.0.0.1:1/index.json";
                string json = "{\"schema\":1,\"generated\":\"2026-11-12T20:00:00Z\",\"mods\":[{\"id\":\"A\",\"uid\":\"6f1c2a3e-8b4d-4e5f-9a60-7b1d2c3e4f50\",\"name\":\"A\",\"kinds\":[\"xml\"],\"version\":\"1.0.0\",\"author\":\"x\","
                    + "\"size\":1,\"sha256\":\"" + new string('a', 64) + "\",\"downloadUrl\":\"https://example.com/a.zip\",\"publishedAt\":\"2026-11-12T15:42:16Z\"}]}";
                File.WriteAllText(CatalogClient.SavedCopyPath(folder, url), json);

                CatalogLoad load = await CatalogClient.LoadAsync(url, folder, CancellationToken.None);

                Assert.True(load.FromSavedCopy);
                Assert.NotEmpty(load.Problem);
                Assert.Equal("A", Assert.Single(load.Index.Entries).Name);
            }
            finally { Directory.Delete(folder, true); }
        }

        [Fact]
        public async Task Load_WhenTheNetworkFails_WithNoSavedCopy_Throws()
        {
            string folder = Path.Combine(Path.GetTempPath(), "dw2-catalogcopy-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                await Assert.ThrowsAnyAsync<Exception>(() => CatalogClient.LoadAsync("https://127.0.0.1:1/index.json", folder, CancellationToken.None));
            }
            finally { Directory.Delete(folder, true); }
        }
    }
}
