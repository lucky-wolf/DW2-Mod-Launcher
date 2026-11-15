using System;
using System.Collections.Generic;
using System.Linq;
using DW2ModLauncher.Core.Diagnostics;
using DW2ModLauncher.Core.Models;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>What one run of the migration did, for the caller to report.</summary>
    public class HookClaimMigrationResult
    {
        /// <summary>Mods whose hooks file gained claims, with how many.</summary>
        public List<(ModInfo Mod, int Claims)> Written { get; } = new List<(ModInfo, int)>();
        public List<string> Problems { get; } = new List<string>();
    }

    /// <summary>
    /// Turns the version-pinned "Compatible with" entries (dw2modlauncher.json "hooksCoordinatedWith") that hold today into hook claims, once the
    /// patches of both mods are known in full (docs/plans/hook-compatibility-claims.md). The entries stay in the file; a pair that has claims is
    /// graded by them alone. A pair is converted whole or not at all, so a half-converted pair never leaves a target unreviewed that was reviewed.
    /// Only mods installed locally are written; a Workshop copy is somebody else's file. Running it again changes nothing once everything is converted.
    /// </summary>
    // PHASE-OUT: the old whole-mod "Compatible with" (hooksCoordinatedWith) goes about three releases after hook claims ship; checklist in docs/plans/hook-compatibility-claims.md.
    public static class HookClaimMigration
    {
        public static HookClaimMigrationResult Run(IList<ModInfo> allMods, ObservedHooksFile observed, Func<string, string> t, DateTime now,
            Func<ModInfo, (HookManifest Manifest, string Error)> readManifest = null, Action<ModInfo, IList<HookClaim>> write = null)
        {
            HookClaimMigrationResult result = new HookClaimMigrationResult();
            readManifest = readManifest ?? (m => HookFiles.ReadManifest(m, t));
            write = write ?? HookFiles.WriteClaims;
            List<ModInfo> holders = allMods.Where(m => m != null && !m.IsWorkshop && (m.HooksCoordinatedWith ?? new List<string>()).Any(e => !string.IsNullOrWhiteSpace(e))).ToList();
            if (holders.Count == 0) return result;

            // every patch of every mod, by target, as the analyzer sees them (all counted as enabled, settings not consulted)
            HookAnalysis everything = HookOverlapAnalyzer.Analyze(allMods, observed, null, null, t, readManifest);

            foreach (ModInfo holder in holders)
            {
                (HookManifest manifest, string error) = readManifest(holder);
                if (error != null) { result.Problems.Add(error); continue; }
                List<HookClaim> claims = (manifest?.Claims ?? new List<HookClaim>()).Where(c => c != null).ToList();
                int before = claims.Count;
                foreach (string entry in holder.HooksCoordinatedWith.Where(e => !string.IsNullOrWhiteSpace(e)))
                {
                    ModInfo partner = ModDependencies.Find(allMods, HookOverlapAnalyzer.SplitCoordinationEntry(entry).Name);
                    // an entry for an uninstalled mod, or pinned to another version than the installed one, holds nothing now
                    if (partner == null || partner == holder || !HookOverlapAnalyzer.Coordinates(entry, partner, allMods) || HookOverlapAnalyzer.SameAuthor(holder, partner)) continue;
                    List<HookClaim> made = new List<HookClaim>();
                    bool whole = true;
                    foreach (KeyValuePair<string, List<HookParticipant>> target in everything.ByTarget.OrderBy(x => x.Key, StringComparer.Ordinal))
                    {
                        List<HookParticipant> mine = target.Value.Where(p => p.Mod == holder).ToList(), theirs = target.Value.Where(p => p.Mod == partner).ToList();
                        if (mine.Count == 0 || theirs.Count == 0) continue;
                        if (claims.Any(c => string.Equals(c.Target, target.Key, StringComparison.Ordinal) && ModDependencies.Find(allMods, c.Partner ?? "") == partner)) continue;
                        HookClaim claim = HookClaims.Make(target.Key, holder, partner, mine, theirs, null, now);
                        if (claim == null) { whole = false; break; }
                        made.Add(claim);
                    }
                    if (whole) claims.AddRange(made);
                }
                if (claims.Count == before) continue;
                try
                {
                    write(holder, claims);
                    result.Written.Add((holder, claims.Count - before));
                }
                catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException || ex is System.IO.InvalidDataException || ex is System.Text.Json.JsonException)
                {
                    Logger.LogException("Convert the compatibility entries of " + (holder.DisplayName ?? holder.Id) + " into hook claims", ex);
                    result.Problems.Add((holder.DisplayName ?? holder.Id) + ": " + ex.Message);
                }
            }
            return result;
        }
    }
}
