using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DW2ModLauncher.Core.Models;
using DW2ModLauncher.Core.Services;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class TranslationPacksTests : IDisposable
    {
        private readonly string dir = Path.Combine(Path.GetTempPath(), "dw2-packs-" + Guid.NewGuid().ToString("N"));

        public TranslationPacksTests() { Directory.CreateDirectory(dir); }
        public void Dispose() { Directory.Delete(dir, true); }

        private ModInfo Mod(string id, params string[] files)
        {
            string folder = Path.Combine(dir, id);
            Directory.CreateDirectory(folder);
            foreach (string relative in files)
            {
                string full = Path.Combine(folder, relative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(full));
                File.WriteAllText(full, "{}");
            }
            return new ModInfo { Id = id, DisplayName = id, Folder = folder, ContentRoot = folder };
        }

        private static string Names(IEnumerable<string> files, string root) => string.Join("|", files.Select(f => Path.GetRelativePath(root, f).Replace(Path.DirectorySeparatorChar, '/')));

        [Fact]
        public void APackFindsItsFileForTheTargetInLoadOrder()
        {
            ModInfo target = Mod("DW2Troops");
            ModInfo first = Mod("PackA", "translations/ru/DW2Troops/settings.schema.json");
            ModInfo second = Mod("PackB", "translations/ru/DW2Troops/settings.schema.json", "translations/ru/DW2Troops/status.json");

            List<string> files = TranslationPacks.FilesFor(new[] { first, target, second }, target, TranslationPacks.SettingsSchema, "ru");

            Assert.Equal("PackA/translations/ru/DW2Troops/settings.schema.json|PackB/translations/ru/DW2Troops/settings.schema.json", Names(files, dir));
            Assert.Equal("PackB/translations/ru/DW2Troops/status.json", Names(TranslationPacks.FilesFor(new[] { first, second }, target, TranslationPacks.Status, "ru"), dir));
        }

        [Fact]
        public void TheTargetIsMatchedLikeADependencyIgnoringCase()
        {
            ModInfo target = Mod("DW2Troops");
            target.DisplayName = "Troop Logistics";
            ModInfo pack = Mod("Pack", "translations/RU/troop logistics/settings.schema.json", "translations/ru/OTHERMOD/settings.schema.json");

            Assert.Single(TranslationPacks.FilesFor(new[] { pack }, target, TranslationPacks.SettingsSchema, "ru"));
            Assert.Empty(TranslationPacks.FilesFor(new[] { pack }, Mod("Unrelated"), TranslationPacks.SettingsSchema, "ru"));
        }

        [Fact]
        public void ABaseLanguageComesBeforeTheExactOne()
        {
            ModInfo target = Mod("DW2Troops");
            ModInfo pack = Mod("Pack", "translations/pt-BR/DW2Troops/settings.schema.json", "translations/pt/DW2Troops/settings.schema.json", "translations/ja/DW2Troops/settings.schema.json");

            Assert.Equal("Pack/translations/pt/DW2Troops/settings.schema.json|Pack/translations/pt-BR/DW2Troops/settings.schema.json",
                Names(TranslationPacks.FilesFor(new[] { pack }, target, TranslationPacks.SettingsSchema, "pt-BR"), dir));
        }

        [Fact]
        public void NothingIsFoundForAModWithoutTranslationsOrABlankLanguage()
        {
            ModInfo target = Mod("DW2Troops");
            ModInfo plain = Mod("Plain");
            ModInfo pack = Mod("Pack", "translations/ru/DW2Troops/settings.schema.json");

            Assert.Empty(TranslationPacks.FilesFor(new[] { plain }, target, TranslationPacks.SettingsSchema, "ru"));
            Assert.Empty(TranslationPacks.FilesFor(new[] { pack }, target, TranslationPacks.SettingsSchema, ""));
            Assert.Empty(TranslationPacks.FilesFor(new[] { pack }, target, TranslationPacks.SettingsSchema, "fr"));
            Assert.Empty(TranslationPacks.FilesFor(null, target, TranslationPacks.SettingsSchema, "ru"));
        }

        private static void Write(string path, string json)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, json);
        }

        [Fact]
        public void SettingsDialog_PackFilesGoOverTheOwnSidecarKeyByKey()
        {
            string own = Path.Combine(dir, "Own");
            Write(Path.Combine(own, "settings.schema.ru.json"), @"{ ""fields"": { ""Seed"": { ""label"": ""Зерно"", ""description"": ""Свой"" }, ""Mode"": ""Режим"" } }");
            string first = Path.Combine(dir, "first.json");
            string second = Path.Combine(dir, "second.json");
            Write(first, @"{ ""fields"": { ""Seed"": { ""label"": ""Сид"", ""options"": { ""A"": ""а"" } } } }");
            Write(second, @"{ ""fields"": { ""Seed"": { ""options"": { ""B"": ""б"" } } }, ""groups"": { ""G"": ""Группа"" } }");

            ModSettingsTranslation t = ModSettingsTranslation.Read(own, "ru", new[] { first, second });

            Assert.Equal("Сид", t.Label("Seed", "Seed"));          // the first pack overrides the sidecar's label
            Assert.Equal("Свой", t.Description("Seed", "Seed"));   // a part no pack gives stays as the sidecar had it
            Assert.Equal("а", t.Option("Seed", "A"));
            Assert.Equal("б", t.Option("Seed", "B"));
            Assert.Equal("Режим", t.Label("Mode", "Mode"));
            Assert.Equal("Группа", t.Heading("G", "G"));
        }

        [Fact]
        public void SettingsDialog_PacksTranslateAModWithNoSidecarAtAll()
        {
            string pack = Path.Combine(dir, "pack.json");
            Write(pack, @"{ ""fields"": { ""Seed"": ""Сид"" } }");

            Assert.Equal("Сид", ModSettingsTranslation.Read(Path.Combine(dir, "NoSidecar"), "ru", new[] { pack }).Label("Seed", "Seed"));
        }

        [Fact]
        public void SettingsDialog_ABrokenPackFileIsReportedByName()
        {
            string pack = Path.Combine(dir, "broken.json");
            Write(pack, "{ not json");

            InvalidDataException ex = Assert.Throws<InvalidDataException>(() => ModSettingsTranslation.Read(dir, "ru", new[] { pack }));
            Assert.Contains("broken.json", ex.Message);
        }

        [Fact]
        public void Manifest_ListsThePacksStatusFilesInTheModsOptions()
        {
            string dll = Path.Combine(dir, "Target", "Target.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(dll));
            File.Copy(typeof(DW2ModLauncher.Tests.Fixtures.Entry).Assembly.Location, dll);
            ModInfo target = new ModInfo { Id = "Target", DisplayName = "Target", Folder = Path.GetDirectoryName(dll), ContentRoot = Path.GetDirectoryName(dll) };
            ModInfo pack = Mod("Pack", "translations/ru/Target/status.json");

            LoaderManifest manifest = LoaderManifestBuilder.Build(new List<ModInfo> { target, pack }, "ru");

            Assert.Single(manifest.Entries);
            System.Text.Json.Nodes.JsonNode options = System.Text.Json.Nodes.JsonNode.Parse(manifest.Entries[0].SettingsJson);
            Assert.Equal("ru", options["Language"].GetValue<string>());
            Assert.Equal(GamePaths.ToGameVisiblePath(Path.Combine(pack.Folder, "translations", "ru", "Target", "status.json")), options[LoaderManifestBuilder.TranslationFilesKey][0].GetValue<string>());
        }
    }
}
