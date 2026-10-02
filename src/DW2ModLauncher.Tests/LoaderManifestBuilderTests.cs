using System;
using System.Collections.Generic;
using System.IO;
using DW2ModLauncher.Core.Models;
using DW2ModLauncher.Core.Services;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class LoaderManifestBuilderTests
    {
        private static string MakeModDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "dw2-mod-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "SomeMod.dll"), "");
            return dir;
        }

        [Fact]
        public void Build_ResolvesDllPath_FromModInfoInjectionFields()
        {
            string dir = MakeModDir();
            try
            {
                ModInfo mod = new ModInfo
                {
                    Id = "SomeMod",
                    DisplayName = "Some Mod",
                    Folder = dir,
                    ContentRoot = dir,
                    InjectionDll = "SomeMod.dll",
                    InjectionEntryPoint = "SomeMod.Bootstrap.Init",
                };

                LoaderManifest manifest = LoaderManifestBuilder.Build(new List<ModInfo> { mod });

                Assert.Single(manifest.Entries);
                Assert.Equal(GamePaths.ToGameVisiblePath(Path.Combine(dir, "SomeMod.dll")), manifest.Entries[0].DllPath);
                Assert.Equal("SomeMod.Bootstrap.Init", manifest.Entries[0].EntryType);
                Assert.Null(manifest.Entries[0].SettingsJson);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void Build_Dedups_WhenModInfoAndLauncherJson_DeclareSameTarget()
        {
            string dir = MakeModDir();
            try
            {
                File.WriteAllText(Path.Combine(dir, "launcher.json"), @"{ ""injection"": { ""dll"": ""SomeMod.dll"", ""entryPoint"": ""SomeMod.Bootstrap.Init"" } }");
                ModInfo mod = new ModInfo
                {
                    Id = "SomeMod",
                    Folder = dir,
                    ContentRoot = dir,
                    InjectionDll = "SomeMod.dll",
                    InjectionEntryPoint = "SomeMod.Bootstrap.Init",
                };

                LoaderManifest manifest = LoaderManifestBuilder.Build(new List<ModInfo> { mod });

                Assert.Single(manifest.Entries);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void Build_InlinesSettingsJson_WhenModHasSchema()
        {
            string dir = MakeModDir();
            string token = "test/manifest-" + Guid.NewGuid().ToString("N");
            ModInfo mod = new ModInfo
            {
                Id = token,
                ActiveToken = token,
                Folder = dir,
                ContentRoot = dir,
                InjectionDll = "SomeMod.dll",
                InjectionEntryPoint = "SomeMod.Bootstrap.Init",
            };
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
        public void Build_SkipsMods_WithNoInjectionTarget()
        {
            ModInfo mod = new ModInfo { Id = "NoInjection", Folder = Path.GetTempPath() };

            LoaderManifest manifest = LoaderManifestBuilder.Build(new List<ModInfo> { mod });

            Assert.Empty(manifest.Entries);
        }
    }
}
