using System.Collections.Generic;
using System.Linq;
using DW2ModLauncher.Core.Models;
using DW2ModLauncher.Core.Services.Publishing;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class SteamLinksTests
    {
        private static ModInfo Mod(string id, string workshopId = null, params LauncherDependency[] deps)
        {
            return new ModInfo { Id = id, DisplayName = id, WorkshopId = workshopId, Dependencies = deps.ToList() };
        }

        private static LauncherDependency Dep(string mod, bool required = false, bool? steamLink = null, long? workshopId = null)
        {
            return new LauncherDependency { mod = mod, required = required, steamLink = steamLink, workshopId = workshopId };
        }

        [Fact]
        public void RequiredIsAlwaysLinked_OptionalFollowsTheSetting()
        {
            ModInfo keeper = Mod("Keeper", "11");
            ModInfo matrix = Mod("Matrix", "22");
            ModInfo mine = Mod("Mine", "33", Dep("Keeper", required: true), Dep("Matrix"));
            List<ModInfo> all = new List<ModInfo> { keeper, matrix, mine };

            Assert.Equal(new long[] { 11, 22 }, SteamLinks.Plan(mine, all, true).Wanted);
            Assert.Equal(new long[] { 11 }, SteamLinks.Plan(mine, all, false).Wanted);
        }

        [Fact]
        public void SteamLinkOverridesBothWays()
        {
            ModInfo a = Mod("A", "11");
            ModInfo b = Mod("B", "22");
            ModInfo mine = Mod("Mine", "33", Dep("A", required: true, steamLink: false), Dep("B", steamLink: true));
            List<ModInfo> all = new List<ModInfo> { a, b, mine };

            Assert.Equal(new long[] { 22 }, SteamLinks.Plan(mine, all, false).Wanted);
        }

        [Fact]
        public void WorkshopIdInTheEntryIsUsedWhenTheModIsNotInstalled()
        {
            ModInfo mine = Mod("Mine", "33", Dep("Gone", workshopId: 44));
            SteamLinkPlan plan = SteamLinks.Plan(mine, new List<ModInfo> { mine }, true);
            Assert.Equal(new long[] { 44 }, plan.Wanted);
            Assert.Empty(plan.Unlinkable);
        }

        [Fact]
        public void AModWithoutAWorkshopItemIsReportedNotSilentlySkipped()
        {
            ModInfo local = Mod("LocalOnly");
            ModInfo mine = Mod("Mine", "33", Dep("LocalOnly"), Dep("Nowhere"));
            SteamLinkPlan plan = SteamLinks.Plan(mine, new List<ModInfo> { local, mine }, true);
            Assert.Empty(plan.Wanted);
            Assert.Equal(new[] { "LocalOnly", "Nowhere" }, plan.Unlinkable);
        }

        [Fact]
        public void DuplicatesAndSelfAreDropped()
        {
            ModInfo a = Mod("A", "11");
            ModInfo mine = Mod("Mine", "33", Dep("A"), Dep("A", workshopId: 11), Dep("Mine"));
            Assert.Equal(new long[] { 11 }, SteamLinks.Plan(mine, new List<ModInfo> { a, mine }, true).Wanted);
        }

        [Fact]
        public void Diff_AddsMissing_AndRemovesOnlyWhenTrimming()
        {
            SteamLinks.Diff(new long[] { 1, 2 }, new long[] { 2, 3 }, false, out List<long> add, out List<long> remove);
            Assert.Equal(new long[] { 1 }, add);
            Assert.Empty(remove);
            SteamLinks.Diff(new long[] { 1, 2 }, new long[] { 2, 3 }, true, out add, out remove);
            Assert.Equal(new long[] { 3 }, remove);
        }
    }
}

namespace DW2ModLauncher.Tests
{
    public class SetDependenciesTests : System.IDisposable
    {
        private readonly string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dw2-setdeps-" + System.Guid.NewGuid().ToString("N"));

        public SetDependenciesTests() { System.IO.Directory.CreateDirectory(dir); }
        public void Dispose() { try { System.IO.Directory.Delete(dir, true); } catch (System.IO.IOException) { } }

        [Fact]
        public void WritesEntriesInPlace_KeepsOtherFields_AndDropsTheKeyWhenEmpty()
        {
            string path = System.IO.Path.Combine(dir, "dw2modlauncher.json");
            System.IO.File.WriteAllText(path, "{\n  \"author\": \"me\",\n  \"dependencies\": [ { \"mod\": \"Old\" } ],\n  \"tags\": [\"x\"]\n}");
            Core.Services.LauncherMetaWriter.SetDependencies(dir, new List<LauncherDependency>
            {
                new LauncherDependency { mod = "Keeper", workshopId = 11, required = false, steamLink = true, loadAfter = true, minVersion = " 1.2 " }
            });
            string text = System.IO.File.ReadAllText(path);
            Assert.True(text.IndexOf("\"author\"") < text.IndexOf("\"dependencies\"") && text.IndexOf("\"dependencies\"") < text.IndexOf("\"tags\""));
            LauncherMeta meta = System.Text.Json.JsonSerializer.Deserialize<LauncherMeta>(text);
            LauncherDependency dep = Assert.Single(meta.dependencies);
            Assert.Equal("Keeper", dep.mod);
            Assert.Equal(11, dep.workshopId);
            Assert.True(dep.steamLink);
            Assert.True(dep.loadAfter);
            Assert.False(dep.loadBefore);
            Assert.Equal("1.2", dep.minVersion);
            Assert.Equal("me", meta.author);

            Core.Services.LauncherMetaWriter.SetDependencies(dir, new List<LauncherDependency>());
            Assert.DoesNotContain("dependencies", System.IO.File.ReadAllText(path));
        }
    }
}
