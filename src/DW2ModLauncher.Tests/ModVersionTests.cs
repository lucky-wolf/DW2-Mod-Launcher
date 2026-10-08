using DW2ModLauncher.Core.Services.Publishing;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class ModVersionTests
    {
        [Theory]
        [InlineData("1.35.8", "1.35.9")]
        [InlineData("1.0.0", "1.0.1")]
        [InlineData("2.0", "2.0.1")]
        [InlineData("3", "3.0.1")]
        [InlineData("1.2.9", "1.2.10")]
        [InlineData("1.2.3.4", "1.2.4.0")]
        [InlineData("", "")]
        [InlineData("beta", "beta")]
        [InlineData("1.0-rc1", "1.0-rc1")]
        public void BumpPatch(string input, string expected)
        {
            Assert.Equal(expected, ModVersion.BumpPatch(input));
        }

        [Theory]
        [InlineData("1.35.8", VersionBumpLevel.Minor, "1.36.0")]
        [InlineData("1.35.8", VersionBumpLevel.Major, "2.0.0")]
        [InlineData("2.0", VersionBumpLevel.Minor, "2.1.0")]
        [InlineData("1.2.3.4", VersionBumpLevel.Minor, "1.3.0.0")]
        [InlineData("1.0-rc1", VersionBumpLevel.Major, "1.0-rc1")]
        public void BumpLevels(string input, VersionBumpLevel level, string expected)
        {
            Assert.Equal(expected, ModVersion.Bump(input, level));
        }

        [Theory]
        [InlineData("1.35.8", true, 1, 35, 8)]
        [InlineData(" 0.0.0 ", true, 0, 0, 0)]
        [InlineData("1.35", false, 0, 0, 0)]
        [InlineData("1.2.3.4", false, 0, 0, 0)]
        [InlineData("1.2.3-beta", false, 0, 0, 0)]
        [InlineData("", false, 0, 0, 0)]
        public void TryParseSemver(string input, bool ok, int major, int minor, int patch)
        {
            Assert.Equal(ok, ModVersion.TryParseSemver(input, out int a, out int b, out int c));
            Assert.Equal((major, minor, patch), (a, b, c));
        }

        [Theory]
        [InlineData("minor", VersionBumpLevel.Minor)]
        [InlineData("MAJOR", VersionBumpLevel.Major)]
        [InlineData("patch", VersionBumpLevel.Patch)]
        [InlineData("bogus", VersionBumpLevel.Patch)]
        [InlineData(null, VersionBumpLevel.Patch)]
        public void ParseLevel(string input, VersionBumpLevel expected)
        {
            Assert.Equal(expected, ModVersion.ParseLevel(input));
        }
    }
}
