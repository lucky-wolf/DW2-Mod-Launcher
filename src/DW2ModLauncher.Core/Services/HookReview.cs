using System;
using System.Collections.Generic;
using System.Linq;
using DW2ModLauncher.Core.Models;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>One hook an author can mark compatible: this mod's patches and another mod's on one target, with the claim as it would be made now.</summary>
    public class HookReviewItem
    {
        public string Target { get; set; }
        public ModInfo Partner { get; set; }
        /// <summary>True when a claim existed and a side changed since; false when the pair was never reviewed on this target.</summary>
        public bool Changed { get; set; }
        public HookSeverity Severity { get; set; }
        /// <summary>False when the partner is not enabled: the overlap is what enabling it would bring (the mod's health ignores it).</summary>
        public bool BothEnabled { get; set; } = true;
        /// <summary>What differs from the reviewed patches (empty unless <see cref="Changed"/>).</summary>
        public List<string> Change { get; set; } = new List<string>();
        /// <summary>The claim made from the patches as they are now.</summary>
        public HookClaim Claim { get; set; }
    }

    /// <summary>
    /// What the "Compatible with" list of the Edit Properties and Publish dialogs works from (docs/plans/hook-compatibility-claims.md): the hooks
    /// of one mod that overlap another mod's and have not been marked (or whose mark no longer matches), and how one of its marks stands.
    /// </summary>
    public static class HookReview
    {
        /// <summary>
        /// The mod's overlaps that bite (or that its own claim holds at Info, or whose claim changed), most severe first. <paramref name="wanted"/> says
        /// which partners count (a mod nobody else has installed has nothing to mark). Each item has the claim as it would be made now, or none
        /// (<see cref="HookReviewItem.Claim"/> null) when a side's patches are not known in full: until both mods have run it cannot be marked.
        /// </summary>
        public static List<HookReviewItem> Candidates(HookAnalysis analysis, ModInfo mod, Func<ModInfo, bool> wanted, DateTime now)
        {
            List<HookReviewItem> items = new List<HookReviewItem>();
            foreach (HookOverlap overlap in analysis.Overlaps)
                foreach (HookPair pair in overlap.Pairs.Where(p => (p.A == mod || p.B == mod) && (p.Severity > HookSeverity.Info || p.ClaimChange.Count > 0 || p.ClaimHolder == mod)))
                {
                    ModInfo partner = pair.A == mod ? pair.B : pair.A;
                    if (!wanted(partner)) continue;
                    HookClaim claim = HookClaims.Make(overlap.Target, mod, partner, overlap.Participants.Where(p => p.Mod == mod).ToList(), overlap.Participants.Where(p => p.Mod == partner).ToList(), null, now);
                    items.Add(new HookReviewItem { Target = overlap.Target, Partner = partner, Changed = pair.ClaimChange.Count > 0, Severity = pair.Graded, Change = pair.ClaimChange, Claim = claim, BothEnabled = pair.BothEnabled });
                }
            return items.OrderByDescending(i => i.Severity).ThenBy(i => i.Partner.DisplayName ?? i.Partner.Id, StringComparer.OrdinalIgnoreCase).ThenBy(i => i.Target, StringComparer.Ordinal).ToList();
        }

        /// <summary>One mod's patches on a target, one line each (the facts a claim compares, or kind and effect when a patch is not known in full).</summary>
        public static List<string> PatchLines(HookAnalysis analysis, string target, ModInfo mod)
        {
            if (!analysis.ByTarget.TryGetValue(target ?? "", out List<HookParticipant> patches)) return new List<string>();
            return patches.Where(p => p.Mod == mod).Select(p => HookFacts.Fact(p) ?? (p.Kind + " | " + p.Effect)).OrderBy(x => x, StringComparer.Ordinal).ToList();
        }

        /// <summary>
        /// Everything seen about one overlap, as plain text to hover over or paste into a message: the method in full, then each mod's patches (headed by
        /// <paramref name="sideLabel"/>), then what changed since the mark when it did (headed by <paramref name="changedLabel"/>).
        /// </summary>
        public static string Describe(HookAnalysis analysis, string target, ModInfo mod, ModInfo partner, IList<string> change, Func<ModInfo, string> sideLabel, string changedLabel)
        {
            List<string> lines = new List<string> { target, "" };
            foreach (ModInfo side in new[] { partner, mod })
            {
                lines.Add(sideLabel(side));
                lines.AddRange(PatchLines(analysis, target, side).Select(l => "    " + l));
            }
            if (change != null && change.Count > 0)
            {
                lines.Add("");
                lines.Add(changedLabel);
                lines.AddRange(change.Select(l => "    " + l));
            }
            return string.Join("\n", lines);
        }

        /// <summary>The number of parameters in Harmony's spelling of a target, Namespace.Type:Method(A,B).</summary>
        public static int ArgumentCount(string target)
        {
            int open = (target ?? "").IndexOf('(');
            if (open < 0) return 0;
            string args = target.Substring(open + 1).TrimEnd(')').Trim();
            if (args.Length == 0) return 0;
            // a generic argument's own commas (Dictionary`2[System.Int32,System.String]) are not separators
            int depth = 0, count = 1;
            foreach (char c in args)
            {
                if (c == '[') depth++;
                else if (c == ']') depth--;
                else if (c == ',' && depth == 0) count++;
            }
            return count;
        }

        /// <summary>How the mod's mark stands against the patches now; null when the two mods no longer both patch that target (nothing to compare).</summary>
        public static ClaimCheck Check(HookClaim claim, ModInfo mod, ModInfo partner, HookAnalysis analysis)
        {
            if (!analysis.ByTarget.TryGetValue(claim.Target ?? "", out List<HookParticipant> patches)) return null;
            List<HookParticipant> mine = patches.Where(p => p.Mod == mod).ToList(), theirs = patches.Where(p => p.Mod == partner).ToList();
            return mine.Count == 0 || theirs.Count == 0 ? null : HookClaims.Evaluate(claim, mod, partner, mine, theirs);
        }

        /// <summary>The mark made again from the patches as they are now (its note kept); null when they are not known in full or the mods no longer both patch it.</summary>
        public static HookClaim Restamp(HookClaim claim, ModInfo mod, ModInfo partner, HookAnalysis analysis, DateTime now)
        {
            if (!analysis.ByTarget.TryGetValue(claim.Target ?? "", out List<HookParticipant> patches)) return null;
            List<HookParticipant> mine = patches.Where(p => p.Mod == mod).ToList(), theirs = patches.Where(p => p.Mod == partner).ToList();
            return mine.Count == 0 || theirs.Count == 0 ? null : HookClaims.Make(claim.Target, mod, partner, mine, theirs, claim.Note, now);
        }
    }
}
