using System;
using System.IO;
using System.Text.Json.Nodes;
using DW2ModLauncher.Core.Services;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class LaunchPreparationTests : IDisposable
    {
        private readonly string dir = Path.Combine(Path.GetTempPath(), "dw2-prepare-" + Guid.NewGuid().ToString("N"));

        public LaunchPreparationTests() { Directory.CreateDirectory(dir); }
        public void Dispose() { try { Directory.Delete(dir, true); } catch (IOException) { } }

        [Fact]
        public void NotAGameFolderIsAnErrorInTheResultFile_NotAThrow()
        {
            string notGame = Path.Combine(dir, "nothing");
            Directory.CreateDirectory(notGame);
            string output = Path.Combine(dir, "out");

            int exit = LaunchPreparation.Run(new[] { LaunchPreparation.Flag, "--game-root", notGame, "--out", output });

            Assert.Equal(1, exit);
            JsonNode result = JsonNode.Parse(File.ReadAllText(Path.Combine(output, LaunchPreparation.ResultFile)));
            Assert.Contains("is not a Distant Worlds 2 folder", (string)result["Error"]);
            Assert.Null(result["Arguments"]);
        }

        [Fact]
        public void MissingArgumentsAreRefusedBeforeAnythingIsWritten()
        {
            Assert.Equal(1, LaunchPreparation.Run(new[] { LaunchPreparation.Flag, "--game-root", dir }));
            Assert.Equal(1, LaunchPreparation.Run(new[] { LaunchPreparation.Flag, "--game-root", dir, "--out", dir, "--bogus" }));
        }
    }
}
