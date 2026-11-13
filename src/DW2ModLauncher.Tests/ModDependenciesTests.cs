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

        // ---- naming the other mod

        [Fact]
        public void Find_WorkshopIdWinsOverTheName()
        {
            ModInfo xl = MakeMod("XL"); xl.WorkshopId = "111";
            ModInfo other = MakeMod("Other"); other.WorkshopId = "222";
            LauncherDependency both = new LauncherDependency { mod = "Other", workshopId = 111 };

            Assert.Same(xl, ModDependencies.Find(new[] { xl, other }, both));
            Assert.Same(other, ModDependencies.Find(new[] { xl, other }, new LauncherDependency { mod = "Other" }));
            Assert.Same(other, ModDependencies.Find(new[] { xl, other }, new LauncherDependency { workshopId = 222 }));
        }

        [Fact]
        public void Find_AnIdNoModHasIsNotRepairedFromTheName()
        {
            ModInfo xl = MakeMod("XL"); xl.WorkshopId = "111";
            Assert.Null(ModDependencies.Find(new[] { xl }, new LauncherDependency { mod = "XL", workshopId = 999 }));
        }

        [Fact]
        public void Problems_AnEntryWithNeitherNameNorIdIsAnError_AndIdAloneIsEnough()
        {
            ModInfo a = MakeMod("a"); ModInfo xl = MakeMod("XL"); xl.WorkshopId = "111";
            a.Dependencies.Add(new LauncherDependency());
            Assert.Single(ModDependencies.ProblemsOf(a, new[] { a, xl }, new[] { a, xl }, HookOverlapAnalyzerTests.T));

            a.Dependencies.Clear();
            a.Dependencies.Add(new LauncherDependency { workshopId = 111, required = true });
            Assert.Empty(ModDependencies.ProblemsOf(a, new[] { a, xl }, new[] { a, xl }, HookOverlapAnalyzerTests.T));
            Assert.Single(ModDependencies.ProblemsOf(a, new[] { a }, new[] { a, xl }, HookOverlapAnalyzerTests.T));
        }

        [Fact]
        public void DependencyIds_AreWrittenBesideTheNameOfALocalMod_NeverIntoAWorkshopMod()
        {
            ModInfo xl = MakeMod("XL"); xl.WorkshopId = "111";
            ModInfo local = MakeMod("local", null, ("dw2modlauncher.json", "{ \"author\": \"me\", \"dependencies\": [ { \"mod\": \"XL\", \"loadAfter\": true }, { \"mod\": \"nobody\" } ] }"));
            local.Dependencies.Add(Dep("XL", after: true)); local.Dependencies.Add(Dep("nobody"));
            ModInfo workshop = MakeMod("333", null, ("dw2modlauncher.json", "{ \"dependencies\": [ { \"mod\": \"XL\" } ] }"));
            workshop.IsWorkshop = true; workshop.Dependencies.Add(Dep("XL"));
            string workshopBefore = File.ReadAllText(Path.Combine(workshop.Folder, "dw2modlauncher.json"));

            Assert.Empty(DependencyIds.Resolve(new[] { xl, local, workshop }, new[] { local, workshop }));

            string text = File.ReadAllText(Path.Combine(local.Folder, "dw2modlauncher.json"));
            Assert.Contains("\"workshopId\": 111", text);
            Assert.Single(System.Text.RegularExpressions.Regex.Matches(text, "workshopId"));
            Assert.True(text.IndexOf("\"mod\": \"XL\"") < text.IndexOf("workshopId"));
            Assert.Equal(111, local.Dependencies[0].workshopId);
            Assert.Equal(workshopBefore, File.ReadAllText(Path.Combine(workshop.Folder, "dw2modlauncher.json")));
        }

        [Fact]
        public void DependencyIds_AddTheUidBesideTheWorkshopId_NeverRemovingEither()
        {
            const string uid = "11111111-2222-3333-4444-555555555555";
            ModInfo b = MakeMod("b"); b.WorkshopId = "222"; b.Uid = uid;
            ModInfo a = MakeMod("a", null, ("dw2modlauncher.json", "{ \"dependencies\": [ { \"mod\": \"b\", \"workshopId\": 222, \"required\": false }, { \"workshopId\": 222 } ] }"));
            a.Dependencies.Add(new LauncherDependency { mod = "b", workshopId = 222 });
            a.Dependencies.Add(new LauncherDependency { workshopId = 222 });

            Assert.Empty(DependencyIds.Resolve(new[] { a, b }, new[] { a }));

            System.Text.Json.Nodes.JsonArray deps = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(a.Folder, "dw2modlauncher.json")))["dependencies"].AsArray();
            Assert.Equal(new[] { "mod", "uid", "workshopId", "required" }, deps[0].AsObject().Select(kv => kv.Key).ToArray());
            Assert.Equal(uid, (string)deps[0]["uid"]);
            Assert.Equal(222, (long)deps[0]["workshopId"]);
            Assert.Equal(new[] { "uid", "workshopId" }, deps[1].AsObject().Select(kv => kv.Key).ToArray());
            Assert.Equal(uid, a.Dependencies[0].uid);

            // a second pass finds nothing to add
            string after = File.ReadAllText(Path.Combine(a.Folder, "dw2modlauncher.json"));
            Assert.Empty(DependencyIds.Resolve(new[] { a, b }, new[] { a }));
            Assert.Equal(after, File.ReadAllText(Path.Combine(a.Folder, "dw2modlauncher.json")));
        }

        [Fact]
        public void OwnModUids_StampsOnlyTheLocalModsOfThePlayersAuthorName_AndThenTheDependentGetsItUid()
        {
            ModInfo mine = MakeMod("mine", null, ("dw2modlauncher.json", "{ \"author\": \"Me\" }")); mine.Author = "Me"; mine.WorkshopId = "333";
            ModInfo noAuthor = MakeMod("noAuthor", null, ("dw2modlauncher.json", "{ }"));
            ModInfo theirs = MakeMod("theirs", null, ("dw2modlauncher.json", "{ \"author\": \"Them\" }")); theirs.Author = "Them";
            ModInfo workshop = MakeMod("444", null, ("dw2modlauncher.json", "{ \"author\": \"Me\" }")); workshop.Author = "me"; workshop.IsWorkshop = true;
            ModInfo broken = MakeMod("broken", null, ("dw2modlauncher.json", "{ \"author\": \"Me\" ")); broken.Author = "Me"; broken.LauncherJsonError = "bad json";
            ModInfo user = MakeMod("user", null, ("dw2modlauncher.json", "{ \"author\": \"Me\", \"dependencies\": [ { \"mod\": \"mine\", \"workshopId\": 333 } ] }"));
            user.Author = "Me"; user.Dependencies.Add(new LauncherDependency { mod = "mine", workshopId = 333 });
            string untouched = File.ReadAllText(Path.Combine(theirs.Folder, "dw2modlauncher.json"));

            ModInfo[] all = { mine, noAuthor, theirs, workshop, broken, user };
            Assert.Empty(OwnModUids.Stamp(all, "me"));

            Assert.NotNull(mine.Uid);
            Assert.NotNull(user.Uid);
            Assert.Null(noAuthor.Uid);
            Assert.Null(theirs.Uid);
            Assert.Null(workshop.Uid);
            Assert.Null(broken.Uid);
            Assert.Equal(untouched, File.ReadAllText(Path.Combine(theirs.Folder, "dw2modlauncher.json")));
            Assert.Contains(mine.Uid, File.ReadAllText(Path.Combine(mine.Folder, "dw2modlauncher.json")));

            // the same scan then completes the dependency with the fresh uid
            Assert.Empty(DependencyIds.Resolve(all, new[] { user }));
            Assert.Equal(mine.Uid, user.Dependencies[0].uid);
            Assert.Equal(333, user.Dependencies[0].workshopId);

            // with no Author setting nothing is anyone's own
            ModInfo other = MakeMod("other", null, ("dw2modlauncher.json", "{ \"author\": \"Me\" }")); other.Author = "Me";
            Assert.Empty(OwnModUids.Stamp(new[] { other }, null));
            Assert.Null(other.Uid);
        }

        // ---- order

        [Fact]
        public void Violations_NamesOnlyTheRulesTheMoverBreaks()
        {
            ModInfo a = MakeMod("a"); ModInfo b = MakeMod("b"); ModInfo c = MakeMod("c");
            a.Dependencies.Add(Dep("c", after: true));
            c.Dependencies.Add(Dep("b", before: true));   // c must load before b

            Assert.Empty(ModDependencies.Violations(new[] { b, c, a }, a, HookOverlapAnalyzerTests.T));
            Assert.Single(ModDependencies.Violations(new[] { a, b, c }, a, HookOverlapAnalyzerTests.T));
            Assert.Single(ModDependencies.Violations(new[] { b, c, a }, b, HookOverlapAnalyzerTests.T));   // b must load after c (c loads before b)
            Assert.Empty(ModDependencies.Violations(new[] { c, b, a }, a, HookOverlapAnalyzerTests.T));
        }

        [Fact]
        public void NearestValidIndex_ClampsToTheClosestPlaceTheRulesAllow()
        {
            ModInfo a = MakeMod("a"); ModInfo b = MakeMod("b"); ModInfo c = MakeMod("c"); ModInfo m = MakeMod("m");
            m.Dependencies.Add(Dep("b", after: true));
            m.Dependencies.Add(Dep("c", before: true));
            ModInfo[] others = { a, b, c };

            Assert.Equal(2, ModDependencies.NearestValidIndex(others, m, 0));   // must load after b (index 1), so slot 2
            Assert.Equal(2, ModDependencies.NearestValidIndex(others, m, 3));   // and before c (index 2)
            Assert.Equal(2, ModDependencies.NearestValidIndex(others, m, 2));
            Assert.Equal(1, ModDependencies.NearestValidIndex(new[] { a, c }, m, 5)); // no b: only "before c" binds
        }

        [Fact]
        public void Enforce_MovesAModAfterWhatItLoadsAfter()
        {
            ModInfo a = MakeMod("a"); ModInfo b = MakeMod("b"); ModInfo c = MakeMod("c");
            a.Dependencies.Add(Dep("c", after: true));

            var result = ModDependencies.Enforce(new[] { a, b, c }, HookOverlapAnalyzerTests.T);

            Assert.Equal(new[] { "b", "c", "a" }, result.Order.Select(m => m.Id));
            Assert.True(result.Changed);
            Assert.Single(result.Moves);
        }

        [Fact]
        public void Enforce_LoadBefore_MovesTheOtherModsDependentLater()
        {
            ModInfo a = MakeMod("a"); ModInfo b = MakeMod("b");
            b.Dependencies.Add(Dep("a", before: true));

            var result = ModDependencies.Enforce(new[] { b, a }, HookOverlapAnalyzerTests.T);

            Assert.Equal(new[] { "b", "a" }, result.Order.Select(m => m.Id));
            Assert.False(result.Changed);

            a.Dependencies.Add(Dep("b", before: true));
            b.Dependencies.Clear();
            result = ModDependencies.Enforce(new[] { b, a }, HookOverlapAnalyzerTests.T);
            Assert.Equal(new[] { "a", "b" }, result.Order.Select(m => m.Id));
        }

        [Fact]
        public void Enforce_LeavesAnOrderThatAlreadyMeetsTheRules()
        {
            ModInfo a = MakeMod("a"); ModInfo b = MakeMod("b"); ModInfo c = MakeMod("c");
            c.Dependencies.Add(Dep("a", after: true));

            var result = ModDependencies.Enforce(new[] { a, b, c }, HookOverlapAnalyzerTests.T);

            Assert.Equal(new[] { "a", "b", "c" }, result.Order.Select(m => m.Id));
            Assert.False(result.Changed);
        }

        [Fact]
        public void Enforce_IgnoresARuleAboutAModThatIsNotEnabled()
        {
            ModInfo a = MakeMod("a");
            a.Dependencies.Add(Dep("ghost", after: true));

            var result = ModDependencies.Enforce(new[] { a }, HookOverlapAnalyzerTests.T);

            Assert.False(result.Changed);
            Assert.Empty(result.Cycle);
        }

        [Fact]
        public void Enforce_ReportsRulesThatContradictEachOther()
        {
            ModInfo a = MakeMod("a"); ModInfo b = MakeMod("b");
            a.Dependencies.Add(Dep("b", after: true));
            b.Dependencies.Add(Dep("a", after: true));

            var result = ModDependencies.Enforce(new[] { a, b }, HookOverlapAnalyzerTests.T);

            Assert.Equal(2, result.Cycle.Count);
            Assert.Equal(new[] { "a", "b" }, result.Order.Select(m => m.Id));
        }

        [Fact]
        public void Enforce_MatchesTheOtherModByNameIgnoringCase()
        {
            ModInfo a = MakeMod("a"); ModInfo xl = MakeMod("xl");
            xl.DisplayName = "DW2-XL";
            a.Dependencies.Add(Dep("dw2-xl", after: true));

            var result = ModDependencies.Enforce(new[] { a, xl }, HookOverlapAnalyzerTests.T);

            Assert.Equal(new[] { "xl", "a" }, result.Order.Select(m => m.Id));
        }

        // ---- problems

        [Fact]
        public void Problems_RequiredModNotEnabled_IsReported_OptionalIsNot()
        {
            ModInfo a = MakeMod("a");
            a.Dependencies.Add(Dep("needed", required: true));
            a.Dependencies.Add(Dep("nice", required: false));

            List<string> problems = ModDependencies.Problems(new[] { a }, new[] { a }, HookOverlapAnalyzerTests.T);

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

            Assert.Equal(problem, ModDependencies.Problems(new[] { a, xl }, new[] { a, xl }, HookOverlapAnalyzerTests.T).Count > 0);
        }

        [Fact]
        public void Problems_ReportsBothBeforeAndAfter_AndAnEntryWithNoMod()
        {
            ModInfo a = MakeMod("a"); ModInfo b = MakeMod("b");
            a.Dependencies.Add(Dep("b", after: true, before: true));
            a.Dependencies.Add(Dep(" "));

            Assert.Equal(2, ModDependencies.Problems(new[] { a, b }, new[] { a, b }, HookOverlapAnalyzerTests.T).Count);
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
