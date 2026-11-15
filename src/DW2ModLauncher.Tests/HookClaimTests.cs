using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using DW2ModLauncher.Core.Models;
using DW2ModLauncher.Core.Services;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class HookClaimTests
    {
        private static string T(string key) { return HookOverlapAnalyzerTests.T(key); }

        private static ModInfo Mod(string id, string version = "1.0")
        {
            return new ModInfo { Id = id, DisplayName = id, Author = id + " author", Version = version, Folder = "/mods/" + id };
        }

        private static DeclaredHook Hook(string target, string kind, string effect, string signature = "(System.Int32 amount) -> System.Void", int priority = 400)
        {
            return new DeclaredHook { Target = target, Kind = kind, Effect = effect, Signature = signature, Priority = priority };
        }

        private static HookManifest Manifest(IEnumerable<DeclaredHook> hooks, IEnumerable<HookClaim> claims = null)
        {
            return new HookManifest { Hooks = hooks.ToList(), Claims = (claims ?? Enumerable.Empty<HookClaim>()).ToList() };
        }

        private static HookParticipant Part(DeclaredHook h)
        {
            return new HookParticipant { Kind = h.Kind, Effect = h.Effect, Signature = h.Signature, Priority = h.Priority, Before = h.Before ?? new List<string>(), After = h.After ?? new List<string>() };
        }

        private static HookClaim ClaimOf(string target, ModInfo holder, ModInfo partner, DeclaredHook mine, DeclaredHook theirs)
        {
            return HookClaims.Make(target, holder, partner, new List<HookParticipant> { Part(mine) }, new List<HookParticipant> { Part(theirs) }, null, new DateTime(2026, 11, 15));
        }

        private static HookAnalysis Run(List<ModInfo> mods, Dictionary<string, HookManifest> manifests)
        {
            return HookOverlapAnalyzer.Analyze(mods, null, null, null, T, m => manifests.TryGetValue(m.Id, out HookManifest x) ? (x, null) : (null, null));
        }

        private const string Target = "G:Hit(System.Int32)";

        [Fact]
        public void AClaimHoldsAcrossVersionsOfBothMods()
        {
            ModInfo a = Mod("A"), b = Mod("B");
            DeclaredHook ha = Hook(Target, "prefix", "skips-original"), hb = Hook(Target, "postfix", "changes-result");
            Dictionary<string, HookManifest> files = new Dictionary<string, HookManifest>
            {
                ["A"] = Manifest(new[] { ha }),
                ["B"] = Manifest(new[] { hb }, new[] { ClaimOf(Target, b, a, hb, ha) })
            };
            Assert.Equal(HookSeverity.Info, Assert.Single(Run(new List<ModInfo> { a, b }, files).Overlaps).Severity);

            a.Version = "9.9";
            b.Version = "7.7";
            HookAnalysis later = Run(new List<ModInfo> { a, b }, files);
            Assert.Equal(HookSeverity.Info, Assert.Single(later.Overlaps).Severity);
            Assert.Equal(1, later.ClaimedCount);
            Assert.Empty(later.ClaimProblems);
        }

        [Fact]
        public void EitherModsFileMayHoldTheClaim()
        {
            ModInfo a = Mod("A"), b = Mod("B");
            DeclaredHook ha = Hook(Target, "prefix", "skips-original"), hb = Hook(Target, "postfix", "changes-result");
            Dictionary<string, HookManifest> files = new Dictionary<string, HookManifest>
            {
                ["A"] = Manifest(new[] { ha }, new[] { ClaimOf(Target, a, b, ha, hb) }),
                ["B"] = Manifest(new[] { hb })
            };
            Assert.Equal(HookSeverity.Info, Assert.Single(Run(new List<ModInfo> { a, b }, files).Overlaps).Severity);
        }

        [Theory]
        [InlineData("signature")]
        [InlineData("kind")]
        [InlineData("priority")]
        [InlineData("extra patch")]
        public void AChangeToEitherSidesPatchesDropsTheClaimAndSaysWhat(string change)
        {
            ModInfo a = Mod("A"), b = Mod("B");
            DeclaredHook ha = Hook(Target, "prefix", "skips-original"), hb = Hook(Target, "postfix", "changes-result");
            HookClaim claim = ClaimOf(Target, b, a, hb, ha);
            List<DeclaredHook> nowB = new List<DeclaredHook> { hb };
            switch (change)
            {
                case "signature": nowB[0] = Hook(Target, "postfix", "changes-result", "(ref System.Int32 amount) -> System.Void"); break;
                case "kind": nowB[0] = Hook(Target, "prefix", "skips-original"); break;
                case "priority": nowB[0] = Hook(Target, "postfix", "changes-result", priority: 200); break;
                case "extra patch": nowB.Add(Hook(Target, "transpiler", "rewrites")); break;
            }
            HookAnalysis analysis = Run(new List<ModInfo> { a, b }, new Dictionary<string, HookManifest> { ["A"] = Manifest(new[] { ha }), ["B"] = Manifest(nowB, new[] { claim }) });

            HookPair pair = Assert.Single(Assert.Single(analysis.Overlaps).Pairs);
            Assert.True(pair.Severity > HookSeverity.Info, "graded as if never reviewed");
            Assert.NotEmpty(pair.ClaimChange);
            Assert.All(pair.ClaimChange, line => Assert.StartsWith("B: ", line));
            ClaimCheck problem = Assert.Single(analysis.ClaimProblems);
            Assert.Equal(ClaimStanding.Changed, problem.Standing);
            HookNote note = Assert.Single(analysis.Notes, n => n.Text.Contains("changed since"));
            Assert.Equal(b, note.Mod);
        }

        [Fact]
        public void AChangeInThePartnersPatchAlsoDropsIt()
        {
            ModInfo a = Mod("A"), b = Mod("B");
            DeclaredHook ha = Hook(Target, "prefix", "skips-original"), hb = Hook(Target, "postfix", "changes-result");
            HookClaim claim = ClaimOf(Target, b, a, hb, ha);
            DeclaredHook nowA = Hook(Target, "prefix", "skips-original", "(System.Int32 amount, System.Object __instance) -> System.Boolean");
            HookAnalysis analysis = Run(new List<ModInfo> { a, b }, new Dictionary<string, HookManifest> { ["A"] = Manifest(new[] { nowA }), ["B"] = Manifest(new[] { hb }, new[] { claim }) });
            Assert.Equal(HookSeverity.Conflict, Assert.Single(analysis.Overlaps).Severity);
            Assert.All(Assert.Single(analysis.ClaimProblems).Change, line => Assert.StartsWith("A: ", line));
        }

        [Fact]
        public void ADifferentOverloadIsADifferentTargetSoItIsNotCovered()
        {
            ModInfo a = Mod("A"), b = Mod("B");
            DeclaredHook ha = Hook(Target, "prefix", "skips-original"), hb = Hook(Target, "postfix", "changes-result");
            HookClaim claim = ClaimOf(Target, b, a, hb, ha);
            string other = "G:Hit(System.Int32,System.Single)";
            HookAnalysis analysis = Run(new List<ModInfo> { a, b }, new Dictionary<string, HookManifest>
            {
                ["A"] = Manifest(new[] { Hook(other, "prefix", "skips-original") }),
                ["B"] = Manifest(new[] { Hook(other, "postfix", "changes-result") }, new[] { claim })
            });
            Assert.Equal(HookSeverity.Conflict, Assert.Single(analysis.Overlaps).Severity);
            Assert.Empty(analysis.ClaimProblems);
        }

        [Fact]
        public void APatchWithNoSignatureCannotBeCheckedAndIsSaidSo()
        {
            ModInfo a = Mod("A"), b = Mod("B");
            DeclaredHook ha = Hook(Target, "prefix", "skips-original"), hb = Hook(Target, "postfix", "changes-result");
            HookClaim claim = ClaimOf(Target, b, a, hb, ha);
            DeclaredHook unsigned = Hook(Target, "prefix", "skips-original", signature: null);
            HookAnalysis analysis = Run(new List<ModInfo> { a, b }, new Dictionary<string, HookManifest> { ["A"] = Manifest(new[] { unsigned }), ["B"] = Manifest(new[] { hb }, new[] { claim }) });
            Assert.Equal(HookSeverity.Info, Assert.Single(analysis.Overlaps).Severity);
            Assert.Equal(ClaimStanding.Unverified, Assert.Single(analysis.ClaimProblems).Standing);
            Assert.Contains(analysis.Notes, n => n.Text.Contains("cannot be checked"));
        }

        [Fact]
        public void APairWithClaimsIgnoresTheOlderPinnedEntryForTargetsNotReviewed()
        {
            ModInfo a = Mod("A"), b = Mod("B");
            b.HooksCoordinatedWith.Add("A");
            string second = "G:Other()";
            DeclaredHook ha = Hook(Target, "prefix", "skips-original"), hb = Hook(Target, "postfix", "changes-result");
            HookClaim claim = ClaimOf(Target, b, a, hb, ha);
            Dictionary<string, HookManifest> files = new Dictionary<string, HookManifest>
            {
                ["A"] = Manifest(new[] { ha, Hook(second, "prefix", "skips-original") }),
                ["B"] = Manifest(new[] { hb, Hook(second, "postfix", "changes-result") }, new[] { claim })
            };
            HookAnalysis analysis = Run(new List<ModInfo> { a, b }, files);
            Assert.Equal(HookSeverity.Info, analysis.Overlaps.Single(o => o.Target == Target).Severity);
            Assert.Equal(HookSeverity.Conflict, analysis.Overlaps.Single(o => o.Target == second).Severity);
        }

        [Fact]
        public void TheFactsDigestIsStableAndTheOrderOfPatchesDoesNotMatter()
        {
            DeclaredHook h1 = Hook(Target, "prefix", "skips-original"), h2 = Hook(Target, "postfix", "changes-result");
            List<string> one = HookFacts.Side(new[] { Part(h1), Part(h2) }), two = HookFacts.Side(new[] { Part(h2), Part(h1) });
            Assert.Equal(one, two);
            Assert.Equal(HookFacts.Hash(one), HookFacts.Hash(two));
            Assert.Equal(12, HookFacts.Hash(one).Length);
        }

        // ---- migration of the version-pinned "Compatible with" entries

        private static HookClaimMigrationResult Migrate(List<ModInfo> mods, Dictionary<string, HookManifest> files, Dictionary<string, List<HookClaim>> written)
        {
            return HookClaimMigration.Run(mods, null, T, new DateTime(2026, 11, 15), m => files.TryGetValue(m.Id, out HookManifest x) ? (x, null) : (null, null),
                (mod, claims) => { written[mod.Id] = claims.ToList(); files[mod.Id].Claims = claims.ToList(); });
        }

        [Fact]
        public void AHoldingPinBecomesClaimsOnEveryOverlapAndThenKeepsTheOverlapsInfo()
        {
            ModInfo a = Mod("A", "2.0"), b = Mod("B");
            b.HooksCoordinatedWith.Add("A@2.0");
            DeclaredHook ha = Hook(Target, "prefix", "skips-original"), hb = Hook(Target, "postfix", "changes-result");
            Dictionary<string, HookManifest> files = new Dictionary<string, HookManifest> { ["A"] = Manifest(new[] { ha }), ["B"] = Manifest(new[] { hb }) };
            Dictionary<string, List<HookClaim>> written = new Dictionary<string, List<HookClaim>>();

            HookClaimMigrationResult result = Migrate(new List<ModInfo> { a, b }, files, written);
            Assert.Equal(1, Assert.Single(result.Written).Claims);
            Assert.Equal(Target, Assert.Single(written["B"]).Target);
            Assert.Equal(HookSeverity.Info, Assert.Single(Run(new List<ModInfo> { a, b }, files).Overlaps).Severity);

            // converted once: a second run changes nothing
            Assert.Empty(Migrate(new List<ModInfo> { a, b }, files, written).Written);

            // from now on the claim, not the pin, decides: a change to A's hook is noticed although the entry is still there
            files["A"] = Manifest(new[] { Hook(Target, "prefix", "skips-original", "(System.Int32 amount, System.Object __instance) -> System.Boolean") });
            Assert.Equal(HookSeverity.Conflict, Assert.Single(Run(new List<ModInfo> { a, b }, files).Overlaps).Severity);
        }

        [Fact]
        public void APinnedEntryForAnotherVersionHoldsNothingSoNothingIsConverted()
        {
            ModInfo a = Mod("A", "3.0"), b = Mod("B");
            b.HooksCoordinatedWith.Add("A@2.0");
            Dictionary<string, HookManifest> files = new Dictionary<string, HookManifest>
            {
                ["A"] = Manifest(new[] { Hook(Target, "prefix", "skips-original") }),
                ["B"] = Manifest(new[] { Hook(Target, "postfix", "changes-result") })
            };
            Assert.Empty(Migrate(new List<ModInfo> { a, b }, files, new Dictionary<string, List<HookClaim>>()).Written);
        }

        [Fact]
        public void APairIsConvertedWholeOrNotAtAll()
        {
            ModInfo a = Mod("A"), b = Mod("B");
            b.HooksCoordinatedWith.Add("A");
            string second = "G:Other()";
            Dictionary<string, HookManifest> files = new Dictionary<string, HookManifest>
            {
                ["A"] = Manifest(new[] { Hook(Target, "prefix", "skips-original"), Hook(second, "prefix", "skips-original", signature: null) }),
                ["B"] = Manifest(new[] { Hook(Target, "postfix", "changes-result"), Hook(second, "postfix", "changes-result") })
            };
            Assert.Empty(Migrate(new List<ModInfo> { a, b }, files, new Dictionary<string, List<HookClaim>>()).Written);
            Assert.Empty(files["B"].Claims);
        }

        [Fact]
        public void AWorkshopModIsNeverWritten()
        {
            ModInfo a = Mod("A"), b = Mod("B");
            b.IsWorkshop = true;
            b.HooksCoordinatedWith.Add("A");
            Dictionary<string, HookManifest> files = new Dictionary<string, HookManifest>
            {
                ["A"] = Manifest(new[] { Hook(Target, "prefix", "skips-original") }),
                ["B"] = Manifest(new[] { Hook(Target, "postfix", "changes-result") })
            };
            Assert.Empty(Migrate(new List<ModInfo> { a, b }, files, new Dictionary<string, List<HookClaim>>()).Written);
        }

        // ---- the file

        [Fact]
        public void WritingClaimsKeepsTheRestOfTheHooksFileAndTheClaimsReadBack()
        {
            string folder = Path.Combine(Path.GetTempPath(), "claims-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                File.WriteAllText(Path.Combine(folder, HookFiles.ManifestFileName), "{ \"schema\": 1, \"modVersion\": \"1.2\", \"hooks\": [ { \"target\": \"G:M()\", \"kind\": \"prefix\", \"effect\": \"observes\" } ] }");
                ModInfo b = Mod("B");
                b.Folder = folder;
                ModInfo a = Mod("A");
                DeclaredHook ha = Hook(Target, "prefix", "skips-original"), hb = Hook(Target, "postfix", "changes-result");
                HookFiles.WriteClaims(b, new[] { ClaimOf(Target, b, a, hb, ha) });

                JsonObject root = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, HookFiles.ManifestFileName))).AsObject();
                Assert.Equal("1.2", (string)root["modVersion"]);
                Assert.Single(root["hooks"].AsArray());
                (HookManifest read, string error) = HookFiles.ReadManifest(b, T);
                Assert.Null(error);
                HookClaim claim = Assert.Single(read.Claims);
                Assert.Equal("A", claim.Partner);
                Assert.True(HookFacts.Same(claim.Self, HookFacts.Side(new[] { Part(hb) })));

                HookFiles.WriteClaims(b, new HookClaim[0]);
                Assert.False(JsonNode.Parse(File.ReadAllText(Path.Combine(folder, HookFiles.ManifestFileName))).AsObject().ContainsKey("claims"));
            }
            finally { Directory.Delete(folder, true); }
        }
    }
}
