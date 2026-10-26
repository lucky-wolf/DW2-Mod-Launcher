extern alias loader;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DW2ModLauncher.Core.Models;
using DW2ModLauncher.Core.Services;
using Xunit;
using LoaderEntry = loader::DW2ModLauncher.Loader.LoaderManifestEntry;
using LoaderManifest = loader::DW2ModLauncher.Loader.LoaderManifest;
using ObservedHooks = loader::DW2ModLauncher.Loader.ObservedHooks;

namespace DW2ModLauncher.Tests
{
    public class HookOverlapAnalyzerTests
    {
        // the English pack, so the assertions read the sentences a player reads
        internal static string T(string key) { return DW2ModLauncher.Core.Services.Localization.Get("en", key); }

        private static ModInfo Mod(string id, string author = null, string version = "1.0")
        {
            return new ModInfo { Id = id, DisplayName = id, Author = author, Version = version, Folder = "/mods/" + id };
        }

        private static (HookManifest, string) Declares(params (string Target, string Kind, string Effect)[] hooks)
        {
            return (new HookManifest { Hooks = hooks.Select(h => new DeclaredHook { Target = h.Target, Kind = h.Kind, Effect = h.Effect }).ToList() }, null);
        }

        private static HookAnalysis Run(List<ModInfo> mods, Dictionary<string, (HookManifest, string)> manifests, ObservedHooksFile observed = null, ISet<string> acknowledged = null)
        {
            return HookOverlapAnalyzer.Analyze(mods, observed, null, acknowledged, T, m => manifests.TryGetValue(m.Id, out var x) ? x : (null, null));
        }

        [Fact]
        public void TwoModsChangingTheSameResultAreACaution()
        {
            ModInfo a = Mod("A", "x"), b = Mod("B", "y");
            HookAnalysis analysis = Run(new List<ModInfo> { a, b }, new Dictionary<string, (HookManifest, string)>
            {
                ["A"] = Declares(("G:Hit(System.Int32)", "postfix", "changes-result")),
                ["B"] = Declares(("G:Hit(System.Int32)", "postfix", "changes-result")),
            });
            HookOverlap overlap = Assert.Single(analysis.Overlaps);
            Assert.Equal(HookSeverity.Caution, overlap.Severity);
        }

        [Fact]
        public void ObservingOverlapIsInfoAndSkippingOriginalIsAConflict()
        {
            ModInfo a = Mod("A", "x"), b = Mod("B", "y"), c = Mod("C", "z");
            HookAnalysis analysis = Run(new List<ModInfo> { a, b, c }, new Dictionary<string, (HookManifest, string)>
            {
                ["A"] = Declares(("G:Look()", "postfix", "observes"), ("G:Skip()", "prefix", "skips-original")),
                ["B"] = Declares(("G:Look()", "postfix", "observes")),
                ["C"] = Declares(("G:Skip()", "postfix", "observes")),
            });
            Assert.Equal(HookSeverity.Conflict, analysis.Overlaps.Single(o => o.Target == "G:Skip()").Severity);
            Assert.Equal(HookSeverity.Info, analysis.Overlaps.Single(o => o.Target == "G:Look()").Severity);
        }

        [Fact]
        public void TwoTranspilersAreACautionNotAConflict()
        {
            HookAnalysis analysis = Run(new List<ModInfo> { Mod("A", "x"), Mod("B", "y") }, new Dictionary<string, (HookManifest, string)>
            {
                ["A"] = Declares(("G:M()", "transpiler", "rewrites")),
                ["B"] = Declares(("G:M()", "transpiler", "rewrites")),
            });
            Assert.Equal(HookSeverity.Caution, Assert.Single(analysis.Overlaps).Severity);
        }

        [Fact]
        public void SameAuthorIsInfoButAnUnstatedAuthorIsNotGuessed()
        {
            var manifests = new Dictionary<string, (HookManifest, string)>
            {
                ["A"] = Declares(("G:M()", "postfix", "changes-result")),
                ["B"] = Declares(("G:M()", "postfix", "changes-result")),
            };
            Assert.Equal(HookSeverity.Info, Assert.Single(Run(new List<ModInfo> { Mod("A", "Same"), Mod("B", "same") }, manifests).Overlaps).Severity);
            Assert.Equal(HookSeverity.Caution, Assert.Single(Run(new List<ModInfo> { Mod("A"), Mod("B") }, manifests).Overlaps).Severity);
        }

        [Fact]
        public void ADeclaredCoordinationFromEitherSideMakesTheOverlapInfo()
        {
            var manifests = new Dictionary<string, (HookManifest, string)>
            {
                ["A"] = Declares(("G:M()", "prefix", "skips-original")),
                ["B"] = Declares(("G:M()", "postfix", "changes-result")),
            };
            ModInfo a = Mod("A", "x"), b = Mod("B", "y");
            Assert.Equal(HookSeverity.Conflict, Assert.Single(Run(new List<ModInfo> { a, b }, manifests).Overlaps).Severity);

            b.HooksCoordinatedWith.Add("a");
            Assert.Equal(HookSeverity.Info, Assert.Single(Run(new List<ModInfo> { a, b }, manifests).Overlaps).Severity);
        }

        [Fact]
        public void ACoordinationPinnedToAVersionLapsesWhenTheOtherModChanges()
        {
            var manifests = new Dictionary<string, (HookManifest, string)>
            {
                ["A"] = Declares(("G:M()", "prefix", "skips-original")),
                ["B"] = Declares(("G:M()", "postfix", "changes-result")),
            };
            ModInfo a = Mod("A", "x"), b = Mod("B", "y");
            a.Version = "0.4.0";
            b.HooksCoordinatedWith.Add("a@0.4.0");
            Assert.Equal(HookSeverity.Info, Assert.Single(Run(new List<ModInfo> { a, b }, manifests).Overlaps).Severity);

            a.Version = "0.5.0";
            Assert.Equal(HookSeverity.Conflict, Assert.Single(Run(new List<ModInfo> { a, b }, manifests).Overlaps).Severity);
        }

        [Fact]
        public void DeclaredLoadOrderBetweenTheModsDowngradesOneLevel()
        {
            ModInfo a = Mod("A", "x"), b = Mod("B", "y");
            b.Dependencies.Add(new LauncherDependency { mod = "A", loadAfter = true });
            HookAnalysis analysis = Run(new List<ModInfo> { a, b }, new Dictionary<string, (HookManifest, string)>
            {
                ["A"] = Declares(("G:M()", "postfix", "changes-result")),
                ["B"] = Declares(("G:M()", "postfix", "changes-result")),
            });
            Assert.Equal(HookSeverity.Info, Assert.Single(analysis.Overlaps).Severity);
        }

        [Fact]
        public void AcknowledgementHidesOnlyThatTargetAndPairAndVersion()
        {
            ModInfo a = Mod("A", "x"), b = Mod("B", "y");
            var manifests = new Dictionary<string, (HookManifest, string)>
            {
                ["A"] = Declares(("G:M()", "postfix", "changes-result"), ("G:N()", "postfix", "changes-result")),
                ["B"] = Declares(("G:M()", "postfix", "changes-result"), ("G:N()", "postfix", "changes-result")),
            };
            var acknowledged = new HashSet<string> { HookOverlapAnalyzer.AcknowledgementKey("G:M()", b, a) };
            Assert.Equal("G:N()", Assert.Single(Run(new List<ModInfo> { a, b }, manifests, null, acknowledged).Overlaps).Target);

            ModInfo newer = Mod("A", "x", "2.0");
            Assert.Equal(2, Run(new List<ModInfo> { newer, b }, manifests, null, acknowledged).Overlaps.Count);
        }

        [Fact]
        public void ObservedDataReplacesTheDeclaredManifestWhenItCoversTheInstalledVersion()
        {
            ModInfo a = Mod("A", "x"), b = Mod("B", "y");
            var manifests = new Dictionary<string, (HookManifest, string)>
            {
                ["A"] = Declares(("G:M()", "postfix", "changes-result")),
                ["B"] = Declares(("G:M()", "postfix", "changes-result")),
            };
            ObservedHooksFile observed = new ObservedHooksFile
            {
                Mods = new List<ObservedModInfo> { new ObservedModInfo { Key = a.Key, Version = "1.0" }, new ObservedModInfo { Key = b.Key, Version = "1.0" } },
                Hooks = new List<ObservedHookInfo>
                {
                    new ObservedHookInfo { Target = "G:M()", Patches = new List<ObservedPatchInfo>
                    {
                        new ObservedPatchInfo { Owner = a.Key, Kind = "postfix", Effect = "observes" },
                        new ObservedPatchInfo { Owner = b.Key, Kind = "postfix", Effect = "observes" }
                    } }
                }
            };
            Assert.Equal(HookSeverity.Info, Assert.Single(Run(new List<ModInfo> { a, b }, manifests, observed).Overlaps).Severity);
        }

        [Fact]
        public void UnknownEffectAndStaleVersionAreReportedNotGuessed()
        {
            ModInfo a = Mod("A", "x", "2.0");
            var manifests = new Dictionary<string, (HookManifest, string)>
            {
                ["A"] = (new HookManifest { ModVersion = "1.0", Hooks = new List<DeclaredHook> { new DeclaredHook { Target = "G:M()", Effect = "explodes" } } }, null)
            };
            HookAnalysis analysis = Run(new List<ModInfo> { a }, manifests);
            Assert.Contains(analysis.Notes, n => n.Text.Contains("explodes"));
            Assert.Contains(analysis.Notes, n => n.Text.Contains("may be stale"));
        }

        [Fact]
        public void ConsumerAndProviderChannelVersionsMustAgree()
        {
            ModInfo provider = Mod("P", "x"), consumer = Mod("C", "y");
            var manifests = new Dictionary<string, (HookManifest, string)>
            {
                ["P"] = (new HookManifest { Channels = new List<DeclaredChannel> { new DeclaredChannel { Name = "c", Role = "provides", Version = 2 } } }, null),
                ["C"] = (new HookManifest { Channels = new List<DeclaredChannel> { new DeclaredChannel { Name = "c", Role = "consumes", Version = 1 } } }, null),
            };
            ChannelIssue issue = Assert.Single(Run(new List<ModInfo> { provider, consumer }, manifests).ChannelIssues);
            Assert.Contains("v1", issue.Text);
            Assert.Contains("v2", issue.Text);
        }

        [Fact]
        public void ApplyFeedsTheHealthColumn()
        {
            ModInfo a = Mod("A", "x"), b = Mod("B", "y");
            HookAnalysis analysis = Run(new List<ModInfo> { a, b }, new Dictionary<string, (HookManifest, string)>
            {
                ["A"] = Declares(("G:M()", "prefix", "skips-original")),
                ["B"] = Declares(("G:M()", "postfix", "changes-result")),
            });
            HookOverlapAnalyzer.Apply(new[] { a, b }, analysis, T);
            Assert.Equal(1, a.HookConflictCount);
            Assert.Equal(3, ModHealth.Severity(a, true));
            Assert.Single(b.HookNotes);
        }

        [Fact]
        public void DriftNamesWhatTheManifestAndTheLastSessionDisagreeOn()
        {
            ModInfo a = Mod("A", "x");
            var manifests = new Dictionary<string, (HookManifest, string)> { ["A"] = Declares(("G:Old()", "postfix", "observes"), ("G:Both()", "postfix", "observes")) };
            ObservedHooksFile observed = new ObservedHooksFile
            {
                Mods = new List<ObservedModInfo> { new ObservedModInfo { Key = a.Key, Version = "1.0" } },
                Hooks = new List<ObservedHookInfo>
                {
                    new ObservedHookInfo { Target = "G:Both()", Patches = new List<ObservedPatchInfo> { new ObservedPatchInfo { Owner = a.Key, Kind = "postfix", Effect = "observes" } } },
                    new ObservedHookInfo { Target = "G:New()", Patches = new List<ObservedPatchInfo> { new ObservedPatchInfo { Owner = a.Key, Kind = "postfix", Effect = "observes" } } }
                }
            };
            HookAnalysis analysis = Run(new List<ModInfo> { a }, manifests, observed);
            Assert.Contains(analysis.Drift, d => d.Text.Contains("declared but not seen") && d.Text.Contains("G:Old()"));
            Assert.Contains(analysis.Drift, d => d.Text.Contains("not declared") && d.Text.Contains("G:New()"));
            Assert.Equal(2, analysis.Drift.Count);
        }

        [Fact]
        public void ReportsListSharedTargetsAndAcknowledgedPairsAreCounted()
        {
            ModInfo a = Mod("A", "x"), b = Mod("B", "y");
            var manifests = new Dictionary<string, (HookManifest, string)>
            {
                ["A"] = Declares(("N.G:Hit(System.Int32)", "postfix", "changes-result")),
                ["B"] = Declares(("N.G:Hit(System.Int32)", "postfix", "changes-result")),
            };
            HookAnalysis open = Run(new List<ModInfo> { a, b }, manifests);
            string report = HookReport.Full(open, T);
            Assert.Contains("N.G:Hit(System.Int32)  [Caution]", report);
            Assert.Contains("A (by x, v1.0)", report);
            Assert.Contains("shared with: B", HookReport.ForMod(open, a, T));

            HookAnalysis known = Run(new List<ModInfo> { a, b }, manifests, null, new HashSet<string> { HookOverlapAnalyzer.AcknowledgementKey("N.G:Hit(System.Int32)", a, b) });
            Assert.Empty(known.Overlaps);
            Assert.Equal(1, known.AcknowledgedCount);
            Assert.Contains("acknowledged", HookReport.Full(known, T));
        }

        // --- mods that are not enabled: their record is kept, and they are checked against the enabled mods

        private static ObservedHooksFile Session(string captured, params (ModInfo Mod, string Target, string Effect)[] patches)
        {
            ObservedHooksFile file = new ObservedHooksFile { CapturedAt = captured };
            foreach (ModInfo mod in patches.Select(p => p.Mod).Distinct())
                file.Mods.Add(new ObservedModInfo { Key = mod.Key, Version = mod.Version });
            foreach (var patch in patches)
                file.Hooks.Add(new ObservedHookInfo { Target = patch.Target, Patches = new List<ObservedPatchInfo> { new ObservedPatchInfo { Owner = patch.Mod.Key, Kind = "prefix", Effect = patch.Effect } } });
            return file;
        }

        [Fact]
        public void AModTheLastSessionDidNotLoadKeepsItsEarlierRecord()
        {
            ModInfo a = Mod("A", "x"), b = Mod("B", "y");
            ObservedHooksFile first = Session("1", (a, "G:Hit()", "skips-original"), (b, "G:Hit()", "changes-result"));
            ObservedHooksFile second = Session("2", (a, "G:Hit()", "skips-original"), (a, "G:More()", "observes"));
            ObservedHooksFile merged = HookFiles.MergeObserved(HookFiles.MergeObserved(null, first), second);

            Assert.Equal("2", merged.CapturedAt);
            Assert.Contains(merged.Mods, m => m.Key == b.Key);
            Assert.Contains(merged.Hooks.Single(h => h.Target == "G:Hit()").Patches, p => p.Owner == b.Key);
            Assert.Single(merged.Hooks.Single(h => h.Target == "G:Hit()").Patches, p => p.Owner == a.Key);
            Assert.Contains(merged.Hooks, h => h.Target == "G:More()");
        }

        [Fact]
        public void ASessionReplacesWhatItsOwnModsHadBeforeNotWhatOthersHad()
        {
            ModInfo a = Mod("A", "x"), b = Mod("B", "y");
            ObservedHooksFile first = Session("1", (a, "G:Old()", "observes"), (b, "G:B()", "observes"));
            ObservedHooksFile second = Session("2", (a, "G:New()", "observes"));
            ObservedHooksFile merged = HookFiles.MergeObserved(first, second);
            Assert.DoesNotContain(merged.Hooks, h => h.Target == "G:Old()");
            Assert.Contains(merged.Hooks, h => h.Target == "G:New()");
            Assert.Contains(merged.Hooks, h => h.Target == "G:B()");
        }

        [Fact]
        public void TheHistoryIsWrittenOnceAndRemembersAModTakenOutOfTheList()
        {
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hookhist-" + System.Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(dir);
            try
            {
                string sessionPath = System.IO.Path.Combine(dir, "hooks-observed.json"), historyPath = System.IO.Path.Combine(dir, "history.json");
                ModInfo a = Mod("A", "x"), b = Mod("B", "y");
                void WriteSession(ObservedHooksFile f) => System.IO.File.WriteAllText(sessionPath, System.Text.Json.JsonSerializer.Serialize(f));

                WriteSession(Session("1", (a, "G:Hit()", "skips-original"), (b, "G:Hit()", "skips-original")));
                Assert.Null(HookFiles.UpdateObservedHistory(sessionPath, historyPath, T).Error);
                WriteSession(Session("2", (a, "G:Hit()", "skips-original")));
                (ObservedHooksFile history, string error) = HookFiles.UpdateObservedHistory(sessionPath, historyPath, T);

                Assert.Null(error);
                Assert.Contains(history.Mods, m => m.Key == b.Key);
                // read back from disk, not only returned
                Assert.Contains(HookFiles.UpdateObservedHistory(System.IO.Path.Combine(dir, "none.json"), historyPath, T).File.Mods, m => m.Key == b.Key);
            }
            finally { System.IO.Directory.Delete(dir, true); }
        }

        [Fact]
        public void AnUnreadableHistoryIsReportedAndNotOverwritten()
        {
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hookhist-" + System.Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(dir);
            try
            {
                string historyPath = System.IO.Path.Combine(dir, "history.json");
                System.IO.File.WriteAllText(historyPath, "{ not json");
                string sessionPath = System.IO.Path.Combine(dir, "hooks-observed.json");
                System.IO.File.WriteAllText(sessionPath, System.Text.Json.JsonSerializer.Serialize(Session("1", (Mod("A"), "G:Hit()", "observes"))));
                (ObservedHooksFile file, string error) = HookFiles.UpdateObservedHistory(sessionPath, historyPath, T);
                Assert.Null(file);
                Assert.Contains(historyPath, error);
                Assert.Equal("{ not json", System.IO.File.ReadAllText(historyPath));
            }
            finally { System.IO.Directory.Delete(dir, true); }
        }

        [Fact]
        public void AModNotEnabledShowsItsClashWithAnEnabledModButTheEnabledOneIsNotMarkedDown()
        {
            ModInfo enabled = Mod("E", "x"), removed = Mod("R", "y");
            ObservedHooksFile observed = Session("1", (enabled, "G:Hit()", "changes-result"), (removed, "G:Hit()", "skips-original"));
            HookAnalysis analysis = HookOverlapAnalyzer.Analyze(new List<ModInfo> { enabled }, observed, null, null, T, m => (null, null), null, new[] { removed });
            HookOverlapAnalyzer.Apply(new[] { enabled, removed }, analysis, T);

            Assert.Equal(1, removed.HookConflictCount);
            Assert.Equal(3, ModHealth.HookSeverityIfEnabled(removed));
            Assert.Equal(0, enabled.HookConflictCount);
            Assert.Equal(0, enabled.HookCautionCount);
            Assert.Empty(enabled.HookNotes);
            Assert.Contains("E", Assert.Single(removed.HookNotes));
            Assert.Contains("R (by y, v1.0, not enabled)", HookReport.Full(analysis, T, removed));
            Assert.DoesNotContain("not enabled", HookReport.ForMod(analysis, enabled, T));
            // the report of the whole launcher is about the enabled mods only
            Assert.DoesNotContain("G:Hit()", HookReport.Full(analysis, T));
            Assert.DoesNotContain(analysis.Overlaps.SelectMany(o => o.Pairs), p => p.BothEnabled);
        }

        [Fact]
        public void AModUpdatedSinceItWasRecordedIsToldItsNewVersionHasNotBeenRun()
        {
            ModInfo e = Mod("E", "x"), updated = Mod("U", "y", "2.0");
            ObservedHooksFile observed = Session("1", (e, "G:Hit()", "observes"), (Mod("U", "y", "1.0"), "G:Hit()", "skips-original"));
            HookAnalysis analysis = HookOverlapAnalyzer.Analyze(new List<ModInfo> { e }, observed, null, null, T, m => (null, null), null, new[] { updated });
            HookNote note = Assert.Single(analysis.Notes);
            Assert.Equal(updated, note.Mod);
            Assert.Contains("version 1.0", note.Text);
            Assert.Contains("version 2.0", note.Text);
            Assert.False(note.Error);
            Assert.Empty(analysis.Overlaps);
        }

        [Fact]
        public void TwoModsThatAreBothNotEnabledAreNeverComparedAndAnUntouchedTargetIsDropped()
        {
            ModInfo e = Mod("E", "x"), r1 = Mod("R1", "y"), r2 = Mod("R2", "z");
            ObservedHooksFile observed = Session("1", (e, "G:Mine()", "observes"), (r1, "G:Hit()", "skips-original"), (r2, "G:Hit()", "skips-original"));
            HookAnalysis analysis = HookOverlapAnalyzer.Analyze(new List<ModInfo> { e }, observed, null, null, T, m => (null, null), null, new[] { r1, r2 });
            Assert.Empty(analysis.Overlaps);
            Assert.DoesNotContain("G:Hit()", analysis.ByTarget.Keys);
        }

        // --- the loader's side: owner and effect come from the shape of the code, not from the Harmony ID

        [Fact]
        public void PatchOwnerIsTheModWhoseFolderTheCodeCameFrom()
        {
            LoaderManifest manifest = new LoaderManifest
            {
                Entries = new List<LoaderEntry>
                {
                    new LoaderEntry { ModKey = "managed:A", ModFolder = "/games/mods/A" },
                    new LoaderEntry { ModKey = "managed:A-more", ModFolder = "/games/mods/A-more" }
                }
            };
            Assert.Equal("managed:A", ObservedHooks.OwnerOfPath("/games/mods/A/dll/A.dll", manifest));
            Assert.Equal("managed:A-more", ObservedHooks.OwnerOfPath("/games/mods/A-more/B.dll", manifest));
            Assert.Equal(ObservedHooks.Unknown, ObservedHooks.OwnerOfPath("/elsewhere/X.dll", manifest));
        }

        public static class Patches
        {
            public static bool SkipPrefix() => true;
            public static void ObservePrefix(object __instance) { }
            public static void ArgsPrefix(ref int amount) { amount = 0; }
            public static void ResultPostfix(ref int __result) { __result++; }
            public static void ObservePostfix(object __instance) { }
            public static System.Collections.Generic.IEnumerable<object> Transpiler(System.Collections.Generic.IEnumerable<object> codes) => codes;
        }

        [Theory]
        [InlineData("prefix", nameof(Patches.SkipPrefix), "skips-original")]
        [InlineData("prefix", nameof(Patches.ObservePrefix), "observes")]
        [InlineData("prefix", nameof(Patches.ArgsPrefix), "changes-args")]
        [InlineData("postfix", nameof(Patches.ResultPostfix), "changes-result")]
        [InlineData("postfix", nameof(Patches.ObservePostfix), "observes")]
        [InlineData("transpiler", nameof(Patches.Transpiler), "rewrites")]
        public void EffectIsReadFromThePatchMethodShape(string kind, string method, string expected)
        {
            Assert.Equal(expected, ObservedHooks.EffectOf(kind, typeof(Patches).GetMethod(method, BindingFlags.Public | BindingFlags.Static)));
        }
    }
}

namespace DW2ModLauncher.Tests
{
    public class SteamAuthorsTests
    {
        [Fact]
        public void ProfileDocumentGivesTheHandle()
        {
            Assert.Equal("Mordachai", SteamAuthors.ParsePersonaName("<profile><steamID64>1</steamID64><steamID><![CDATA[ Mordachai ]]></steamID></profile>"));
            Assert.Null(SteamAuthors.ParsePersonaName("<response><error>The specified profile could not be found.</error></response>"));
        }

        [Fact]
        public void OnlyModsWithoutAnAuthorAreNamedAndEachCreatorIsAskedOnce()
        {
            ModInfo stated = new ModInfo { Id = "1", Author = "Someone", WorkshopCreator = "111" };
            ModInfo silentA = new ModInfo { Id = "2", WorkshopCreator = "222" };
            ModInfo silentB = new ModInfo { Id = "3", WorkshopCreator = "222" };
            Dictionary<string, string> names = new Dictionary<string, string>();
            List<string> asked = new List<string>();
            List<string> failures = SteamAuthors.Resolve(new[] { stated, silentA, silentB }, names, id => { asked.Add(id); return "Handle" + id; });
            Assert.Empty(failures);
            Assert.Equal(new[] { "222" }, asked);

            SteamAuthors.Apply(new[] { stated, silentA, silentB }, names);
            Assert.Equal("Someone", stated.Author);
            Assert.False(stated.AuthorFromSteam);
            Assert.Equal("Handle222", silentA.Author);
            Assert.True(silentB.AuthorFromSteam);
        }

        [Fact]
        public void AFailedLookupIsReportedAndLeavesTheAuthorBlank()
        {
            ModInfo silent = new ModInfo { Id = "2", WorkshopCreator = "222" };
            Dictionary<string, string> names = new Dictionary<string, string>();
            List<string> failures = SteamAuthors.Resolve(new[] { silent }, names, id => throw new System.InvalidOperationException("private"));
            Assert.Contains("222", Assert.Single(failures));
            SteamAuthors.Apply(new[] { silent }, names);
            Assert.True(string.IsNullOrEmpty(silent.Author));
        }

        [Fact]
        public void ModsFromOneSteamCreatorAreByOneAuthorWhateverTheyCallThemselves()
        {
            ModInfo a = new ModInfo { Id = "a", Author = "Old Name", WorkshopCreator = "7" };
            ModInfo b = new ModInfo { Id = "b", Author = "New Name", WorkshopCreator = "7" };
            ModInfo c = new ModInfo { Id = "c", WorkshopCreator = "8" };
            Assert.True(HookOverlapAnalyzer.SameAuthor(a, b));
            Assert.False(HookOverlapAnalyzer.SameAuthor(a, c));
            Assert.False(HookOverlapAnalyzer.SameAuthor(new ModInfo(), new ModInfo()));
        }
    }
}

namespace DW2ModLauncher.Tests
{
    public class HookTextTests
    {
        // every language-pack key the hook code asks for exists in every pack, with the same named placeholders as English
        [Fact]
        public void EveryHookKeyIsTranslatedWithTheSameHoles()
        {
            string sources = string.Join("\n", new[] { "HookOverlapAnalyzer.cs", "HookReport.cs", "HookFiles.cs" }
                .Select(f => System.IO.File.ReadAllText(System.IO.Path.Combine(FindSource(), f))));
            // the quoted names that open a ("Name", value) pair are placeholders, not keys
            System.Collections.Generic.HashSet<string> placeholders = System.Text.RegularExpressions.Regex.Matches(sources, "\\(\"([A-Za-z]+)\", ").Select(m => m.Groups[1].Value).ToHashSet();
            System.Collections.Generic.List<string> keys = System.Text.RegularExpressions.Regex.Matches(sources, "(?<!cref=)\"(Hook[A-Za-z]+)\"").Select(m => m.Groups[1].Value)
                .Where(k => !placeholders.Contains(k)).Concat(new[] { "HealthCaution", "HealthConflict" }).Distinct().ToList();
            Assert.True(keys.Count > 20);
            foreach (string code in new[] { "en", "ja", "ru" })
                foreach (string key in keys)
                {
                    string text = DW2ModLauncher.Core.Services.Localization.Get(code, key);
                    Assert.False(string.IsNullOrEmpty(text) || text == key, code + " lacks " + key);
                    Assert.Equal(Holes(DW2ModLauncher.Core.Services.Localization.Get("en", key)), Holes(text));
                }
        }

        private static string Holes(string text)
        {
            return string.Join(",", DW2ModLauncher.Core.Services.NamedText.Names(text).OrderBy(x => x));
        }

        private static string FindSource()
        {
            string dir = System.AppContext.BaseDirectory;
            while (dir != null && !System.IO.Directory.Exists(System.IO.Path.Combine(dir, "src", "DW2ModLauncher.Core"))) dir = System.IO.Path.GetDirectoryName(dir);
            Assert.NotNull(dir);
            return System.IO.Path.Combine(dir, "src", "DW2ModLauncher.Core", "Services");
        }
    }
}
