using System;
using System.IO;
using DW2ModLauncher.Core.Models;
using DW2ModLauncher.Core.Services;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class PathTokensTests
    {
        private const string LinuxGame = "/home/u/.steam/steamapps/common/Distant Worlds 2";

        [Fact]
        public void Expand_Dw2_IsTheGameFolder_AndZDriveForTheGameUnderProton()
        {
            Assert.Equal(@"Z:\home\u\.steam\steamapps\common\Distant Worlds 2\data\Logs", PathTokens.Expand(@"%DW2%\data\Logs", LinuxGame, forGame: true, hostIsWindows: false).Path);
            Assert.Equal(@"C:\Games\DW2\data\Logs", PathTokens.Expand(@"%DW2%\data\Logs", @"C:\Games\DW2\", forGame: true, hostIsWindows: true).Path);
        }

        [Fact]
        public void Expand_UnderProton_TheGameSeesThePrefixProfile()
        {
            Assert.Equal(@"C:\users\steamuser\AppData\Roaming\Foo", PathTokens.Expand(@"%appdata%\Foo", LinuxGame, forGame: true, hostIsWindows: false).Path);
            Assert.Equal(@"C:\users\steamuser\Documents\Foo", PathTokens.Expand(@"%DOCUMENTS%\Foo", LinuxGame, forGame: true, hostIsWindows: false).Path);
        }

        [Fact]
        public void Expand_UnknownToken_IsAnError_NotLeftAlone()
        {
            PathExpansion result = PathTokens.Expand(@"%MODS%\x", @"C:\Games\DW2", forGame: true, hostIsWindows: true);
            Assert.Equal(PathTokenProblem.UnknownToken, result.Problem);
            Assert.Equal("MODS", result.Detail);
        }

        [Fact]
        public void Expand_Dw2WithoutGameFolder_IsAnError()
        {
            Assert.Equal(PathTokenProblem.NoGameFolder, PathTokens.Expand(@"%DW2%\data", "", forGame: true, hostIsWindows: true).Problem);
        }

        [Fact]
        public void Expand_PlainPath_IsReturnedAsTyped()
        {
            Assert.Equal(@"D:\Logs\x", PathTokens.Expand(@"  D:\Logs\x ", null, forGame: true, hostIsWindows: true).Path);
        }

        [Fact]
        public void Collapse_WritesTheMostSpecificToken()
        {
            if (!OperatingSystem.IsWindows()) return;
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            Assert.Equal(@"%APPDATA%\Foo\Bar", PathTokens.Collapse(Path.Combine(appData, "Foo", "Bar"), @"C:\Games\DW2"));
            Assert.Equal(@"%DW2%\data\Logs", PathTokens.Collapse(@"C:\Games\DW2\data\Logs", @"C:\Games\DW2"));
            Assert.Equal(@"%DW2%", PathTokens.Collapse(@"c:\games\dw2", @"C:\Games\DW2"));
        }

        [Fact]
        public void Collapse_LeavesUnrelatedPathsAlone_AndRespectsFolderBoundaries()
        {
            if (!OperatingSystem.IsWindows()) return;
            Assert.Equal(@"D:\Elsewhere\x", PathTokens.Collapse(@"D:\Elsewhere\x", @"C:\Games\DW2"));
            Assert.Equal(@"C:\Games\DW2-old\x", PathTokens.Collapse(@"C:\Games\DW2-old\x", @"C:\Games\DW2"));
        }

        [Fact]
        public void IsValidPath_TokenPathsNeedOnlyAKnownName()
        {
            string game = Path.Combine(Path.GetTempPath(), "dw2-tokens-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(game, "data", "Logs"));
            try
            {
                Assert.True(ModSettingsValues.IsValidPath(ModSettingKind.Folder, @"%DW2%\data\Logs", game));
                Assert.True(ModSettingsValues.IsValidPath(ModSettingKind.Folder, @"%DW2%\data\Missing", game));
                Assert.False(ModSettingsValues.IsValidPath(ModSettingKind.Folder, Path.Combine(game, "Missing"), game));
                Assert.False(ModSettingsValues.IsValidPath(ModSettingKind.Folder, @"%NOPE%\data", game));
                Assert.True(ModSettingsValues.IsValidPath(ModSettingKind.Folder, @"%DW2%\data", null));
                Assert.True(ModSettingsValues.IsValidPath(ModSettingKind.Folder, "", null));
            }
            finally { Directory.Delete(game, true); }
        }
    }
}
