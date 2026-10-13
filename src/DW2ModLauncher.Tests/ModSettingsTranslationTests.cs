using System;
using System.IO;
using DW2ModLauncher.Core.Services;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class ModSettingsTranslationTests : IDisposable
    {
        private readonly string dir = Path.Combine(Path.GetTempPath(), "dw2-test-" + Guid.NewGuid().ToString("N"));

        public ModSettingsTranslationTests() { Directory.CreateDirectory(dir); }
        public void Dispose() { Directory.Delete(dir, true); }

        private void Sidecar(string code, string json) => File.WriteAllText(Path.Combine(dir, "settings.schema." + code + ".json"), json);

        [Fact]
        public void NoSidecarOrNoLanguageMeansTheSchemasOwnText()
        {
            Assert.Same(ModSettingsTranslation.None, ModSettingsTranslation.Read(dir, "ja"));
            Sidecar("ja", "{}");
            Assert.Same(ModSettingsTranslation.None, ModSettingsTranslation.Read(dir, ""));
            Assert.Same(ModSettingsTranslation.None, ModSettingsTranslation.Read(null, "ja"));
            Assert.Equal("Seed", ModSettingsTranslation.None.Label("Seed", "Seed"));
        }

        [Fact]
        public void OnlyWhatIsTranslatedChangesAndTheRestKeepsTheSchemasText()
        {
            Sidecar("ja", @"{ ""fields"": { ""Seed"": { ""label"": ""シード"" } } }");

            ModSettingsTranslation t = ModSettingsTranslation.Read(dir, "ja");

            Assert.Equal("シード", t.Label("Seed", "Seed"));
            Assert.Equal("Random seed for everything", t.Description("Seed", "Random seed for everything"));
            Assert.Equal("Other", t.Label("Other", "Other"));
            Assert.Equal("Hubs", t.Heading("Hubs", "Hubs"));
        }

        [Fact]
        public void APlainStringTranslatesJustTheLabel()
        {
            Sidecar("ru", @"{ ""fields"": { ""Seed"": ""Зерно"" } }");

            ModSettingsTranslation t = ModSettingsTranslation.Read(dir, "ru");

            Assert.Equal("Зерно", t.Label("Seed", "Seed"));
            Assert.Equal("tip", t.Description("Seed", "tip"));
        }

        [Fact]
        public void GroupsDescriptionsAndOptionsAreTranslatedAndKeysIgnoreCase()
        {
            Sidecar("ja", @"{
                // comments and trailing commas are fine
                ""groups"": { ""Hubs"": ""ハブ"" },
                ""fields"": { ""hubsplayer"": { ""description"": ""説明"", ""options"": { ""normal"": ""通常"" }, } }
            }");

            ModSettingsTranslation t = ModSettingsTranslation.Read(dir, "ja");

            Assert.Equal("ハブ", t.Heading("HUBS", "Hubs"));
            Assert.Equal("説明", t.Description("HubsPlayer", "x"));
            Assert.Equal("通常", t.Option("HubsPlayer", "Normal"));
            Assert.Equal("Hard", t.Option("HubsPlayer", "Hard"));
        }

        [Fact]
        public void ASidecarForTheBaseLanguageIsUsedLikeAnyOther()
        {
            Sidecar("en", @"{ ""fields"": { ""Seed"": ""Random seed"" } }");

            Assert.Equal("Random seed", ModSettingsTranslation.Read(dir, "en").Label("Seed", "シード"));
        }

        [Theory]
        [InlineData("{ not json")]
        [InlineData("[]")]
        [InlineData(@"{ ""fields"": { ""Seed"": 5 } }")]
        public void ABrokenSidecarIsReportedNotIgnored(string json)
        {
            Sidecar("ja", json);

            Assert.Throws<InvalidDataException>(() => ModSettingsTranslation.Read(dir, "ja"));
        }
    }
}
