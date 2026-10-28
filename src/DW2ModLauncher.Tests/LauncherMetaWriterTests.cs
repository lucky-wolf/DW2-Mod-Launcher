using System;
using System.IO;
using System.Text.Json.Nodes;
using DW2ModLauncher.Core.Models;
using DW2ModLauncher.Core.Services;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class LauncherMetaWriterTests : IDisposable
    {
        private readonly string folder = Path.Combine(Path.GetTempPath(), "dw2ml-meta-" + Guid.NewGuid().ToString("N"));

        public LauncherMetaWriterTests() { Directory.CreateDirectory(folder); }

        public void Dispose() { Directory.Delete(folder, true); }

        private string FilePath { get { return Path.Combine(folder, LauncherMetaWriter.FileName); } }

        [Fact]
        public void SetCoordinatedWith_WritesTheListAndKeepsTheRestOfTheFile()
        {
            File.WriteAllText(FilePath, "{ \"author\": \"Me\", \"dependencies\": [ { \"mod\": \"XL\" } ] }");

            LauncherMetaWriter.SetCoordinatedWith(folder, new[] { "Other@1.2.3", " Plain ", "" });

            JsonObject root = JsonNode.Parse(File.ReadAllText(FilePath)).AsObject();
            Assert.Equal("Me", (string)root["author"]);
            Assert.Equal("XL", (string)root["dependencies"][0]["mod"]);
            Assert.Equal(new[] { "Other@1.2.3", "Plain" }, new[] { (string)root["hooksCoordinatedWith"][0], (string)root["hooksCoordinatedWith"][1] });
            Assert.DoesNotContain("\r", File.ReadAllText(FilePath));
        }

        [Fact]
        public void SetCoordinatedWith_EmptyListRemovesTheKey_AndCreatesNoFile()
        {
            LauncherMetaWriter.SetCoordinatedWith(folder, new string[0]);
            Assert.False(File.Exists(FilePath));

            File.WriteAllText(FilePath, "{ \"author\": \"Me\", \"HooksCoordinatedWith\": [ \"A\" ] }");
            LauncherMetaWriter.SetCoordinatedWith(folder, new string[0]);

            JsonObject root = JsonNode.Parse(File.ReadAllText(FilePath)).AsObject();
            Assert.Equal("Me", (string)root["author"]);
            Assert.False(root.ContainsKey("hooksCoordinatedWith") || root.ContainsKey("HooksCoordinatedWith"));
        }

        [Fact]
        public void SetCoordinatedWith_RefusesAFileThatIsNotAnObject()
        {
            File.WriteAllText(FilePath, "[1, 2]");
            Assert.Throws<InvalidDataException>(() => LauncherMetaWriter.SetCoordinatedWith(folder, new[] { "A" }));
            Assert.Equal("[1, 2]", File.ReadAllText(FilePath));
        }

        [Fact]
        public void CoordinationEntry_IsTheNamePinnedToTheVersion_AndSplitsBack()
        {
            ModInfo pinned = new ModInfo { Id = "x", DisplayName = "Other Mod", Version = " 1.2.3 " };
            ModInfo unversioned = new ModInfo { Id = "y", DisplayName = "No Version" };

            Assert.Equal("Other Mod@1.2.3", HookOverlapAnalyzer.CoordinationEntryFor(pinned));
            Assert.Equal("No Version", HookOverlapAnalyzer.CoordinationEntryFor(unversioned));
            Assert.Equal(("Other Mod", "1.2.3"), HookOverlapAnalyzer.SplitCoordinationEntry("Other Mod@1.2.3"));
            Assert.Equal(("No Version", (string)null), HookOverlapAnalyzer.SplitCoordinationEntry(" No Version "));
            Assert.True(HookOverlapAnalyzer.Coordinates(HookOverlapAnalyzer.CoordinationEntryFor(pinned), pinned, new[] { pinned }));
        }
    }
}
