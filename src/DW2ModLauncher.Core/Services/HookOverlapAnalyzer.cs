using System;
using System.Collections.Generic;
using System.Linq;
using DW2ModLauncher.Core.Models;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>The effect words a hook can carry, from harmless to hardest.</summary>
    public static class HookEffects
    {
        public const string Observes = "observes";
        public const string ChangesResult = "changes-result";
        public const string ChangesArgs = "changes-args";
        public const string SkipsOriginal = "skips-original";
        public const string Rewrites = "rewrites";

        public static readonly string[] All = { Observes, ChangesResult, ChangesArgs, SkipsOriginal, Rewrites };

        public static bool IsKnown(string effect) { return All.Contains(effect, StringComparer.OrdinalIgnoreCase); }
    }

    public enum HookSeverity { Info = 0, Caution = 1, Conflict = 2 }

    /// <summary>One mod's patch on a shared target.</summary>
    public class HookParticipant
    {
        public ModInfo Mod { get; set; }
        public string Kind { get; set; }
        public string Effect { get; set; }
        public string HarmonyId { get; set; }
        public List<string> Before { get; set; } = new List<string>();
        public List<string> After { get; set; } = new List<string>();
        /// <summary>True when it came from an observed game session, false when from the mod's declared manifest.</summary>
        public bool Observed { get; set; }
        /// <summary>False for a mod that is not in the launch list: its patches are checked against the enabled mods so the player can see what enabling it would bring.</summary>
        public bool Enabled { get; set; } = true;
    }

    /// <summary>Two mods that both patch one target, and how badly that bites.</summary>
    public class HookPair
    {
        public ModInfo A { get; set; }
        public ModInfo B { get; set; }
        public HookSeverity Severity { get; set; }
        /// <summary>False when one of the two is not enabled: the pair is what enabling that mod would bring.</summary>
        public bool BothEnabled { get; set; } = true;
    }

    public class HookOverlap
    {
        public string Target { get; set; }
        public HookSeverity Severity { get; set; }
        public List<HookParticipant> Participants { get; set; } = new List<HookParticipant>();
        public List<HookPair> Pairs { get; set; } = new List<HookPair>();
    }

    public class ChannelIssue
    {
        public string Channel { get; set; }
        public string Text { get; set; }
        public List<ModInfo> Mods { get; set; } = new List<ModInfo>();
    }

    /// <summary>One finding about a mod's hook data (stale, unreadable, drifting from the last session), in the player's language.</summary>
    public class HookNote
    {
        public ModInfo Mod { get; set; }
        public string Text { get; set; }
        /// <summary>True when the data could not be used at all (an unreadable or invalid file), as opposed to merely stale.</summary>
        public bool Error { get; set; }
    }

    public class HookAnalysis
    {
        /// <summary>The mods in the launch list; the others in <see cref="ByTarget"/> and <see cref="Overlaps"/> are mods that are not enabled.</summary>
        public HashSet<ModInfo> Enabled { get; set; } = new HashSet<ModInfo>();
        /// <summary>Targets patched by two or more mods that have not been acknowledged, most severe first.</summary>
        public List<HookOverlap> Overlaps { get; set; } = new List<HookOverlap>();
        public List<ChannelIssue> ChannelIssues { get; set; } = new List<ChannelIssue>();
        /// <summary>Every target any enabled mod patches (not only the shared ones), with each mod's patches on it.</summary>
        public Dictionary<string, List<HookParticipant>> ByTarget { get; set; } = new Dictionary<string, List<HookParticipant>>(StringComparer.Ordinal);
        /// <summary>Where a mod's declared manifest and the last session disagree: declared but not seen, seen but not declared.</summary>
        public List<HookNote> Drift { get; set; } = new List<HookNote>();
        /// <summary>How many shared-target pairs are hidden because the player acknowledged them.</summary>
        public int AcknowledgedCount { get; set; }
        /// <summary>Declared or observed data that no longer matches the installed mod or game, and hooks files that could not be read.</summary>
        public List<HookNote> Notes { get; set; } = new List<HookNote>();
    }

    /// <summary>
    /// Compares what the enabled mods patch in the game's code (docs/plans/hook-manifest.md). Each mod's hooks come from the last session's
    /// observed file when it covers the installed version of the mod, otherwise from the mod's declared manifest. Pure logic; no UI.
    /// </summary>
    public static class HookOverlapAnalyzer
    {
        private static string Name(ModInfo mod) { return mod.DisplayName ?? mod.Id; }

        /// <summary>The text for a language-pack key, with the arguments put in.</summary>
        internal static string F(Func<string, string> t, string key, params (string Name, object Value)[] values)
        {
            return NamedText.Fill(t(key), placeholder => Diagnostics.Logger.Log("Language pack", "'" + key + "' has the placeholder {" + placeholder + "}, which the code does not give."), values);
        }

        /// <summary>
        /// <paramref name="isSettingOn"/> says whether a declared hook's setting is on for a mod (null: all are on); <paramref name="acknowledged"/>
        /// holds <see cref="AcknowledgementKey"/> values the player has dismissed; <paramref name="t"/> gives the text of a language-pack key (with
        /// {0}-style holes for the arguments); <paramref name="readManifest"/> defaults to reading each
        /// mod's dw2modlauncher.hooks.json. <paramref name="notEnabled"/> are installed mods that are not in the launch list: their patches (from
        /// the observed history, or their declared file) are checked against the enabled mods' so the player can see what enabling one would bring,
        /// but two mods that are both not enabled are never compared, and their settings are not consulted.
        /// </summary>
        public static HookAnalysis Analyze(IList<ModInfo> orderedEnabled, ObservedHooksFile observed, Func<ModInfo, string, bool> isSettingOn, ISet<string> acknowledged,
            Func<string, string> t, Func<ModInfo, (HookManifest Manifest, string Error)> readManifest = null, string gameVersion = null, IEnumerable<ModInfo> notEnabled = null)
        {
            HookAnalysis result = new HookAnalysis { Enabled = new HashSet<ModInfo>(orderedEnabled) };
            readManifest = readManifest ?? (m => HookFiles.ReadManifest(m, t));
            Dictionary<string, List<HookParticipant>> byTarget = new Dictionary<string, List<HookParticipant>>(StringComparer.Ordinal);
            Dictionary<ModInfo, HookManifest> manifests = new Dictionary<ModInfo, HookManifest>();

            // One mod's patches into byTarget: what an observed session recorded for the installed version, else what the mod declares.
            void Participate(ModInfo mod, HookManifest manifest, bool enabled)
            {
                if (Observes(observed, mod))
                {
                    foreach (ObservedHookInfo hook in observed.Hooks ?? new List<ObservedHookInfo>())
                        foreach (ObservedPatchInfo patch in (hook.Patches ?? new List<ObservedPatchInfo>()).Where(p => string.Equals(p.Owner, mod.Key, StringComparison.OrdinalIgnoreCase)))
                            Add(byTarget, hook.Target, new HookParticipant
                            {
                                Mod = mod,
                                Kind = patch.Kind,
                                Effect = patch.Effect,
                                HarmonyId = patch.HarmonyId,
                                Before = patch.Before ?? new List<string>(),
                                After = patch.After ?? new List<string>(),
                                Observed = true,
                                Enabled = enabled
                            });
                }
                else if (manifest != null)
                {
                    foreach (DeclaredHook hook in manifest.Hooks ?? new List<DeclaredHook>())
                    {
                        if (string.IsNullOrWhiteSpace(hook?.Target)) { result.Notes.Add(new HookNote { Mod = mod, Text = F(t, "HookNoTarget", ("Mod", Name(mod)), ("File", HookFiles.ManifestFileName)), Error = enabled }); continue; }
                        if (!HookEffects.IsKnown(hook.Effect)) { result.Notes.Add(new HookNote { Mod = mod, Text = F(t, "HookUnknownEffect", ("Mod", Name(mod)), ("Hook", hook.Target), ("Effect", hook.Effect), ("Allowed", string.Join(t("ListSeparator"), HookEffects.All))), Error = enabled }); continue; }
                        if (enabled && !string.IsNullOrWhiteSpace(hook.Setting) && isSettingOn != null && !isSettingOn(mod, hook.Setting)) continue;
                        Add(byTarget, hook.Target, new HookParticipant { Mod = mod, Kind = hook.Kind, Effect = hook.Effect.ToLowerInvariant(), Enabled = enabled });
                    }
                }
            }

            // A record from another version of the mod says nothing about this one: say so, rather than showing "no overlaps" for code nobody has run.
            // A declared file covers the mod (its own staleness is noted separately), so only a mod with neither is told.
            void NoteUnrunVersion(ModInfo mod, HookManifest manifest)
            {
                if (manifest != null || Observes(observed, mod)) return;
                ObservedModInfo old = observed?.Mods?.FirstOrDefault(m => string.Equals(m.Key, mod.Key, StringComparison.OrdinalIgnoreCase));
                if (old != null) result.Notes.Add(new HookNote { Mod = mod, Text = F(t, "HookUnrunVersion", ("Mod", Name(mod)), ("Recorded", old.Version ?? "?"), ("Installed", mod.Version ?? "?")) });
            }

            foreach (ModInfo mod in orderedEnabled)
            {
                (HookManifest manifest, string error) = readManifest(mod);
                NoteUnrunVersion(mod, manifest);
                if (error != null) result.Notes.Add(new HookNote { Mod = mod, Text = F(t, "HookModError", ("Mod", Name(mod)), ("Problem", error)), Error = true });
                if (manifest != null) manifests[mod] = manifest;
                if (manifest != null && !string.IsNullOrWhiteSpace(manifest.ModVersion) && !string.IsNullOrWhiteSpace(mod.Version)
                    && manifest.ModVersion.Trim() != mod.Version.Trim())
                    result.Notes.Add(new HookNote { Mod = mod, Text = F(t, "HookStaleMod", ("Mod", Name(mod)), ("Declared", manifest.ModVersion), ("Installed", mod.Version)) });
                if (manifest != null && gameVersion != null && !string.IsNullOrWhiteSpace(manifest.GameVersion) && manifest.GameVersion.Trim() != gameVersion.Trim())
                    result.Notes.Add(new HookNote { Mod = mod, Text = F(t, "HookStaleGame", ("Mod", Name(mod)), ("Declared", manifest.GameVersion), ("Installed", gameVersion)) });

                if (Observes(observed, mod) && manifest != null) result.Drift.AddRange(Drift(mod, observed, manifest, isSettingOn, t));
                Participate(mod, manifest, true);
            }

            // A problem with a not-enabled mod's file is noted on that mod's own details, not raised as an error for the whole launcher.
            List<ModInfo> notEnabledMods = (notEnabled ?? Enumerable.Empty<ModInfo>()).Where(m => !result.Enabled.Contains(m)).ToList();
            foreach (ModInfo mod in notEnabledMods)
            {
                (HookManifest manifest, string error) = readManifest(mod);
                if (error != null) result.Notes.Add(new HookNote { Mod = mod, Text = F(t, "HookModError", ("Mod", Name(mod)), ("Problem", error)) });
                NoteUnrunVersion(mod, manifest);
                Participate(mod, manifest, false);
            }

            // a target nobody enabled patches is of no interest, however many disabled mods patch it
            foreach (string target in byTarget.Where(x => !x.Value.Any(p => p.Enabled)).Select(x => x.Key).ToList()) byTarget.Remove(target);
            result.ByTarget = byTarget;
            // declared coordination and load order are looked up among every mod that takes part, enabled or not
            List<ModInfo> everyone = orderedEnabled.Concat(notEnabledMods).ToList();
            foreach (KeyValuePair<string, List<HookParticipant>> target in byTarget)
            {
                List<ModInfo> mods = target.Value.Select(p => p.Mod).Distinct().ToList();
                if (mods.Count < 2) continue;
                HookOverlap overlap = new HookOverlap { Target = target.Key, Participants = target.Value };
                for (int i = 0; i < mods.Count; i++)
                    for (int j = i + 1; j < mods.Count; j++)
                    {
                        bool bothEnabled = result.Enabled.Contains(mods[i]) && result.Enabled.Contains(mods[j]);
                        if (!bothEnabled && !result.Enabled.Contains(mods[i]) && !result.Enabled.Contains(mods[j])) continue;
                        if (acknowledged != null && acknowledged.Contains(AcknowledgementKey(target.Key, mods[i], mods[j]))) { result.AcknowledgedCount++; continue; }
                        HookSeverity severity = GradePair(target.Value.Where(p => p.Mod == mods[i]).ToList(), target.Value.Where(p => p.Mod == mods[j]).ToList(), mods[i], mods[j], everyone);
                        overlap.Pairs.Add(new HookPair { A = mods[i], B = mods[j], Severity = severity, BothEnabled = bothEnabled });
                    }
                if (overlap.Pairs.Count == 0) continue;
                overlap.Severity = overlap.Pairs.Max(p => p.Severity);
                result.Overlaps.Add(overlap);
            }
            result.Overlaps = result.Overlaps.OrderByDescending(o => o.Severity).ThenByDescending(o => o.Participants.Count).ThenBy(o => o.Target, StringComparer.Ordinal).ToList();

            result.ChannelIssues.AddRange(ChannelIssues(orderedEnabled, manifests, observed, t));
            return result;
        }

        /// <summary>The key an acknowledgement is stored under: the target, and both mods with their versions, so a new version or a new partner asks again.</summary>
        public static string AcknowledgementKey(string target, ModInfo a, ModInfo b)
        {
            string[] sides = { a.Key + "@" + (a.Version ?? ""), b.Key + "@" + (b.Version ?? "") };
            Array.Sort(sides, StringComparer.OrdinalIgnoreCase);
            return target + " | " + sides[0] + " | " + sides[1];
        }

        /// <summary>Sets each mod's code-overlap counters and notes from the analysis (cleared first), for the health column and details.</summary>
        public static void Apply(IEnumerable<ModInfo> allMods, HookAnalysis analysis, Func<string, string> t)
        {
            foreach (ModInfo mod in allMods.Where(m => m != null))
            {
                mod.HookCautionCount = 0;
                mod.HookConflictCount = 0;
                mod.HookConflictMods = mod.HookConflictMethods = mod.HookCautionMods = mod.HookCautionMethods = 0;
                if (mod.HookNotes == null) mod.HookNotes = new List<string>(); else mod.HookNotes.Clear();
                if (mod.HookConflictNotes == null) mod.HookConflictNotes = new List<string>(); else mod.HookConflictNotes.Clear();
            }
            // One note per partner mod, not per patched method: a player needs to know who a mod clashes with, the methods are in the full report.
            // The hook count in a note is of the note's own level (a partner with 2 conflicts and 1 overlap reads "2 hooks" under Conflict).
            Dictionary<(ModInfo, ModInfo), (int Conflicts, int Cautions)> partners = new Dictionary<(ModInfo, ModInfo), (int, int)>();
            foreach (HookOverlap overlap in analysis.Overlaps)
                foreach (HookPair pair in overlap.Pairs.Where(p => p.Severity > HookSeverity.Info))
                    foreach ((ModInfo mod, ModInfo other) in new[] { (pair.A, pair.B), (pair.B, pair.A) })
                    {
                        // an enabled mod's health is not marked down by a mod that is not enabled; the not-enabled one shows what enabling it would bring
                        if (analysis.Enabled.Contains(mod) && !analysis.Enabled.Contains(other)) continue;
                        if (pair.Severity == HookSeverity.Conflict) mod.HookConflictCount++; else mod.HookCautionCount++;
                        partners.TryGetValue((mod, other), out (int Conflicts, int Cautions) counts);
                        partners[(mod, other)] = pair.Severity == HookSeverity.Conflict ? (counts.Conflicts + 1, counts.Cautions) : (counts.Conflicts, counts.Cautions + 1);
                    }
            foreach (IGrouping<ModInfo, KeyValuePair<(ModInfo, ModInfo), (int Conflicts, int Cautions)>> group in partners.GroupBy(x => x.Key.Item1))
            {
                List<string> conflictTargets = new List<string>(), cautionTargets = new List<string>();
                // the pairs counted against this mod (see above): not those where it is enabled and the other is not
                bool Counts(HookPair p) => (p.A == group.Key || p.B == group.Key) && (p.BothEnabled || !analysis.Enabled.Contains(group.Key));
                foreach (HookOverlap overlap in analysis.Overlaps)
                {
                    if (overlap.Pairs.Any(p => p.Severity == HookSeverity.Conflict && Counts(p))) conflictTargets.Add(overlap.Target);
                    else if (overlap.Pairs.Any(p => p.Severity == HookSeverity.Caution && Counts(p))) cautionTargets.Add(overlap.Target);
                }
                group.Key.HookConflictMods = group.Count(x => x.Value.Conflicts > 0);
                group.Key.HookCautionMods = group.Count(x => x.Value.Conflicts == 0);
                group.Key.HookConflictMethods = conflictTargets.Count;
                group.Key.HookCautionMethods = cautionTargets.Count;
            }
            foreach (KeyValuePair<(ModInfo, ModInfo), (int Conflicts, int Cautions)> partner in partners
                .OrderByDescending(x => x.Value.Conflicts > 0).ThenBy(x => Name(x.Key.Item2), StringComparer.OrdinalIgnoreCase))
            {
                bool conflict = partner.Value.Conflicts > 0;
                int hooks = conflict ? partner.Value.Conflicts : partner.Value.Cautions;
                string note = F(t, "HookPartnerLine", ("Mod", Name(partner.Key.Item2)), ("Count", hooks));
                partner.Key.Item1.HookNotes.Add(note);
                if (conflict) partner.Key.Item1.HookConflictNotes.Add(note);
            }
            foreach (ChannelIssue issue in analysis.ChannelIssues)
                foreach (ModInfo mod in issue.Mods)
                {
                    mod.HookCautionCount++;
                    mod.HookNotes.Add(issue.Text);
                }
        }

        // (target, kind) pairs the mod declares (with their setting on) against the ones the last session saw it patch
        private static IEnumerable<HookNote> Drift(ModInfo mod, ObservedHooksFile observed, HookManifest manifest, Func<ModInfo, string, bool> isSettingOn, Func<string, string> t)
        {
            HashSet<string> seen = new HashSet<string>(observed.Hooks.SelectMany(h => (h.Patches ?? new List<ObservedPatchInfo>())
                .Where(p => string.Equals(p.Owner, mod.Key, StringComparison.OrdinalIgnoreCase)).Select(p => h.Target + " " + p.Kind)), StringComparer.Ordinal);
            HashSet<string> declared = new HashSet<string>((manifest.Hooks ?? new List<DeclaredHook>())
                .Where(h => !string.IsNullOrWhiteSpace(h?.Target) && (string.IsNullOrWhiteSpace(h.Setting) || isSettingOn == null || isSettingOn(mod, h.Setting)))
                .Select(h => h.Target + " " + h.Kind), StringComparer.Ordinal);
            foreach (string missing in declared.Except(seen).OrderBy(x => x, StringComparer.Ordinal)) yield return new HookNote { Mod = mod, Text = F(t, "HookDriftMissing", ("Mod", Name(mod)), ("Hooks", missing)) };
            foreach (string extra in seen.Except(declared).OrderBy(x => x, StringComparer.Ordinal)) yield return new HookNote { Mod = mod, Text = F(t, "HookDriftExtra", ("Mod", Name(mod)), ("Hooks", extra)) };
        }

        private static void Add(Dictionary<string, List<HookParticipant>> byTarget, string target, HookParticipant participant)
        {
            if (!byTarget.TryGetValue(target, out List<HookParticipant> list)) { list = new List<HookParticipant>(); byTarget[target] = list; }
            list.Add(participant);
        }

        // The last session's data speaks for a mod only when that session loaded the same version of it.
        public static bool Observes(ObservedHooksFile observed, ModInfo mod)
        {
            return observed?.Mods != null && observed.Mods.Any(m => string.Equals(m.Key, mod.Key, StringComparison.OrdinalIgnoreCase)
                && (m.Version ?? "") == (mod.Version ?? ""));
        }

        private static HookSeverity GradePair(List<HookParticipant> a, List<HookParticipant> b, ModInfo modA, ModInfo modB, IList<ModInfo> enabled)
        {
            // Mods by one author are coordinated by definition; an author nobody stated is not guessed to be the same.
            if (SameAuthor(modA, modB)) return HookSeverity.Info;
            // ... and so are two mods either of which says it has coordinated with the other (dw2modlauncher.json "hooksCoordinatedWith")
            bool Says(ModInfo mod, ModInfo other) => (mod.HooksCoordinatedWith ?? new List<string>()).Any(entry => Coordinates(entry, other, enabled));
            if (Says(modA, modB) || Says(modB, modA)) return HookSeverity.Info;
            HookSeverity worst = HookSeverity.Info;
            foreach (HookParticipant pa in a)
                foreach (HookParticipant pb in b)
                {
                    HookSeverity s = HookSeverity.Info;
                    if (pa.Effect == HookEffects.SkipsOriginal || pb.Effect == HookEffects.SkipsOriginal) s = HookSeverity.Conflict;
                    else if (pa.Effect != HookEffects.Observes && pb.Effect != HookEffects.Observes) s = HookSeverity.Caution;
                    if (s > HookSeverity.Info && Coordinated(pa, pb, modA, modB, enabled)) s--;
                    if (s > worst) worst = s;
                }
            return worst;
        }

        /// <summary>
        /// One "hooksCoordinatedWith" entry: a mod name, or "Name@1.2.3" to cover only that version of it. A pinned entry says nothing
        /// about any other version, so an update brings the overlap back for the author to look at again (an unpinned entry never expires).
        /// </summary>
        public static bool Coordinates(string entry, ModInfo other, IList<ModInfo> enabled)
        {
            if (string.IsNullOrWhiteSpace(entry)) return false;
            (string name, string pinned) = SplitCoordinationEntry(entry);
            if (ModDependencies.Find(enabled, name) != other) return false;
            return pinned == null || string.Equals(pinned, (other.Version ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The mod name and the pinned version (null when the entry is unpinned) of a "hooksCoordinatedWith" entry.</summary>
        public static (string Name, string Pinned) SplitCoordinationEntry(string entry)
        {
            string name = (entry ?? "").Trim(), pinned = null;
            int at = name.LastIndexOf('@');
            if (at > 0) { pinned = name.Substring(at + 1).Trim(); name = name.Substring(0, at).Trim(); }
            return (name, pinned);
        }

        /// <summary>The entry that says a mod is coordinated with <paramref name="other"/> as it is now: its name, pinned to its version when it has one.</summary>
        public static string CoordinationEntryFor(ModInfo other)
        {
            string name = Name(other);
            return string.IsNullOrWhiteSpace(other.Version) ? name : name + "@" + other.Version.Trim();
        }

        /// <summary>Two mods are by one author when they state the same author, or when the Workshop items they are published as were created by the same Steam user.</summary>
        public static bool SameAuthor(ModInfo a, ModInfo b)
        {
            if (!string.IsNullOrWhiteSpace(a.WorkshopCreator) && string.Equals(a.WorkshopCreator.Trim(), b.WorkshopCreator?.Trim(), StringComparison.Ordinal)) return true;
            return !string.IsNullOrWhiteSpace(a.Author) && string.Equals(a.Author.Trim(), b.Author?.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        // Harmony ordering that names the other patch's owner, or a declared load order between the two mods: the authors have already dealt with each other.
        private static bool Coordinated(HookParticipant a, HookParticipant b, ModInfo modA, ModInfo modB, IList<ModInfo> enabled)
        {
            bool Names(HookParticipant p, HookParticipant other) =>
                !string.IsNullOrEmpty(other.HarmonyId) && (p.Before.Contains(other.HarmonyId) || p.After.Contains(other.HarmonyId));
            if (Names(a, b) || Names(b, a)) return true;
            bool Orders(ModInfo mod, ModInfo other) =>
                (mod.Dependencies ?? new List<LauncherDependency>()).Any(d => d != null && (d.loadAfter || d.loadBefore) && ModDependencies.Find(enabled, d.mod) == other);
            return Orders(modA, modB) || Orders(modB, modA);
        }

        private static IEnumerable<ChannelIssue> ChannelIssues(IList<ModInfo> enabled, Dictionary<ModInfo, HookManifest> manifests, ObservedHooksFile observed, Func<string, string> t)
        {
            // declared: a consumer expecting another version than the winning (last in load order) provider supplies
            Dictionary<string, (ModInfo Mod, int Version)> winners = new Dictionary<string, (ModInfo, int)>(StringComparer.OrdinalIgnoreCase);
            foreach (ModInfo mod in enabled)
                if (manifests.TryGetValue(mod, out HookManifest m))
                    foreach (DeclaredChannel c in (m.Channels ?? new List<DeclaredChannel>()).Where(c => string.Equals(c?.Role, "provides", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(c.Name)))
                        winners[c.Name.Trim()] = (mod, c.Version);
            foreach (ModInfo mod in enabled)
                if (manifests.TryGetValue(mod, out HookManifest m))
                    foreach (DeclaredChannel c in (m.Channels ?? new List<DeclaredChannel>()).Where(c => string.Equals(c?.Role, "consumes", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(c.Name)))
                        if (winners.TryGetValue(c.Name.Trim(), out (ModInfo Mod, int Version) provider) && provider.Version != c.Version && provider.Mod != mod)
                            yield return new ChannelIssue
                            {
                                Channel = c.Name.Trim(),
                                Text = F(t, "HookChannelVersion", ("Mod", Name(mod)), ("Channel", c.Name.Trim()), ("Wanted", c.Version), ("Provider", Name(provider.Mod)), ("Provided", provider.Version)),
                                Mods = new List<ModInfo> { mod, provider.Mod }
                            };
            // observed: what the loader itself refused to connect last session
            foreach (string problem in observed?.Channels?.Problems ?? new List<string>())
                yield return new ChannelIssue { Channel = "", Text = F(t, "HookLastSession", ("Problem", problem)), Mods = new List<ModInfo>() };
        }
    }
}
