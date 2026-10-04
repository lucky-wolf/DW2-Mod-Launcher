using DW2ModLauncher.Core.Services;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class ConflictRulesTests
    {
        [Theory]
        [InlineData("mod.json")]
        [InlineData("mods.json")]
        [InlineData("dw2modlauncher.json")]
        [InlineData("Assets\\texture.dds")]
        [InlineData("Plugins\\SomeMod.dll")]
        [InlineData("GameText_XL.txt")]
        [InlineData("sub\\Hints.txt")]
        [InlineData("description.txt")]
        [InlineData("other\\track.mp3")]
        [InlineData("music\\track.wav")]
        [InlineData("XL.bundle")]
        [InlineData("settings.json")]
        [InlineData("Settings.schema.json")]
        [InlineData("sub\\settings.json")]
        [InlineData("other.schema.json")]
        [InlineData(".gitignore")]
        [InlineData(".git\\config")]
        [InlineData("Thumbs.db")]
        [InlineData("CREDITS.txt")]
        [InlineData("README.txt")]
        [InlineData("sub\\folder\\README.md")]
        [InlineData("sub\\Description.txt")]
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
        [InlineData("GameText.txt")]
        [InlineData("hints.txt")]
        [InlineData("SystemNames.txt")]
        [InlineData("Dialog\\human.txt")]
        [InlineData("galactopedia\\sub\\Entry.txt")]
        [InlineData("music\\Action\\High\\track.mp3")]
        [InlineData("art\\Sprites.atlas")]
        [InlineData("policy\\Policies.xml")]
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
