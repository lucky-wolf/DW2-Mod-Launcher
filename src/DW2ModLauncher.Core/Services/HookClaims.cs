using System;
using System.Collections.Generic;
using System.Linq;
using DW2ModLauncher.Core.Models;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>
    /// Finds and checks the hook claims in the mods' declared files (docs/plans/hook-compatibility-claims.md). A claim sits in the file of the mod
    /// whose author made it and names the other mod; either file may hold the claim for a pair. A claim is checked against the patches as they
    /// are now, never trusted.
    /// </summary>
    public static class HookClaims
    {
        private static string Name(ModInfo mod) { return mod.DisplayName ?? mod.Id; }

        private static IEnumerable<HookClaim> Held(ModInfo holder, ModInfo partner, Dictionary<ModInfo, HookManifest> files, IList<ModInfo> everyone)
        {
            if (!files.TryGetValue(holder, out HookManifest manifest)) yield break;
            foreach (HookClaim claim in manifest.Claims ?? new List<HookClaim>())
                if (claim != null && !string.IsNullOrWhiteSpace(claim.Target) && !string.IsNullOrWhiteSpace(claim.Partner) && ModDependencies.Find(everyone, claim.Partner.Trim()) == partner)
                    yield return claim;
        }

        /// <summary>Whether either mod's file holds any claim about the other, on any target.</summary>
        public static bool HasAny(ModInfo a, ModInfo b, Dictionary<ModInfo, HookManifest> files, IList<ModInfo> everyone)
        {
            return Held(a, b, files, everyone).Any() || Held(b, a, files, everyone).Any();
        }

        /// <summary>
        /// How the review of this pair on this target stands, or null when nobody reviewed it. When both mods hold a claim, one that is in force
        /// wins; otherwise the one that changed is reported.
        /// </summary>
        public static ClaimCheck Check(string target, ModInfo a, ModInfo b, List<HookParticipant> sideA, List<HookParticipant> sideB,
            Dictionary<ModInfo, HookManifest> files, IList<ModInfo> everyone)
        {
            List<ClaimCheck> checks = new List<ClaimCheck>();
            foreach ((ModInfo holder, ModInfo partner, List<HookParticipant> mine, List<HookParticipant> theirs) in new[] { (a, b, sideA, sideB), (b, a, sideB, sideA) })
                foreach (HookClaim claim in Held(holder, partner, files, everyone).Where(c => string.Equals(c.Target.Trim(), target, StringComparison.Ordinal)))
                    checks.Add(Evaluate(claim, holder, partner, mine, theirs));
            return checks.FirstOrDefault(c => c.Standing == ClaimStanding.InForce) ?? checks.FirstOrDefault(c => c.Standing == ClaimStanding.Unverified) ?? checks.FirstOrDefault();
        }

        /// <summary>How one claim stands against the two sides' patches as they are now.</summary>
        public static ClaimCheck Evaluate(HookClaim claim, ModInfo holder, ModInfo partner, List<HookParticipant> mine, List<HookParticipant> theirs)
        {
            ClaimCheck check = new ClaimCheck { Claim = claim, Holder = holder, Partner = partner };
            List<string> nowMine = HookFacts.Side(mine), nowTheirs = HookFacts.Side(theirs);
            if (nowMine == null || nowTheirs == null) { check.Standing = ClaimStanding.Unverified; return check; }
            bool sameMine = HookFacts.Same(claim.Self, nowMine), sameTheirs = HookFacts.Same(claim.Other, nowTheirs);
            if (sameMine && sameTheirs) { check.Standing = ClaimStanding.InForce; return check; }
            check.Standing = ClaimStanding.Changed;
            if (!sameMine) check.Change.AddRange(HookFacts.Diff(claim.Self, nowMine).Select(l => Name(holder) + ": " + l));
            if (!sameTheirs) check.Change.AddRange(HookFacts.Diff(claim.Other, nowTheirs).Select(l => Name(partner) + ": " + l));
            return check;
        }

        /// <summary>A claim as it would be made now, from the current patches of the two sides; null when either is not known in full.</summary>
        public static HookClaim Make(string target, ModInfo holder, ModInfo partner, List<HookParticipant> mine, List<HookParticipant> theirs, string note, DateTime now)
        {
            List<string> factsMine = HookFacts.Side(mine), factsTheirs = HookFacts.Side(theirs);
            if (factsMine == null || factsTheirs == null) return null;
            return new HookClaim
            {
                Target = target,
                Partner = Name(partner),
                Self = HookFacts.ToClaimSide(factsMine),
                Other = HookFacts.ToClaimSide(factsTheirs),
                Reviewed = now.ToString("yyyy-MM-dd"),
                Note = note
            };
        }
    }
}
