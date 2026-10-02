using DW2ModLauncher.Core.Services;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class SteamLocatorTests
    {
        [Fact]
        public void ParseLibraryPaths_ReadsLinuxAndEscapedWindowsPaths()
        {
            string vdf = "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"/home/u/.steam/steam\"\n\t}\n\t\"1\"\n\t{\n\t\t\"path\"\t\t\"D:\\\\SteamLibrary\"\n\t}\n}";

            var paths = SteamLocator.ParseLibraryPaths(vdf);

            Assert.Equal(new[] { "/home/u/.steam/steam", @"D:\SteamLibrary" }, paths);
        }
    }
}
