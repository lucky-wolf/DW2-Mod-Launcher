using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DW2ModLauncher.Core.Models;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>
    /// Plain-text views of a <see cref="HookAnalysis"/>: the "Code hooks" section of a mod's details, and the report a developer can paste to another author.
    /// <c>t</c> gives the text of a language-pack key (with {0}-style holes for the arguments). Hook kinds and effects (prefix, skips-original, ...) and
    /// method names stay as they are: they are the identifiers authors search for.
    /// </summary>
    public static class HookReport
    {
        private static string Name(ModInfo mod) { return mod.DisplayName ?? mod.Id; }

        private static string F(Func<string, string> t, string key, params object[] args) { return HookOverlapAnalyzer.F(t, key, args); }

        /// <summary>Namespace.Type:Method(args) as Type:Method.</summary>
        public static string Short(string target)
        {
            string head = target.Split('(')[0];
            int colon = head.IndexOf(':');
            int dot = colon < 0 ? -1 : head.LastIndexOf('.', colon);
            return dot >= 0 ? head.Substring(dot + 1) : head;
        }

        private static string Patches(IEnumerable<HookParticipant> patches)
        {
            return string.Join(", ", patches.Select(p => p.Kind + " " + p.Effect));
        }

        /// <summary>The mod's hooks with who else patches each, then the staleness and channel lines that concern it. Empty when there is nothing to say.</summary>
        public static string ForMod(HookAnalysis analysis, ModInfo mod, Func<string, string> t)
        {
            StringBuilder text = new StringBuilder();
            List<KeyValuePair<string, List<HookParticipant>>> mine = analysis.ByTarget.Where(x => x.Value.Any(p => p.Mod == mod)).OrderBy(x => x.Key, StringComparer.Ordinal).ToList();
            if (mine.Count > 0)
            {
                bool seen = mine.Any(x => x.Value.Any(p => p.Mod == mod && p.Observed));
                text.AppendLine(F(t, "HookDetailsHeader", mine.Count, t(seen ? "HookSeenLastSession" : "HookDeclaredByMod")));
                foreach (KeyValuePair<string, List<HookParticipant>> target in mine)
                {
                    text.AppendLine("  " + Short(target.Key) + "    " + Patches(target.Value.Where(p => p.Mod == mod)));
                    foreach (IGrouping<ModInfo, HookParticipant> other in target.Value.Where(p => p.Mod != mod).GroupBy(p => p.Mod))
                        text.AppendLine("      " + F(t, "HookSharedWith", Name(other.Key), Patches(other)));
                }
            }
            foreach (HookNote note in analysis.Drift.Concat(analysis.Notes).Where(n => n.Mod == mod)) text.AppendLine(note.Text);
            foreach (ChannelIssue issue in analysis.ChannelIssues.Where(i => i.Mods.Contains(mod))) text.AppendLine(issue.Text);
            return text.ToString().TrimEnd();
        }

        /// <summary>Every target two or more mods patch (only those <paramref name="only"/> is in, when given), most-shared first, then the channel and staleness findings; made to be pasted into a message to another author.</summary>
        public static string Full(HookAnalysis analysis, Func<string, string> t, ModInfo only = null, HookSeverity? level = null)
        {
            StringBuilder text = new StringBuilder();
            List<KeyValuePair<string, List<HookParticipant>>> shared = analysis.ByTarget.Where(x => x.Value.Select(p => p.Mod).Distinct().Count() > 1 && (only == null || x.Value.Any(p => p.Mod == only)) && InLevel(analysis, x.Key, only, level))
                .OrderByDescending(x => x.Value.Select(p => p.Mod).Distinct().Count()).ThenBy(x => x.Key, StringComparer.Ordinal).ToList();
            text.AppendLine(F(t, only == null ? "HookFullHeader" : "HookScopedHeader", shared.Count, only == null ? "" : Name(only)) + (analysis.AcknowledgedCount > 0 ? " " + F(t, "HookFullAcknowledgedPairs", analysis.AcknowledgedCount) : ""));
            foreach (KeyValuePair<string, List<HookParticipant>> target in shared)
            {
                HookOverlap overlap = analysis.Overlaps.FirstOrDefault(o => o.Target == target.Key);
                text.AppendLine();
                text.AppendLine(target.Key + "  " + (overlap == null ? t("HookAcknowledged") : "[" + SeverityWord(overlap.Severity, t) + "]"));
                foreach (IGrouping<ModInfo, HookParticipant> mod in target.Value.GroupBy(p => p.Mod))
                    text.AppendLine("  " + Name(mod.Key) + (string.IsNullOrWhiteSpace(mod.Key.Author) ? "" : " " + F(t, "HookBy", mod.Key.Author)) + (string.IsNullOrWhiteSpace(mod.Key.Version) ? "" : " v" + mod.Key.Version)
                        + ": " + string.Join(", ", mod.Select(p => p.Kind + " " + p.Effect + " " + t(p.Observed ? "HookSeenTag" : "HookDeclaredTag"))));
            }
            List<ChannelIssue> channelIssues = analysis.ChannelIssues.Where(i => (only == null || i.Mods.Contains(only)) && level != HookSeverity.Conflict).ToList();
            if (channelIssues.Count > 0)
            {
                text.AppendLine();
                text.AppendLine(t("HookChannelsHeader"));
                foreach (ChannelIssue issue in channelIssues) text.AppendLine("  " + issue.Text);
            }
            List<HookNote> notes = analysis.Drift.Concat(analysis.Notes).Where(n => (only == null || n.Mod == only) && level != HookSeverity.Conflict).ToList();
            if (notes.Count > 0)
            {
                text.AppendLine();
                text.AppendLine(t("HookDriftHeader"));
                foreach (HookNote note in notes) text.AppendLine("  " + note.Text);
            }
            return text.ToString().TrimEnd();
        }

        /// <summary>
        /// Whether a target belongs in a report for one mod at one level: a conflict report has the targets where the mod has a conflict pair, a caution report the
        /// ones where it has caution pairs and no conflict (those are the conflict report's). No level (or no mod) accepts every target.
        /// </summary>
        private static bool InLevel(HookAnalysis analysis, string target, ModInfo only, HookSeverity? level)
        {
            if (only == null || level == null) return true;
            HookOverlap overlap = analysis.Overlaps.FirstOrDefault(o => o.Target == target);
            if (overlap == null) return false;
            List<HookPair> mine = overlap.Pairs.Where(p => p.A == only || p.B == only).ToList();
            bool conflict = mine.Any(p => p.Severity == HookSeverity.Conflict);
            return level == HookSeverity.Conflict ? conflict : !conflict && mine.Any(p => p.Severity == HookSeverity.Caution);
        }

        private static string SeverityWord(HookSeverity severity, Func<string, string> t)
        {
            return t(severity == HookSeverity.Conflict ? "HealthConflict" : severity == HookSeverity.Caution ? "HealthCaution" : "HookInfo");
        }
    }
}
