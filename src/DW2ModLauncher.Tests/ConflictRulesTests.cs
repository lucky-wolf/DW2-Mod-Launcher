using DW2ModLauncher.Core.Services;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class ConflictRulesTests
    {
        [Theory]
        [InlineData("mod.json")]
        [InlineData("mods.json")]
        [InlineData("launcher.json")]
        [InlineData("README.txt")]
        [InlineData("sub\\folder\\README.md")]
        [InlineData("install.bat")]
        [InlineData("tool.exe")]
        [InlineData("notes.pdf")]
        [InlineData("backup.launcher_backup")]
        public void IsIgnored_ReturnsTrue_ForNonGameData(string path)
        {
            Assert.True(ConflictRules.IsIgnored(path));
        }

        [Theory]
        [InlineData("Data\\Ships.xml")]
        [InlineData("Assets\\texture.dds")]
        [InlineData("Plugins\\SomeMod.dll")]
        public void IsIgnored_ReturnsFalse_ForGameData(string path)
        {
            Assert.False(ConflictRules.IsIgnored(path));
        }

        [Fact]
        public void IsIgnored_ReturnsTrue_ForEmptyPath()
        {
            Assert.True(ConflictRules.IsIgnored(""));
            Assert.True(ConflictRules.IsIgnored(null));
        }
    }
}
