using DW2ModLauncher.Core.Services;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class GamePathsTests
    {
        [Fact]
        public void ToGameVisiblePath_OnLinux_MapsRootToZDrive()
        {
            Assert.Equal(@"Z:\home\u\mods\a.dll", GamePaths.ToGameVisiblePath("/home/u/mods/a.dll", false));
        }

        [Fact]
        public void ToGameVisiblePath_OnWindows_LeavesPathAlone()
        {
            Assert.Equal(@"C:\mods\a.dll", GamePaths.ToGameVisiblePath(@"C:\mods\a.dll", true));
        }

        [Fact]
        public void GameLauncher_OnLinux_GoesThroughSteam()
        {
            var psi = GameLauncher.BuildStartInfo("/games/dw2", "--low-level-inject x", false);
            Assert.Equal("steam", psi.FileName);
            Assert.Equal("-applaunch 1531540 --low-level-inject x", psi.Arguments);
        }

        [Fact]
        public void GameLauncher_OnWindows_RunsTheExe()
        {
            var psi = GameLauncher.BuildStartInfo(@"C:\g", "--x", true);
            Assert.EndsWith("DistantWorlds2.exe", psi.FileName);
            Assert.Equal("--x", psi.Arguments);
        }
    }
}
