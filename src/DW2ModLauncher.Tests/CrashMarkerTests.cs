using System;
using System.IO;
using DW2ModLauncher.Core.Models;
using DW2ModLauncher.Core.Services;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class CrashMarkerTests
    {
        [Fact]
        public void ClearDeletesTheMarkerOnly()
        {
            string game = Path.Combine(Path.GetTempPath(), "dw2-crash-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(game, "data"));
            try
            {
                Assert.False(CrashMarker.Clear(game));
                File.WriteAllText(CrashMarker.PathFor(game), "Session Started");
                File.WriteAllText(Path.Combine(game, "data", "other.txt"), "x");
                Assert.True(CrashMarker.Clear(game));
                Assert.False(File.Exists(CrashMarker.PathFor(game)));
                Assert.True(File.Exists(Path.Combine(game, "data", "other.txt")));
            }
            finally { Directory.Delete(game, true); }
        }

        [Fact]
        public void SettingIsOffByDefault()
        {
            Assert.False(new LauncherSettings().ClearCrashIndication);
        }
    }
}
