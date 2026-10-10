using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using DW2ModLauncher.Core.Models;
using DW2ModLauncher.Core.Services;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class ModOrderAndConflictTests : IDisposable
    {
        private readonly string dir = Path.Combine(Path.GetTempPath(), "dw2-test-" + Guid.NewGuid().ToString("N"));

        public ModOrderAndConflictTests() { Directory.CreateDirectory(dir); }
        public void Dispose() { Directory.Delete(dir, true); }

        private ModInfo MakeMod(string id, params (string rel, string content)[] files)
        {
            string folder = Path.Combine(dir, id);
            Directory.CreateDirectory(folder);
            foreach ((string rel, string content) in files)
            {
                string full = Path.Combine(folder, rel.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(full));
                File.WriteAllText(full, content);
            }
            return new ModInfo { Id = id, DisplayName = id, Folder = folder, ContentRoot = folder, ActiveToken = id };
        }

        // ---- mods.json

        [Fact]
        public void ModOrderStore_WriteThenRead_RoundTripsAndDedupes()
        {
            string path = Path.Combine(dir, "mods.json");
            List<string> written = ModOrderStore.Write(path, new[] { "a", "B", "b", "", "c" });
            Assert.Equal(new[] { "a", "B", "c" }, written);

            ModOrderState state = ModOrderStore.Read(path);
            Assert.True(state.FileFound);
            Assert.False(state.ReadFailed);
            Assert.Equal(new[] { "a", "B", "c" }, state.Order);
        }

        [Fact]
        public void ModOrderStore_Write_KeepsBackupOfPreviousFile()
        {
            string path = Path.Combine(dir, "mods.json");
            ModOrderStore.Write(path, new[] { "one" });
            ModOrderStore.Write(path, new[] { "two" });
            ModOrderStore.Write(path, new[] { "three" });
            string[] backups = Directory.GetFiles(dir, "mods.json.*.launcher_backup");
            Assert.Equal(2, backups.Length);
            Assert.Contains(backups, b => File.ReadAllText(b).Contains("one"));
            Assert.Contains(backups, b => File.ReadAllText(b).Contains("two"));
            Assert.False(File.Exists(path + ".launcher_tmp"));
        }

        [Fact]
        public void ModOrderStore_Write_ReturnsNullWhenFolderMissing()
        {
            Assert.Null(ModOrderStore.Write(Path.Combine(dir, "nope", "mods.json"), new[] { "a" }));
        }

        [Fact]
        public void ModOrderStore_Read_MissingFileIsNotAnError_CorruptFileIs()
        {
            ModOrderState missing = ModOrderStore.Read(Path.Combine(dir, "mods.json"));
            Assert.False(missing.FileFound);
            Assert.False(missing.ReadFailed);

            File.WriteAllText(Path.Combine(dir, "mods.json"), "{ nope");
            Assert.True(ModOrderStore.Read(Path.Combine(dir, "mods.json")).ReadFailed);
        }

        [Fact]
        public void ModOrderStore_PathFor_FallsBackToGameModsFolder()
        {
            Assert.Equal(Path.Combine("m", "mods.json"), ModOrderStore.PathFor("m", "g"));
            Assert.Equal(Path.Combine("g", "mods", "mods.json"), ModOrderStore.PathFor("", "g"));
            Assert.Null(ModOrderStore.PathFor("", ""));
        }

        [Fact]
        public void WithEnabled_RemovesThenAppendsOnlyWhenEnabling()
        {
            Assert.Equal(new[] { "a", "c", "B" }, ModOrderStore.WithEnabled(new[] { "a", "b", "c" }, "B", true));
            Assert.Equal(new[] { "a", "c" }, ModOrderStore.WithEnabled(new[] { "a", "b", "c" }, "b", false));
        }

        [Fact]
        public void Reorder_RefillsSubsetSlotsAndKeepsOthersInPlace()
        {
            // x and y are not in the visible list; a,b,c are, and were dragged into c,a,b.
            var result = ModOrderStore.Reorder(new[] { "a", "x", "b", "y", "c" }, new List<string> { "c", "a", "b" });
            Assert.Equal(new[] { "c", "x", "a", "y", "b" }, result);
        }

        [Fact]
        public void Reorder_AppendsSubsetEntriesNotYetInOrder()
        {
            Assert.Equal(new[] { "x", "a", "b" }, ModOrderStore.Reorder(new[] { "x", "a" }, new List<string> { "a", "b" }));
        }

        // ---- enabled rules

        [Fact]
        public void ModOrderState_UsesModsJsonWhenFound_ElseSavedSelection()
        {
            ModInfo mod = new ModInfo { Id = "m", ActiveToken = "m" };
            LauncherSettings settings = new LauncherSettings();
            settings.SelectedMods[mod.Key] = true;

            ModOrderState noFile = new ModOrderState();
            Assert.True(noFile.IsSelected(mod, settings));
            Assert.True(noFile.IsEnabledForConflict(mod, settings));

            ModOrderState file = new ModOrderState { FileFound = true, Order = new List<string>() };
            Assert.False(file.IsSelected(mod, settings));
            file.Order.Add("M");
            Assert.True(file.IsSelected(mod, settings));
        }

        // ---- conflicts

        [Fact]
        public void Analyze_DifferingSharedFile_IsACollision_IdenticalIsNot()
        {
            ModInfo a = MakeMod("a", ("Data/Ships.xml", "A"), ("Hints.txt", "x"));
            ModInfo b = MakeMod("b", ("data/ships.xml", "B"), ("Hints.txt", "x"));

            var collisions = ConflictAnalyzer.Analyze(new[] { a, b }, m => true);

            Assert.Single(collisions);
            Assert.True(collisions.ContainsKey(Path.Combine("data", "ships.xml")));
            Assert.Equal(1, a.HighRiskConflictCount);
            Assert.Equal(1, a.IdenticalFileCount);
            Assert.Equal(new[] { "b" }, a.ConflictMods);
        }

        [Fact]
        public void Analyze_IdenticalMixedCaseFiles_AreRecognisedAsIdentical()
        {
            // Paths are compared lowercased; hashing must still find "Data/Ships.xml" on a case-sensitive filesystem.
            ModInfo a = MakeMod("a", ("Data/Ships.xml", "SAME"));
            ModInfo b = MakeMod("b", ("Data/Ships.xml", "SAME"));

            var collisions = ConflictAnalyzer.Analyze(new[] { a, b }, m => true);

            Assert.Empty(collisions);
            Assert.Equal(1, a.IdenticalFileCount);
        }

        [Fact]
        public void Analyze_DisabledModsDoNotParticipate()
        {
            ModInfo a = MakeMod("a", ("f.xml", "A"));
            ModInfo b = MakeMod("b", ("f.xml", "B"));
            Assert.Empty(ConflictAnalyzer.Analyze(new[] { a, b }, m => m == a));
        }

        [Fact]
        public void AnalyzeEnabledCopies_FlagsSameWorkshopIdEnabledTwice_NotNames()
        {
            ModInfo local = MakeMod("Nebulizer"); local.WorkshopId = "123";
            ModInfo steam = MakeMod("123"); steam.IsWorkshop = true;
            ModInfo sameName = MakeMod("Nebulizer2"); sameName.DisplayName = "Nebulizer";

            ConflictAnalyzer.AnalyzeEnabledCopies(new[] { local, steam, sameName }, m => true);
            Assert.Equal(1, local.EnabledCopyCount);
            Assert.Equal(1, steam.EnabledCopyCount);
            Assert.Equal(0, sameName.EnabledCopyCount);

            ConflictAnalyzer.AnalyzeEnabledCopies(new[] { local, steam, sameName }, m => m == local);
            Assert.Equal(0, local.EnabledCopyCount);
        }

        [Fact]
        public void Severity_RanksDisabledOkCautionConflict()
        {
            ModInfo m = new ModInfo();
            Assert.Equal(0, ModHealth.Severity(m, false));
            Assert.Equal(1, ModHealth.Severity(m, true));
            m.LowRiskConflictCount = 1;
            Assert.Equal(2, ModHealth.Severity(m, true));
            m.HighRiskConflictCount = 1;
            Assert.Equal(3, ModHealth.Severity(m, true));
        }

        // ---- diagnostics + misc

        [Fact]
        public void Diagnostics_ReportsMissingRequiredMod_AndMissingLoader()
        {
            ModInfo a = MakeMod("a");
            a.RequiredMods = new List<string> { "ghost" };

            var issues = LaunchDiagnostics.Build(new List<ModInfo> { a }, new ModOrderState(), Path.Combine(dir, "nope.dll"), new List<LoaderManifestEntry>(), key => key + ":");

            Assert.Contains(issues, i => i.Contains("MissingRequiredMod:ghost"));
            Assert.Contains(issues, i => i.Contains("LaunchArgumentDLLNotFound:") && i.Contains("nope.dll"));
        }

        [Theory]
        [InlineData("1.0.6", "1.0.5", true)]
        [InlineData("1.0.6", "1.0.6", false)]
        [InlineData("1.0.6", "1.0.6-dev", false)]
        [InlineData("v1.0.6", "1.1.0", false)]
        [InlineData("1.0.6", "", false)]
        [InlineData("garbage", "1.0.0", false)]
        [InlineData("", "1.0.0", false)]
        public void LauncherRequirement_ComparesNumericVersion_AndIgnoresWhatItCannotRead(string required, string current, bool unmet)
        {
            Assert.Equal(unmet, LauncherRequirement.IsUnmet(new ModInfo { MinLauncherVersion = required }, current));
        }

        [Fact]
        public void ReadModInfo_PicksUpMinLauncherVersion_FromDw2ModLauncherJson()
        {
            ModInfo mod = MakeMod("needy", ("mod.json", "{\"displayName\":\"Needy\"}"), ("dw2modlauncher.json", "{\"minLauncherVersion\":\" 1.0.6 \"}"));

            ModInfo read = ModScanner.ReadModInfo(mod.Folder, Path.Combine(mod.Folder, "mod.json"), false, key => key);

            Assert.Equal("1.0.6", read.MinLauncherVersion);
            Assert.Null(ModScanner.ReadModInfo(MakeMod("plain", ("mod.json", "{}")).Folder, Path.Combine(dir, "plain", "mod.json"), false, key => key).MinLauncherVersion);
        }

        [Fact]
        public void MetLauncherRequirement_AddsNoCautionOrProblemLine()
        {
            ModInfo mod = new ModInfo { MinLauncherVersion = "0.0.1" };
            Assert.Equal(1, ModHealth.Severity(mod, true));
            Assert.Empty(ModDetails.BuildProblems(mod, 2, key => key));
        }

        [Fact]
        public void Diagnostics_ChecksHostPathNotGamePath_OfInjectedDlls()
        {
            ModInfo a = MakeMod("a", ("a.dll", ""));
            string real = Path.Combine(a.Folder, "a.dll");
            string loader = Path.Combine(dir, "loader.dll");
            File.WriteAllText(loader, "");
            var entry = new LoaderManifestEntry { DllPath = @"Z:\does\not\exist\a.dll", HostDllPath = real };

            var issues = LaunchDiagnostics.Build(new List<ModInfo> { a }, new ModOrderState(), loader, new[] { entry }, key => key + ":");

            Assert.DoesNotContain(issues, i => i.Contains("LaunchArgumentDLLNotFound"));
        }

        [Fact]
        public void LoaderManifest_DoesNotSerializeHostPath()
        {
            string json = JsonSerializer.Serialize(new LoaderManifestEntry { DllPath = "Z:\\a.dll", HostDllPath = "/a.dll" });
            Assert.DoesNotContain("HostDllPath", json);
        }

        [Theory]
        [InlineData("DistantWorlds2", true)]
        [InlineData("DistantWorlds2.", true)]
        [InlineData("distantworlds2", true)]
        [InlineData("firefox", false)]
        [InlineData(null, false)]
        public void GameProcess_MatchesFullAndWineTruncatedNames(string name, bool expected)
        {
            Assert.Equal(expected, GameProcess.IsGameProcessName(name));
        }
    }
}
