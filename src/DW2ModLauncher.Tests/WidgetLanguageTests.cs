extern alias loader;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using DW2ModLauncher.Core.Models;
using DW2ModLauncher.Core.Services;
using loader::DW2ModLauncher.Loader;
using Xunit;

namespace DW2ModLauncher.Tests
{
    [Collection("StatusStrings")]
    public class WidgetLanguageTests : IDisposable
    {
        private readonly string dir = Path.Combine(Path.GetTempPath(), "dw2-test-" + Guid.NewGuid().ToString("N"));

        public WidgetLanguageTests() { Directory.CreateDirectory(dir); }
        public void Dispose() { StatusStrings.Use(null); Directory.Delete(dir, true); }

        private ModInfo Mod(string id, string language = null)
        {
            string folder = Path.Combine(dir, id);
            Directory.CreateDirectory(folder);
            if (language != null) File.WriteAllText(Path.Combine(folder, "dw2modlauncher.json"), "{ \"language\": \"" + language + "\" }");
            return new ModInfo { Id = id, DisplayName = id, Folder = folder, ContentRoot = folder, ActiveToken = id };
        }

        [Fact]
        public void BuiltInEnglishEqualsTheWidgetKeysOfEnJson()
        {
            string path = Path.Combine(Localization.LanguagesDirectory, "en.json");
            Dictionary<string, string> en = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
            Dictionary<string, string> widget = en.Where(kv => kv.Key.StartsWith(WidgetLanguage.KeyPrefix, StringComparison.Ordinal)).ToDictionary(kv => kv.Key, kv => kv.Value);

            Assert.Equal(widget.OrderBy(kv => kv.Key), StatusStrings.English.OrderBy(kv => kv.Key));
        }

        [Fact]
        public void EveryPackHasEveryWidgetKeyWithTheSamePlaceholders()
        {
            foreach (string code in Localization.AvailableLanguageCodes().Where(c => c != "en"))
            {
                foreach (KeyValuePair<string, string> kv in StatusStrings.English)
                {
                    string text = Localization.Get(code, kv.Key);
                    Assert.True(text != kv.Key, code + " lacks " + kv.Key);
                    Assert.Equal(Placeholders(kv.Value), Placeholders(text));
                }
            }
        }

        private static string Placeholders(string text) =>
            string.Join(",", System.Text.RegularExpressions.Regex.Matches(text, @"\{\d+\}").Select(m => m.Value).OrderBy(x => x));

        [Fact]
        public void TheLastEnabledModThatDeclaresALanguageWins()
        {
            var settings = new LauncherSettings();
            string chosen = WidgetLanguage.Choose(new[] { Mod("a", "ja"), Mod("b"), Mod("c", "ru"), Mod("d") }, settings, out string unknown);

            Assert.Equal("ru", chosen);
            Assert.Null(unknown);
        }

        [Fact]
        public void NoDeclaredLanguageMeansEnglishWhateverTheLauncherSpeaks()
        {
            var settings = new LauncherSettings { Language = "ja" };

            Assert.Equal("en", WidgetLanguage.Choose(new[] { Mod("a") }, settings, out _));
        }

        [Fact]
        public void ALanguageWithoutAPackIsReportedAndStaysEnglish()
        {
            string chosen = WidgetLanguage.Choose(new[] { Mod("a", "xx") }, new LauncherSettings(), out string unknown);

            Assert.Equal("en", chosen);
            Assert.Equal("xx", unknown);
        }

        [Fact]
        public void TheLauncherLanguageSettingIgnoresTheMods()
        {
            var settings = new LauncherSettings { Language = "ja", WidgetLanguageFromMods = false };

            Assert.Equal("ja", WidgetLanguage.Choose(new[] { Mod("a", "ru") }, settings, out string unknown));
            Assert.Null(unknown);
        }

        [Fact]
        public void EnglishSendsNoStringsAndOtherLanguagesSendEveryKey()
        {
            Assert.Empty(WidgetLanguage.Strings("en"));
            Assert.Equal(StatusStrings.English.Keys.OrderBy(k => k), WidgetLanguage.Strings("ru").Keys.OrderBy(k => k));
        }

        [Fact]
        public void TheLoaderUsesTheManifestTableAndShowsAMissingKeyByName()
        {
            StatusStrings.Use(new Dictionary<string, string> { ["WidgetStateLoaded"] = "geladen" });

            Assert.Equal("geladen", StatusStrings.Get("WidgetStateLoaded"));
            Assert.Equal("WidgetStateError", StatusStrings.Get("WidgetStateError"));

            StatusStrings.Use(new Dictionary<string, string>());
            Assert.Equal("loaded", StatusStrings.Get("WidgetStateLoaded"));
        }

        [Fact]
        public void StampingALanguageKeepsTheRestOfTheFile()
        {
            ModInfo mod = Mod("t");
            File.WriteAllText(Path.Combine(mod.Folder, "dw2modlauncher.json"), "{ \"font\": \"RussianFont\" }");

            LauncherMetaWriter.StampLanguage(mod.Folder, "ru", true);

            LauncherMeta meta = LauncherMetaReader.Read(mod);
            Assert.Equal("ru", meta.language);
            Assert.Equal("RussianFont", meta.font);
        }

        [Fact]
        public void ATypedLanguageNameIsMatchedToAPackByCodeOrDisplayName()
        {
            Assert.Equal("ru", Localization.CodeFor(" RU "));
            Assert.Equal("ja", Localization.CodeFor(Localization.DisplayNameFor("ja")));
            Assert.Null(Localization.CodeFor("French"));
            Assert.Null(Localization.CodeFor(null));
        }
    }
}
