using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using DW2ModLauncher.Core.Models;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>How a hook claim stands against the patches as they are now.</summary>
    public enum ClaimStanding
    {
        /// <summary>Both sides are exactly as reviewed.</summary>
        InForce,
        /// <summary>A side's patches differ from the reviewed ones.</summary>
        Changed,
        /// <summary>A side's patches are not known in full (its mod has not run, and its declared file lacks the signature), so nothing can be compared.</summary>
        Unverified
    }

    /// <summary>The outcome of checking one claim; Change says what differs when it has <see cref="ClaimStanding.Changed"/>.</summary>
    public class ClaimCheck
    {
        public ClaimStanding Standing { get; set; }
        public HookClaim Claim { get; set; }
        /// <summary>The mod whose file holds the claim.</summary>
        public ModInfo Holder { get; set; }
        public ModInfo Partner { get; set; }
        /// <summary>Facts the claim lists that are gone, then facts that are new, one per line, each prefixed by its mod; empty unless Changed.</summary>
        public List<string> Change { get; set; } = new List<string>();
    }

    /// <summary>
    /// What a hook claim compares (docs/plans/hook-compatibility-claims.md): per mod and target, the sorted list of its patches as text lines
    /// (kind, effect, priority, before/after, the patch method's signature). Harmony ids are not part of it, they are free-form; the mod's key is
    /// the line's owner by being the claim's side. Text, not just a digest, so a change can be shown.
    /// </summary>
    public static class HookFacts
    {
        /// <summary>One patch as a line, or null when it cannot be stated in full (no signature recorded).</summary>
        public static string Fact(HookParticipant p)
        {
            if (string.IsNullOrWhiteSpace(p.Signature) || p.Priority == null) return null;
            return (p.Kind ?? "?") + " | " + (p.Effect ?? "?") + " | prio " + p.Priority + " | before [" + string.Join(",", Sorted(p.Before)) + "] | after [" + string.Join(",", Sorted(p.After)) + "] | " + p.Signature.Trim();
        }

        private static IEnumerable<string> Sorted(IEnumerable<string> list)
        {
            return (list ?? Enumerable.Empty<string>()).OrderBy(x => x, StringComparer.Ordinal);
        }

        /// <summary>The facts of a mod's patches on one target, sorted; null when any of them is incomplete.</summary>
        public static List<string> Side(IEnumerable<HookParticipant> patches)
        {
            List<string> facts = new List<string>();
            foreach (HookParticipant p in patches)
            {
                string fact = Fact(p);
                if (fact == null) return null;
                facts.Add(fact);
            }
            facts.Sort(StringComparer.Ordinal);
            return facts;
        }

        /// <summary>A short digest of the facts (12 hex digits), what a claim carries for transport and quick comparison.</summary>
        public static string Hash(IEnumerable<string> facts)
        {
            byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", facts)));
            return Convert.ToHexString(bytes, 0, 6).ToLowerInvariant();
        }

        public static ClaimSide ToClaimSide(List<string> facts)
        {
            return new ClaimSide { Hash = Hash(facts), Facts = facts.ToList() };
        }

        /// <summary>Whether a side as reviewed is what the side is now. The digest decides; the facts are only for showing the difference.</summary>
        public static bool Same(ClaimSide reviewed, List<string> now)
        {
            return reviewed != null && string.Equals(reviewed.Hash, Hash(now), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Lines that are only in the reviewed list ("-"), then only in the current one ("+").</summary>
        public static IEnumerable<string> Diff(ClaimSide reviewed, List<string> now)
        {
            List<string> was = reviewed?.Facts ?? new List<string>();
            foreach (string gone in was.Except(now, StringComparer.Ordinal)) yield return "- " + gone;
            foreach (string added in now.Except(was, StringComparer.Ordinal)) yield return "+ " + added;
        }
    }
}
