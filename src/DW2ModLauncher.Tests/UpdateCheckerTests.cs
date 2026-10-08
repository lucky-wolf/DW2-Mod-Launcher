using DW2ModLauncher.Core.Services.Updates;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class UpdateCheckerTests
    {
        private const string Json = "{\"tag_name\":\"v1.2.4\",\"body\":\"Notes\",\"assets\":["
            + "{\"name\":\"DW2ModLauncher-v1.2.4-linux-x64.tar.gz\",\"browser_download_url\":\"https://x/l.tar.gz\",\"size\":10},"
            + "{\"name\":\"DW2ModLauncher-v1.2.4-win-x64.zip\",\"browser_download_url\":\"https://x/w.zip\",\"size\":20}]}";

        [Theory]
        [InlineData("v1.2.4", "1.2.3", true)]
        [InlineData("v1.2.3", "1.2.3", false)]
        [InlineData("v1.2.3", "1.2.4", false)]
        [InlineData("v1.10.0", "1.9.9", true)]
        [InlineData("v1.2.4", "1.2.4-dev", false)]
        [InlineData("v1.2.5", "1.2.4-dev", true)]
        [InlineData("garbage", "1.2.4", false)]
        [InlineData("v1.2.4", "", false)]
        public void IsNewerComparesNumerically(string tag, string current, bool expected)
        {
            Assert.Equal(expected, UpdateChecker.IsNewer(tag, current));
        }

        [Fact]
        public void PicksThePackageForThisOs()
        {
            LauncherRelease r = UpdateChecker.ParseNewer(Json, "1.2.3", "-win-x64.zip");
            Assert.Equal("1.2.4", r.Version);
            Assert.Equal("https://x/w.zip", r.AssetUrl);
            Assert.Equal(20, r.AssetSize);
            Assert.Equal("Notes", r.Notes);
        }

        [Fact]
        public void NullWhenCurrentOrWhenNoPackageFits()
        {
            Assert.Null(UpdateChecker.ParseNewer(Json, "1.2.4", "-win-x64.zip"));
            Assert.Null(UpdateChecker.ParseNewer(Json, "1.2.3", "-osx.zip"));
        }
    }
}
