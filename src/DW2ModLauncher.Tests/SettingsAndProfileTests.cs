using System;
using System.Collections.Generic;
using System.IO;
using DW2ModLauncher.Core.Models;
using DW2ModLauncher.Core.Services;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class SettingsAndProfileTests : IDisposable
    {
        private readonly string dir = Path.Combine(Path.GetTempPath(), "dw2-test-" + Guid.NewGuid().ToString("N"));

        public SettingsAndProfileTests() { Directory.CreateDirectory(dir); }
        public void Dispose() { Directory.Delete(dir, true); }

        [Fact]
        public void SettingsStore_RoundTrips_AndMissingFileGivesDefaults()
        {
            LauncherSettingsStore store = new LauncherSettingsStore(Path.Combine(dir, "s.json"));
            Assert.Equal("en", store.Load().Language);

            LauncherSettings s = new LauncherSettings { Language = "ja", GameRoot = "/g" };
            s.SelectedMods["a"] = true;
            store.Save(s);

            LauncherSettings loaded = store.Load();
            Assert.Equal("ja", loaded.Language);
            Assert.Equal("/g", loaded.GameRoot);
            Assert.True(loaded.SelectedMods["a"]);
        }

        [Fact]
        public void SettingsStore_CorruptFileGivesDefaults()
        {
            string path = Path.Combine(dir, "s.json");
            File.WriteAllText(path, "{ not json");
            Assert.Equal("en", new LauncherSettingsStore(path).Load().Language);
        }

        [Fact]
        public void Normalize_UsesNativeSeparatorsOnWindows()
        {
            if (!OperatingSystem.IsWindows()) return;
            LauncherSettings s = new LauncherSettings { GameRoot = @"c:/steam\steamapps/common/Distant Worlds 2" };
            LauncherSettingsStore.Normalize(s);
            Assert.DoesNotContain("/", s.GameRoot);
        }

        [Fact]
        public void Normalize_FillsNulls()
        {
            LauncherSettings s = new LauncherSettings { Language = " ", GameRoot = null, ActiveProfile = null, SelectedMods = null };
            LauncherSettingsStore.Normalize(s);
            Assert.Equal("en", s.Language);
            Assert.Equal("", s.GameRoot);
            Assert.Equal("", s.ActiveProfile);
            Assert.NotNull(s.SelectedMods);
        }

        [Fact]
        public void Validate_ReportsFirstProblem_AndAllowsBlank()
        {
            Assert.Equal(SettingsProblem.None, LauncherSettingsStore.Validate("", "", ""));
            Assert.Equal(SettingsProblem.GameFolderInvalid, LauncherSettingsStore.Validate(dir, "", ""));
            File.WriteAllText(Path.Combine(dir, "DistantWorlds2.exe"), "");
            Assert.Equal(SettingsProblem.WorkshopFolderMissing, LauncherSettingsStore.Validate(dir, Path.Combine(dir, "nope"), ""));
            Assert.Equal(SettingsProblem.ManagedFolderMissing, LauncherSettingsStore.Validate(dir, dir, Path.Combine(dir, "nope")));
            Assert.Equal(SettingsProblem.None, LauncherSettingsStore.Validate(dir, dir, dir));
            Assert.Equal(SettingsProblem.LogFolderMissing, LauncherSettingsStore.Validate(dir, dir, dir, Path.Combine(dir, "nope")));
            Assert.Equal(SettingsProblem.None, LauncherSettingsStore.Validate(dir, dir, dir, dir));
        }

        [Fact]
        public void PathDetector_FillsManagedModsFolder_FromValidGameRoot()
        {
            File.WriteAllText(Path.Combine(dir, "DistantWorlds2.exe"), "");
            LauncherSettings s = new LauncherSettings { GameRoot = dir };
            PathDetector.Detect(s, false);
            Assert.Equal(Path.Combine(dir, "mods"), s.ManagedModsRoot);
        }
    }
}
