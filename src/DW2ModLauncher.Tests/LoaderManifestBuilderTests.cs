using System;
using System.Collections.Generic;
using System.IO;
using DW2ModLauncher.Core.Models;
using DW2ModLauncher.Core.Services;
using Xunit;

namespace DW2ModLauncher.Tests.Fixtures
{
    // A conventional injection entry point, so this test assembly itself counts as an injection DLL.
    public static class Entry
    {
        public static void Init() { }
    }
}

namespace DW2ModLauncher.Tests
{
    public class LoaderManifestBuilderTests
    {
        private static readonly string TestDll = typeof(DW2ModLauncher.Tests.Fixtures.Entry).Assembly.Location;
        private const string TestEntryPoint = "DW2ModLauncher.Tests.Fixtures.Entry.Init";

        private static string MakeModDir(params string[] injectionDllNames)
        {
            string dir = Path.Combine(Path.GetTempPath(), "dw2-mod-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            foreach (string name in injectionDllNames)
            {
                string target = Path.Combine(dir, name.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(TestDll, target);
            }
            return dir;
        }

        private static ModInfo ModAt(string dir)
        {
            return new ModInfo { Id = "SomeMod", DisplayName = "Some Mod", Folder = dir, ContentRoot = dir };
        }

        [Fact]
        public void Build_InfersInjection_FromDllWithEntryType()
        {
            string dir = MakeModDir("SomeMod.dll");
            try
            {
                LoaderManifest manifest = LoaderManifestBuilder.Build(new List<ModInfo> { ModAt(dir) });

                Assert.Single(manifest.Entries);
                Assert.Equal(GamePaths.ToGameVisiblePath(Path.Combine(dir, "SomeMod.dll")), manifest.Entries[0].DllPath);
                Assert.Equal(TestEntryPoint, manifest.Entries[0].EntryType);
                Assert.Null(manifest.Entries[0].SettingsJson);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void Build_IgnoresDlls_ThatAreNotInjectionDlls()
        {
            string dir = MakeModDir();
            try
            {
                File.WriteAllText(Path.Combine(dir, "native.dll"), "not a managed assembly");
                // A managed DLL with no Entry type (the launcher's own Core assembly).
                File.Copy(typeof(InjectionScanner).Assembly.Location, Path.Combine(dir, "Library.dll"));

                LoaderManifest manifest = LoaderManifestBuilder.Build(new List<ModInfo> { ModAt(dir) });

                Assert.Empty(manifest.Entries);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void Build_LoadsMultipleDlls_InOrdinalPathOrder_IncludingSubfolders()
        {
            string dir = MakeModDir("b.dll", "a.dll", "sub/c.dll");
            try
            {
                LoaderManifest manifest = LoaderManifestBuilder.Build(new List<ModInfo> { ModAt(dir) });

                Assert.Equal(3, manifest.Entries.Count);
                Assert.EndsWith("a.dll", manifest.Entries[0].HostDllPath);
                Assert.EndsWith("b.dll", manifest.Entries[1].HostDllPath);
                Assert.EndsWith("c.dll", manifest.Entries[2].HostDllPath);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void Build_LauncherJson_OverridesInference()
        {
            string dir = MakeModDir("Inferred.dll", "Manual.dll");
            try
            {
                File.WriteAllText(Path.Combine(dir, "dw2modlauncher.json"), @"{ ""injection"": { ""dll"": ""Manual.dll"", ""entryPoint"": ""Custom.Boot.Start"" } }");

                LoaderManifest manifest = LoaderManifestBuilder.Build(new List<ModInfo> { ModAt(dir) });

                Assert.Single(manifest.Entries);
                Assert.EndsWith("Manual.dll", manifest.Entries[0].HostDllPath);
                Assert.Equal("Custom.Boot.Start", manifest.Entries[0].EntryType);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void Build_InlinesSettingsJson_WhenModHasSchema()
        {
            string dir = MakeModDir("SomeMod.dll");
            string token = "test/manifest-" + Guid.NewGuid().ToString("N");
            ModInfo mod = ModAt(dir);
            mod.Id = token;
            mod.ActiveToken = token;
            string settingsPath = ModSettingsStore.GetSettingsPath(mod);
            try
            {
                File.WriteAllText(Path.Combine(dir, "settings.schema.json"), @"{
                    ""fields"": [ { ""key"": ""Enabled"", ""type"": ""bool"", ""default"": true } ]
                }");

                LoaderManifest manifest = LoaderManifestBuilder.Build(new List<ModInfo> { mod });

                Assert.Single(manifest.Entries);
                Assert.Contains("Enabled", manifest.Entries[0].SettingsJson);
            }
            finally
            {
                Directory.Delete(dir, true);
                if (File.Exists(settingsPath)) File.Delete(settingsPath);
            }
        }

        [Fact]
        public void Build_ListsPatchFiles_ForModsWithoutAnyDll_InOrdinalPathOrder()
        {
            string dir = MakeModDir();
            try
            {
                Directory.CreateDirectory(Path.Combine(dir, "patches", "sub"));
                File.WriteAllText(Path.Combine(dir, "patches", "b.xml"), "<ArrayOfRace />");
                File.WriteAllText(Path.Combine(dir, "patches", "A.xml"), "<ArrayOfRace />");
                File.WriteAllText(Path.Combine(dir, "patches", "sub", "c.xml"), "<ArrayOfRace />");
                File.WriteAllText(Path.Combine(dir, "patches", "notes.txt"), "not a patch");

                LoaderManifest manifest = LoaderManifestBuilder.Build(new List<ModInfo> { ModAt(dir) });

                Assert.Empty(manifest.Entries);
                LoaderManifestPatchSet set = Assert.Single(manifest.Patches);
                Assert.Equal("Some Mod", set.DisplayName);
                Assert.Equal(3, set.Files.Count);
                Assert.EndsWith("A.xml", set.Files[0]);
                Assert.EndsWith("b.xml", set.Files[1]);
                Assert.EndsWith("c.xml", set.Files[2]);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void InvalidFor_ReportsEveryDllWithoutAValidEntryPoint()
        {
            string dir = MakeModDir("Good.dll");
            try
            {
                File.Copy(typeof(InjectionScanner).Assembly.Location, Path.Combine(dir, "Library.dll"));
                File.WriteAllBytes(Path.Combine(dir, "Native.dll"), new byte[] { 1, 2, 3 });
                List<InvalidInjectionDll> invalid = InjectionScanner.InvalidFor(ModAt(dir));
                Assert.Equal(new[] { "Library.dll", "Native.dll" }, invalid.ConvertAll(i => i.Dll));
                Assert.Equal(EntryProblem.NoEntryType, invalid[0].Problem);
                Assert.Equal(EntryProblem.NotManaged, invalid[1].Problem);
                // Reporting never changes what gets injected.
                Assert.Single(InjectionScanner.TargetsFor(ModAt(dir)));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void Build_RecordsModFolders_AndEachPatchSetsPlaceInThem()
        {
            string first = MakeModDir();
            string plain = MakeModDir();
            string second = MakeModDir();
            try
            {
                foreach (string dir in new[] { first, second })
                {
                    Directory.CreateDirectory(Path.Combine(dir, "patches"));
                    File.WriteAllText(Path.Combine(dir, "patches", "a.xml"), "<ArrayOfRace/>");
                }

                LoaderManifest manifest = LoaderManifestBuilder.Build(new List<ModInfo> { ModAt(first), ModAt(plain), ModAt(second) });

                Assert.Equal(new[] { Path.GetFileName(first), Path.GetFileName(plain), Path.GetFileName(second) }, manifest.ModFolders);
                Assert.Equal(new[] { 0, 2 }, manifest.Patches.ConvertAll(p => p.Order));
            }
            finally { Directory.Delete(first, true); Directory.Delete(plain, true); Directory.Delete(second, true); }
        }

        [Fact]
        public void Build_KeepsPatchSets_InModLoadOrder_AndSkipsModsWithoutPatches()
        {
            string first = MakeModDir();
            string plain = MakeModDir();
            string second = MakeModDir();
            try
            {
                foreach (string dir in new[] { first, second })
                {
                    Directory.CreateDirectory(Path.Combine(dir, "patches"));
                    File.WriteAllText(Path.Combine(dir, "patches", "p.xml"), "<ArrayOfRace />");
                }
                ModInfo a = ModAt(first);
                a.DisplayName = "First";
                ModInfo b = ModAt(plain);
                b.DisplayName = "Plain";
                ModInfo c = ModAt(second);
                c.DisplayName = "Second";

                LoaderManifest manifest = LoaderManifestBuilder.Build(new List<ModInfo> { a, b, c });

                Assert.Equal(new[] { "First", "Second" }, manifest.Patches.ConvertAll(p => p.DisplayName));
            }
            finally
            {
                Directory.Delete(first, true);
                Directory.Delete(plain, true);
                Directory.Delete(second, true);
            }
        }

        [Fact]
        public void Build_WithoutPatchesFolder_HasNoPatchSets()
        {
            string dir = MakeModDir("SomeMod.dll");
            try
            {
                Assert.Empty(LoaderManifestBuilder.Build(new List<ModInfo> { ModAt(dir) }).Patches);
            }
            finally { Directory.Delete(dir, true); }
        }


        [Fact]
        public void FontsOf_ReadsTheDeclaredFont_InLoadOrder_AndTheLastOneIsActive()
        {
            string a = MakeModDir();
            string b = MakeModDir();
            string c = MakeModDir();
            try
            {
                File.WriteAllText(Path.Combine(a, "dw2modlauncher.json"), @"{ ""font"": ""RussianFont"" }");
                File.WriteAllText(Path.Combine(b, "dw2modlauncher.json"), @"{ ""font"": ""ChsFonts"" }");
                // c declares no font.
                List<ModInfo> mods = new List<ModInfo> { ModAt(a), ModAt(b), ModAt(c) };

                List<LoaderManifestFont> fonts = LoaderManifestBuilder.FontsOf(mods);

                Assert.Equal(new[] { "RussianFont", "ChsFonts" }, fonts.ConvertAll(f => f.Name));
                Assert.Equal(GamePaths.ToGameVisiblePath(Path.GetFullPath(a)), fonts[0].Folder);
                Assert.Equal("ChsFonts", LoaderManifestBuilder.ActiveFont(mods));
                Assert.Equal(fonts.Count, LoaderManifestBuilder.Build(mods).Fonts.Count);
                Assert.Null(LoaderManifestBuilder.ActiveFont(new List<ModInfo> { ModAt(c) }));
            }
            finally { Directory.Delete(a, true); Directory.Delete(b, true); Directory.Delete(c, true); }
        }

        [Fact]
        public void TextFilesOf_ListsHintsDialogAndGalactopedia_LastModWins_AndIgnoresOtherTextFiles()
        {
            string a = MakeModDir();
            string b = MakeModDir();
            try
            {
                foreach (string rel in new[] { "Hints.txt", "dialog/zenox.txt", "Galactopedia/GameConcepts/Alliances.txt", "GameText.txt", "notes.txt", "patches/readme.txt" })
                {
                    string path = Path.Combine(a, rel.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllText(path, "a");
                }
                File.WriteAllText(Path.Combine(b, "hints.txt"), "b");

                List<LoaderManifestTextFile> files = LoaderManifestBuilder.TextFilesOf(new List<ModInfo> { ModAt(a), ModAt(b) });

                Assert.Equal(new[] { "dialog/zenox.txt", "Galactopedia/GameConcepts/Alliances.txt", "hints.txt" }, files.ConvertAll(f => f.Relative));
                Assert.Equal(GamePaths.ToGameVisiblePath(Path.GetFullPath(Path.Combine(b, "hints.txt"))), files[2].Path);
                Assert.Equal(3, LoaderManifestBuilder.Build(new List<ModInfo> { ModAt(a), ModAt(b) }).TextFiles.Count);
            }
            finally { Directory.Delete(a, true); Directory.Delete(b, true); }
        }

        [Theory]
        [InlineData("Russian Font")]
        [InlineData("Font\"Bad")]
        [InlineData("..\\Evil")]
        [InlineData("a/b")]
        [InlineData("--new-game")]
        [InlineData("Font.bundle")]
        [InlineData("")]
        public void FontsOf_IgnoresNamesThatAreNotPlain(string name)
        {
            string dir = MakeModDir();
            try
            {
                File.WriteAllText(Path.Combine(dir, "dw2modlauncher.json"), "{ \"font\": " + System.Text.Json.JsonSerializer.Serialize(name) + " }");
                List<LoaderManifestFont> fonts = LoaderManifestBuilder.FontsOf(new List<ModInfo> { ModAt(dir) });
                Assert.Empty(fonts);
            }
            finally { Directory.Delete(dir, true); }
        }


        [Fact]
        public void TextFilesOf_GalactopediaReplace_DropsEarlierArticles_AndSetsTheFlag()
        {
            string a = MakeModDir();
            string b = MakeModDir();
            try
            {
                Directory.CreateDirectory(Path.Combine(a, "Galactopedia", "GameConcepts"));
                File.WriteAllText(Path.Combine(a, "Galactopedia", "GameConcepts", "Old.txt"), "x");
                File.WriteAllText(Path.Combine(a, "Hints.txt"), "x");
                Directory.CreateDirectory(Path.Combine(b, "Galactopedia", "GameConcepts"));
                File.WriteAllText(Path.Combine(b, "Galactopedia", "GameConcepts", "Neu.txt"), "x");
                File.WriteAllText(Path.Combine(b, "dw2modlauncher.json"), @"{ ""galactopedia"": ""replace"" }");
                List<ModInfo> mods = new List<ModInfo> { ModAt(a), ModAt(b) };

                List<LoaderManifestTextFile> files = LoaderManifestBuilder.TextFilesOf(mods);

                Assert.Equal(new[] { "Galactopedia/GameConcepts/Neu.txt", "Hints.txt" }, files.ConvertAll(f => f.Relative));
                Assert.True(LoaderManifestBuilder.Build(mods).GalactopediaReplacesVanilla);
                Assert.False(LoaderManifestBuilder.Build(new List<ModInfo> { ModAt(a) }).GalactopediaReplacesVanilla);
            }
            finally { Directory.Delete(a, true); Directory.Delete(b, true); }
        }

        [Fact]
        public void AcceptsOptions_FalseForInitOnlyEntry()
        {
            Assert.False(InjectionScanner.AcceptsOptions(TestDll));
        }

        [Fact]
        public void Build_SkipsMods_WithNoInjectionTarget()
        {
            string dir = MakeModDir();
            try
            {
                LoaderManifest manifest = LoaderManifestBuilder.Build(new List<ModInfo> { ModAt(dir) });

                Assert.Empty(manifest.Entries);
            }
            finally { Directory.Delete(dir, true); }
        }
    }
}
