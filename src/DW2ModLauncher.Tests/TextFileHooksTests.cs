extern alias loader;
using loader::DW2ModLauncher.Loader;
using Xunit;

namespace DW2ModLauncher.Tests
{
    // How the loader recognises the text files it may replace. The game names them by real path or relative to its data folder, and under
    // Proton/Wine its own paths are Z:\... ones; these run on every platform, so the Linux/Proton shapes are covered without Linux.
    public class TextFileHooksTests
    {
        [Theory]
        [InlineData(@"C:\Program Files (x86)\Steam\steamapps\common\Distant Worlds 2\data\Hints.txt", "Hints.txt")]
        [InlineData(@"Z:\home\deck\.steam\steam\steamapps\common\Distant Worlds 2\data\Hints.txt", "Hints.txt")]
        [InlineData(@"Z:\home\deck\.steam\steam\steamapps\common\Distant Worlds 2\data\dialog\zenox.txt", "dialog/zenox.txt")]
        [InlineData("/home/deck/.steam/steam/steamapps/common/Distant Worlds 2/data/Galactopedia/GameConcepts/Alliances.txt", "Galactopedia/GameConcepts/Alliances.txt")]
        [InlineData("/home/deck/steam/Distant Worlds 2/DATA/hints.txt", "hints.txt")]
        [InlineData("data/Hints.txt", "Hints.txt")]
        public void DataRelative_FindsThePathBelowTheDataFolder_ForRealPaths(string path, string expected)
        {
            Assert.Equal(expected, TextFileHooks.DataRelative(path, false));
        }

        [Theory]
        [InlineData("Hints.txt", "Hints.txt")]
        [InlineData("dialog/zenox.txt", "dialog/zenox.txt")]
        [InlineData(@"dialog\zenox.txt", "dialog/zenox.txt")]
        [InlineData("Galactopedia/GameScreens/Map.txt", "Galactopedia/GameScreens/Map.txt")]
        public void DataRelative_AcceptsProviderUrls_OnlyWhenRelativeToData(string url, string expected)
        {
            Assert.Equal(expected, TextFileHooks.DataRelative(url, true));
            Assert.Null(TextFileHooks.DataRelative(url, false));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("Hints.xml")]
        [InlineData(@"C:\Games\Distant Worlds 2\data\GameText.txt")]
        [InlineData(@"C:\Users\x\notes.txt")]
        [InlineData(@"Z:\home\deck\Hints.txt")]
        [InlineData("/home/deck/Hints.txt")]
        [InlineData(@"C:\other\Hints.txt")]
        public void DataRelative_RejectsEverythingElse(string path)
        {
            Assert.Null(TextFileHooks.DataRelative(path, false));
        }
    }
}
