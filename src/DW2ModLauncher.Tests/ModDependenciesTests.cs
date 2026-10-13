using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DW2ModLauncher.Core.Models;
using DW2ModLauncher.Core.Services;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class ModDependenciesTests : IDisposable
    {
        private readonly string dir = Path.Combine(Path.GetTempPath(), "dw2-deps-" + Guid.NewGuid().ToString("N"));

        public ModDependenciesTests() { Directory.CreateDirectory(dir); }
        public void Dispose() { try { Directory.Delete(dir, true); } catch (IOException) { } }

        private ModInfo MakeMod(string id, string version = null, params (string rel, string content)[] files)
        {
            string folder = Path.Combine(dir, id);
            Directory.CreateDirectory(folder);
            foreach ((string rel, string content) in files)
            {
                string full = Path.Combine(folder, rel.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(full));
                File.WriteAllText(full, content);
            }
            return new ModInfo { Id = id, DisplayName = id, Folder = folder, ContentRoot = folder, ActiveToken = "mods/" + id, Version = version };
        }

        private static LauncherDependency Dep(string mod, bool required = false, string minVersion = null, bool after = false, bool before = false)
        {
            return new LauncherDependency { mod = mod, required = required, minVersion = minVersion, loadAfter = after, loadBefore = before };
        }

        // ---- order

        [Fact]
        public void Enforce_MovesAModAfterWhatItLoadsAfter()
        {
            ModInfo a = MakeMod("a"); ModInfo b = MakeMod("b"); ModInfo c = MakeMod("c");
            a.Dependencies.Add(Dep("c", after: true));

            var result = ModDependencies.Enforce(new[] { a, b, c });

            Assert.Equal(new[] { "b", "c", "a" }, result.Order.Select(m => m.Id));
            Assert.True(result.Changed);
            Assert.Single(result.Moves);
        }

        [Fact]
        public void Enforce_LoadBefore_MovesTheOtherModsDependentLater()
        {
            ModInfo a = MakeMod("a"); ModInfo b = MakeMod("b");
            b.Dependencies.Add(Dep("a", before: true));

            var result = ModDependencies.Enforce(new[] { b, a });

            Assert.Equal(new[] { "b", "a" }, result.Order.Select(m => m.Id));
            Assert.False(result.Changed);

            a.Dependencies.Add(Dep("b", before: true));
            b.Dependencies.Clear();
            result = ModDependencies.Enforce(new[] { b, a });
            Assert.Equal(new[] { "a", "b" }, result.Order.Select(m => m.Id));
        }

        [Fact]
        public void Enforce_LeavesAnOrderThatAlreadyMeetsTheRules()
        {
            ModInfo a = MakeMod("a"); ModInfo b = MakeMod("b"); ModInfo c = MakeMod("c");
            c.Dependencies.Add(Dep("a", after: true));

            var result = ModDependencies.Enforce(new[] { a, b, c });

            Assert.Equal(new[] { "a", "b", "c" }, result.Order.Select(m => m.Id));
            Assert.False(result.Changed);
        }

        [Fact]
        public void Enforce_IgnoresARuleAboutAModThatIsNotEnabled()
        {
            ModInfo a = MakeMod("a");
            a.Dependencies.Add(Dep("ghost", after: true));

            var result = ModDependencies.Enforce(new[] { a });

            Assert.False(result.Changed);
            Assert.Empty(result.Cycle);
        }

        [Fact]
        public void Enforce_ReportsRulesThatContradictEachOther()
        {
            ModInfo a = MakeMod("a"); ModInfo b = MakeMod("b");
            a.Dependencies.Add(Dep("b", after: true));
            b.Dependencies.Add(Dep("a", after: true));

            var result = ModDependencies.Enforce(new[] { a, b });

            Assert.Equal(2, result.Cycle.Count);
            Assert.Equal(new[] { "a", "b" }, result.Order.Select(m => m.Id));
        }

        [Fact]
        public void Enforce_MatchesTheOtherModByNameIgnoringCase()
        {
            ModInfo a = MakeMod("a"); ModInfo xl = MakeMod("xl");
            xl.DisplayName = "DW2-XL";
            a.Dependencies.Add(Dep("dw2-xl", after: true));

            var result = ModDependencies.Enforce(new[] { a, xl });

            Assert.Equal(new[] { "xl", "a" }, result.Order.Select(m => m.Id));
        }

        // ---- problems

        [Fact]
        public void Problems_RequiredModNotEnabled_IsReported_OptionalIsNot()
        {
            ModInfo a = MakeMod("a");
            a.Dependencies.Add(Dep("needed", required: true));
            a.Dependencies.Add(Dep("nice", required: false));

            List<string> problems = ModDependencies.Problems(new[] { a });

            Assert.Single(problems);
            Assert.Contains("needed", problems[0]);
        }

        [Theory]
        [InlineData("1.36.0", "1.36.0", false)]
        [InlineData("1.36.0", "v2.0.0", false)]
        [InlineData("1.36.0", "1.35.9", true)]
        [InlineData("1.36.0", "", true)]
        [InlineData("1.36.0", null, true)]
        [InlineData("1.36.0", "garbage", true)]
        [InlineData("garbage", "1.0.0", true)]
        public void Problems_MinVersion(string minVersion, string actual, bool problem)
        {
            ModInfo a = MakeMod("a");
            ModInfo xl = MakeMod("xl", actual);
            a.Dependencies.Add(Dep("xl", minVersion: minVersion));

            Assert.Equal(problem, ModDependencies.Problems(new[] { a, xl }).Count > 0);
        }

        [Fact]
        public void Problems_ReportsBothBeforeAndAfter_AndAnEntryWithNoMod()
        {
            ModInfo a = MakeMod("a"); ModInfo b = MakeMod("b");
            a.Dependencies.Add(Dep("b", after: true, before: true));
            a.Dependencies.Add(Dep(" "));

            Assert.Equal(2, ModDependencies.Problems(new[] { a, b }).Count);
        }

        [Fact]
        public void LauncherMeta_ReadsDependenciesIgnoringCase()
        {
            ModInfo a = MakeMod("a");
            File.WriteAllText(Path.Combine(a.Folder, "dw2modlauncher.json"),
                "{ \"dependencies\": [ { \"Mod\": \"xl\", \"Required\": true, \"MinVersion\": \"1.2.3\", \"LoadAfter\": true } ] }");

            LauncherDependency dep = Assert.Single(LauncherMetaReader.Read(a).dependencies);

            Assert.Equal("xl", dep.mod);
            Assert.True(dep.required);
            Assert.Equal("1.2.3", dep.minVersion);
            Assert.True(dep.loadAfter);
        }
    }
}
