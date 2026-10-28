using DW2ModLauncher.Core.Services;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class ModMusicTests
    {
        [Theory]
        [InlineData("music\\Action\\High\\track.mp3", true)]
        [InlineData("Music/Peaceful/Low/track.OGG", true)]
        [InlineData("music\\Action\\track.mp3", false)]
        [InlineData("music\\track.wav", false)]
        public void IsMusicFile_SaysWhetherTheFileIsInALeveledFolder(string path, bool leveled)
        {
            Assert.True(ModMusic.IsMusicFile(path, out bool actual));
            Assert.Equal(leveled, actual);
        }

        [Theory]
        [InlineData("")]
        [InlineData("music\\Action\\High\\readme.txt")]
        [InlineData("music\\Action\\High\\cover.jpg")]
        [InlineData("other\\track.mp3")]
        [InlineData("track.mp3")]
        public void IsMusicFile_IsFalseForOtherFiles(string path)
        {
            Assert.False(ModMusic.IsMusicFile(path, out _));
        }
    }
}
