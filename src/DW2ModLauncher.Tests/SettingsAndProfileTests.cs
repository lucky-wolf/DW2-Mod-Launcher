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
        }

        [Fact]
        public void PathDetector_FillsManagedModsFolder_FromValidGameRoot()
        {
            File.WriteAllText(Path.Combine(dir, "DistantWorlds2.exe"), "");
            LauncherSettings s = new LauncherSettings { GameRoot = dir };
            PathDetector.Detect(s, false);
            Assert.Equal(Path.Combine(dir, "mods"), s.ManagedModsRoot);
        }

        private static ModInfo Mod(string token, string version)
        {
            return new ModInfo { Id = token, DisplayName = token, Version = version, ActiveToken = token };
        }

        [Fact]
        public void ProfileStore_SaveLoadListDelete()
        {
            ProfileStore store = new ProfileStore(Path.Combine(dir, "Profiles"));
            Assert.Empty(store.ListNames());
            Assert.Null(store.Load("x"));

            ModProfile p = ProfileStore.Capture("My/Profile", new List<string> { "a", "b" }, "--x", new List<ModInfo> { Mod("a", "1.0") });
            store.Save(p);

            Assert.Equal(new[] { "My_Profile" }, store.ListNames());
            Assert.True(store.Exists("My/Profile"));
            ModProfile loaded = store.Load("My/Profile");
            Assert.Equal(new[] { "a", "b" }, loaded.Order);
            Assert.Equal("--x", loaded.ManualLaunchArguments);
            Assert.Equal("1.0", loaded.Versions["a"]);

            store.Delete("My/Profile");
            Assert.Empty(store.ListNames());
            store.Delete("My/Profile");
        }

        [Fact]
        public void CompareVersions_FlagsMissingAndChangedOnly()
        {
            ModProfile p = ProfileStore.Capture("p", null, "", new List<ModInfo> { Mod("same", "1"), Mod("changed", "1"), Mod("gone", "1") });
            List<ModInfo> installed = new List<ModInfo> { Mod("same", "1"), Mod("changed", "2") };

            List<ProfileVersionChange> changes = ProfileStore.CompareVersions(p, installed);

            Assert.Equal(2, changes.Count);
            Assert.Contains(changes, c => c.Token == "changed" && c.Installed != null && c.SavedVersion == "1");
            Assert.Contains(changes, c => c.Token == "gone" && c.Installed == null);
        }

        [Fact]
        public void Snapshot_CreateThenRestore_PutsFilesBack()
        {
            string modDir = Path.Combine(dir, "mods", "m");
            Directory.CreateDirectory(Path.Combine(modDir, "sub"));
            File.WriteAllText(Path.Combine(modDir, "a.txt"), "orig");
            File.WriteAllText(Path.Combine(modDir, "sub", "b.txt"), "orig-b");
            string modsJson = Path.Combine(dir, "mods.json");
            string settingsPath = Path.Combine(dir, "launcher_settings.json");
            File.WriteAllText(modsJson, "[\"m\"]");
            File.WriteAllText(settingsPath, "{\"Language\":\"en\"}");
            ModInfo mod = new ModInfo { Id = "m", ActiveToken = "m", Folder = modDir };

            string snapshots = Path.Combine(dir, "Snapshots");
            Assert.Null(SnapshotStore.FindLatest(snapshots));
            string root = SnapshotStore.Create(snapshots, modsJson, settingsPath, new List<ModInfo> { mod });
            Assert.Equal(root, SnapshotStore.FindLatest(snapshots));

            File.WriteAllText(Path.Combine(modDir, "a.txt"), "changed");
            File.WriteAllText(modsJson, "[]");
            SnapshotStore.Restore(root, modsJson, settingsPath);

            Assert.Equal("orig", File.ReadAllText(Path.Combine(modDir, "a.txt")));
            Assert.Equal("orig-b", File.ReadAllText(Path.Combine(modDir, "sub", "b.txt")));
            Assert.Equal("[\"m\"]", File.ReadAllText(modsJson));
        }
    }
}
