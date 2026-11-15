using System;
using System.Collections.Generic;
using System.Linq;
using DW2ModLauncher.Core.Models;
using DW2ModLauncher.Core.Services;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class HookReviewTests
    {
        private static string T(string key) { return HookOverlapAnalyzerTests.T(key); }
        private const string Target = "G:Hit(System.Int32)";
        private static readonly DateTime Now = new DateTime(2026, 11, 15);

        private static ModInfo Mod(string id)
        {
            return new ModInfo { Id = id, DisplayName = id, Author = id + " author", Version = "1.0", Folder = "/mods/" + id };
        }

        private static HookManifest Declares(string kind, string effect, string signature = "(System.Int32 amount) -> System.Void")
        {
            return new HookManifest { Hooks = new List<DeclaredHook> { new DeclaredHook { Target = Target, Kind = kind, Effect = effect, Signature = signature, Priority = 400 } } };
        }

        private static HookAnalysis Run(List<ModInfo> mods, Dictionary<string, HookManifest> files)
        {
            return HookOverlapAnalyzer.Analyze(mods, null, null, null, T, m => files.TryGetValue(m.Id, out HookManifest x) ? (x, null) : (null, null));
        }

        private static List<HookReviewItem> Open(HookAnalysis analysis, ModInfo mod)
        {
            return HookReview.Candidates(analysis, mod, m => true, Now);
        }

        [Fact]
        public void AnOpenOverlapIsACandidateAndMarkingItMakesTheOverlapInfo()
        {
            ModInfo a = Mod("A"), b = Mod("B");
            List<ModInfo> mods = new List<ModInfo> { a, b };
            Dictionary<string, HookManifest> files = new Dictionary<string, HookManifest> { ["A"] = Declares("prefix", "skips-original"), ["B"] = Declares("postfix", "changes-result") };

            HookReviewItem item = Assert.Single(Open(Run(mods, files), a));
            Assert.False(item.Changed);
            Assert.Equal(b, item.Partner);
            Assert.Equal(Target, item.Claim.Target);

            files["A"].Claims.Add(item.Claim);
            HookAnalysis after = Run(mods, files);
            Assert.Equal(HookSeverity.Info, Assert.Single(after.Overlaps).Severity);
            // still listed for the holder, at the severity it has when unmarked (so removing the mark shows what comes back)
            Assert.Equal(HookSeverity.Conflict, Assert.Single(Open(after, a)).Severity);
            // and never for the partner, whose file holds nothing
            Assert.Empty(Open(after, b));
        }

        [Fact]
        public void ACheckSaysHowAMarkStandsAndRestampingBringsItUpToDate()
        {
            ModInfo a = Mod("A"), b = Mod("B");
            List<ModInfo> mods = new List<ModInfo> { a, b };
            Dictionary<string, HookManifest> files = new Dictionary<string, HookManifest> { ["A"] = Declares("prefix", "skips-original"), ["B"] = Declares("postfix", "changes-result") };
            HookClaim claim = Open(Run(mods, files), a).Single().Claim;
            Assert.Equal(ClaimStanding.InForce, HookReview.Check(claim, a, b, Run(mods, files)).Standing);

            files["B"] = Declares("postfix", "changes-result", "(ref System.Int32 amount) -> System.Void");
            HookAnalysis changed = Run(mods, files);
            ClaimCheck check = HookReview.Check(claim, a, b, changed);
            Assert.Equal(ClaimStanding.Changed, check.Standing);
            Assert.NotEmpty(check.Change);

            claim.Note = "looked at it";
            HookClaim fresh = HookReview.Restamp(claim, a, b, changed, Now);
            Assert.Equal("looked at it", fresh.Note);
            Assert.Equal(ClaimStanding.InForce, HookReview.Check(fresh, a, b, changed).Standing);
        }

        [Fact]
        public void AMarkForATargetNobodyPatchesAnymoreHasNothingToCheck()
        {
            ModInfo a = Mod("A"), b = Mod("B");
            Dictionary<string, HookManifest> files = new Dictionary<string, HookManifest> { ["A"] = Declares("prefix", "skips-original"), ["B"] = Declares("postfix", "changes-result") };
            HookClaim claim = Open(Run(new List<ModInfo> { a, b }, files), a).Single().Claim;
            files["B"] = new HookManifest();
            Assert.Null(HookReview.Check(claim, a, b, Run(new List<ModInfo> { a, b }, files)));
            Assert.Null(HookReview.Restamp(claim, a, b, Run(new List<ModInfo> { a, b }, files), Now));
        }

        [Fact]
        public void OnlyWantedPartnersCountAndOverlapsNotKnownInFullCannotBeMarked()
        {
            ModInfo a = Mod("A"), b = Mod("B");
            Dictionary<string, HookManifest> files = new Dictionary<string, HookManifest> { ["A"] = Declares("prefix", "skips-original"), ["B"] = Declares("postfix", "changes-result") };
            HookAnalysis analysis = Run(new List<ModInfo> { a, b }, files);
            Assert.Empty(HookReview.Candidates(analysis, a, m => false, Now));

            files["B"] = Declares("postfix", "changes-result", signature: null);
            HookReviewItem item = Assert.Single(Open(Run(new List<ModInfo> { a, b }, files), a));
            Assert.Null(item.Claim);
        }

        [Fact]
        public void TheFactsOfASideAndTheArgumentCountAreReadable()
        {
            ModInfo a = Mod("A"), b = Mod("B");
            Dictionary<string, HookManifest> files = new Dictionary<string, HookManifest> { ["A"] = Declares("prefix", "skips-original"), ["B"] = Declares("postfix", "changes-result") };
            HookAnalysis analysis = Run(new List<ModInfo> { a, b }, files);
            Assert.StartsWith("prefix | skips-original | prio 400", Assert.Single(HookReview.PatchLines(analysis, Target, a)));
            Assert.Equal(1, HookReview.ArgumentCount(Target));
            Assert.Equal(0, HookReview.ArgumentCount("G:M()"));
            Assert.Equal(3, HookReview.ArgumentCount("G:M(System.Int32,System.Single,A.B)"));
        }
    }
}
