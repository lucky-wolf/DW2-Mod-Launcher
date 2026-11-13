using System.IO;
using DW2ModLauncher.Core.Services;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class GameLocatorTests
    {
        private static string MakeGame(string dir)
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, GameFolder.ExeName), "");
            return dir;
        }

        [Fact]
        public void IsGameRoot_NeedsTheExe()
        {
            string dir = Path.Combine(Path.GetTempPath(), "dw2ml-gl-" + Path.GetRandomFileName());
            try
            {
                Directory.CreateDirectory(dir);
                Assert.False(GameFolder.IsGameRoot(dir));
                Assert.False(GameFolder.IsGameRoot(""));
                Assert.False(GameFolder.IsGameRoot(null));
                MakeGame(dir);
                Assert.True(GameFolder.IsGameRoot(dir));
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }

        [Fact]
        public void FirstGameRoot_SkipsFoldersWithoutTheGame()
        {
            string root = Path.Combine(Path.GetTempPath(), "dw2ml-gl-" + Path.GetRandomFileName());
            try
            {
                string empty = Path.Combine(root, "empty");
                Directory.CreateDirectory(empty);
                string game = MakeGame(Path.Combine(root, "game"));

                Assert.Equal(game, GameLocator.FirstGameRoot(new[] { Path.Combine(root, "missing"), empty, game }));
                Assert.Equal("", GameLocator.FirstGameRoot(new[] { empty }));
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [Fact]
        public void Find_KeepsAValidSavedFolder()
        {
            string dir = Path.Combine(Path.GetTempPath(), "dw2ml-gl-" + Path.GetRandomFileName());
            try
            {
                MakeGame(dir);
                Assert.Equal(dir, GameLocator.Find(dir));
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }
    }
}
