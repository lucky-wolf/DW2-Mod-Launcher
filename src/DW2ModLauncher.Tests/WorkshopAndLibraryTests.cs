using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DW2ModLauncher.Core.Models;
using DW2ModLauncher.Core.Services;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class WorkshopAndLibraryTests : IDisposable
    {
        private readonly string dir = Path.Combine(Path.GetTempPath(), "dw2-test-" + Guid.NewGuid().ToString("N"));

        public WorkshopAndLibraryTests() { Directory.CreateDirectory(dir); }
        public void Dispose() { Directory.Delete(dir, true); }

        private static ModInfo Mod(string id, string name = null, bool workshop = false)
        {
            return new ModInfo { Id = id, DisplayName = name ?? id, ActiveToken = id, IsWorkshop = workshop };
        }

        // ---- workshop updates

        [Fact]
        public void Apply_MarksUpdateCurrentAndUnknown()
        {
            ModInfo needsUpdate = Mod("1", workshop: true);
            ModInfo current = Mod("2", workshop: true);
            ModInfo unknown = Mod("3", workshop: true);
            WorkshopUpdateCheckResult r = new WorkshopUpdateCheckResult();
            r.InstalledTimes["1"] = 100; r.RemoteTimes["1"] = 200;
            r.InstalledTimes["2"] = 100; r.DetailTimes["2"] = 101; // within the 2s tolerance

            int updates = WorkshopUpdateService.Apply(r, new[] { needsUpdate, current, unknown });

            Assert.Equal(1, updates);
            Assert.Equal("update", needsUpdate.UpdateState);
            Assert.Equal(200, needsUpdate.RemoteWorkshopTimeUpdated);
            Assert.Equal("current", current.UpdateState);
            Assert.Equal("unknown", unknown.UpdateState);
        }

        [Fact]
        public void Apply_CopiesRemoteDetails_AndRenamesFromWorkshopTitle()
        {
            ModInfo mod = Mod("1", "local name", workshop: true);
            WorkshopUpdateCheckResult r = new WorkshopUpdateCheckResult();
            r.Details["1"] = new WorkshopRemoteDetail { Title = "Steam Title", Description = "Steam desc", Creator = "someone" };

            WorkshopUpdateService.Apply(r, new[] { mod });

            Assert.Equal("Steam Title", mod.DisplayName);
            Assert.Equal("Steam desc", mod.Description);
            Assert.Equal("someone", mod.WorkshopCreator);
        }

        [Fact]
        public void Backup_CopiesEachModFolder_SkippingMissingOnes()
        {
            string folder = Path.Combine(dir, "mod1");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "a.txt"), "x");
            ModInfo real = Mod("1"); real.Folder = folder; real.Version = "2.0";
            ModInfo missing = Mod("2"); missing.Folder = Path.Combine(dir, "gone");

            int count = WorkshopUpdateService.Backup(Path.Combine(dir, "backup"), new[] { real, missing });

            Assert.Equal(1, count);
            Assert.True(File.Exists(Path.Combine(dir, "backup", "1_v2.0", "a.txt")));
        }

        // ---- library

        [Fact]
        public void OrderForDisplay_PutsLoadOrderFirst_ThenAlphabetical()
        {
            ModOrderState order = new ModOrderState { FileFound = true, Order = new List<string> { "b", "a" } };

            var result = ModLibrary.OrderForDisplay(new[] { Mod("z"), Mod("a"), Mod("b"), Mod("c") }, order);

            Assert.Equal(new[] { "b", "a", "c", "z" }, result.Select(m => m.Id));
        }

        [Fact]
        public void RestoreWorkshopRuntimeState_CarriesUpdateStateOntoFreshScan()
        {
            ModInfo old = Mod("1", workshop: true); old.UpdateState = "update"; old.WorkshopTitle = "T"; old.DisplayName = "Steam name";
            ModInfo fresh = Mod("1", "folder name", workshop: true);

            ModLibrary.RestoreWorkshopRuntimeState(new List<ModInfo> { fresh }, ModLibrary.IndexWorkshopById(new[] { old }));

            Assert.Equal("update", fresh.UpdateState);
            Assert.Equal("Steam name", fresh.DisplayName);
        }

        [Fact]
        public void SaveOrderSelection_WritesModsJson_AndUpdatesState()
        {
            string mods = Path.Combine(dir, "mods");
            Directory.CreateDirectory(mods);
            LauncherSettings settings = new LauncherSettings { ManagedModsRoot = mods };
            ModOrderState order = new ModOrderState { Order = new List<string> { "a" } };

            SetEnabledResult result = ModLibrary.SaveOrderSelection(Mod("b"), true, settings, order);

            Assert.Equal(SetEnabledOutcome.Saved, result.Outcome);
            Assert.Equal(new[] { "a", "b" }, order.Order);
            Assert.True(order.FileFound);
            Assert.Equal(new[] { "a", "b" }, ModOrderStore.Read(Path.Combine(mods, "mods.json")).Order);
        }

        [Fact]
        public void SaveOrderSelection_RefusesToOverwriteUnreadableModsJson()
        {
            LauncherSettings settings = new LauncherSettings { ManagedModsRoot = dir };
            ModOrderState order = new ModOrderState { ReadFailed = true };
            Assert.Equal(SetEnabledOutcome.ModsJsonInvalid, ModLibrary.SaveOrderSelection(Mod("b"), true, settings, order).Outcome);
            Assert.False(File.Exists(Path.Combine(dir, "mods.json")));
        }

        [Fact]
        public void SaveOrderSelection_NoModsFolder_ReportsIt()
        {
            LauncherSettings settings = new LauncherSettings { ManagedModsRoot = Path.Combine(dir, "nope") };
            Assert.Equal(SetEnabledOutcome.NoModsFolder, ModLibrary.SaveOrderSelection(Mod("b"), true, settings, new ModOrderState()).Outcome);
        }

        [Fact]
        public void SetEnabled_RecordsSelectionInSettings()
        {
            string mods = Path.Combine(dir, "mods");
            Directory.CreateDirectory(mods);
            LauncherSettings settings = new LauncherSettings { ManagedModsRoot = mods };
            ModInfo mod = Mod("a");

            ModLibrary.SetEnabled(mod, true, settings, new ModOrderState());

            Assert.True(settings.SelectedMods[mod.Key]);
        }

        // ---- launch

        [Fact]
        public void OrderedEnabled_FiltersToEnabledAndSortsByLoadOrder()
        {
            ModOrderState order = new ModOrderState { FileFound = true, Order = new List<string> { "c", "a" } };

            var result = GameLauncher.OrderedEnabled(new[] { Mod("a"), Mod("b"), Mod("c") }, order, new LauncherSettings());

            Assert.Equal(new[] { "c", "a" }, result.Select(m => m.Id));
        }

        [Fact]
        public void BuildArguments_StartsWithInjectionFlag_ThenUserArguments()
        {
            string args = GameLauncher.BuildArguments("--windowed");
            Assert.StartsWith("--low-level-inject ", args);
            Assert.Contains("!DW2ModLauncher.Loader.Entry.Init", args);
            Assert.EndsWith(" --windowed", args);
            Assert.DoesNotContain("--windowed", GameLauncher.BuildArguments(" "));
        }
    }
}
