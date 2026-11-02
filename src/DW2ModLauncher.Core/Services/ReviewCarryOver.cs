using System;
using System.Collections.Generic;
using System.Linq;
using DW2ModLauncher.Core.Diagnostics;
using DW2ModLauncher.Core.Models;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>
    /// What the author or player already reviewed about a mod, carried to the version being published, so a version bump does not bring back
    /// problems that were looked at and put to bed. Three records name a mod's version: acknowledged code overlaps, acknowledged file
    /// collisions, and the pinned "Name@1.2.3" entries of other mods' "hooksCoordinatedWith". All three are moved on before the publish, so
    /// nothing is sent out (or shown) as unreviewed; the old-version pins go once the publish has gone through.
    /// </summary>
    public sealed class ReviewCarryOver
    {
        private readonly ModInfo mod;
        private readonly string oldVersion;
        private readonly List<(ModInfo Other, List<string> Pruned)> pinsToPrune = new List<(ModInfo, List<string>)>();

        /// <summary>Whether the acknowledgement lists in the settings changed (the caller saves them).</summary>
        public bool SettingsChanged { get; private set; }

        /// <summary>What could not be carried over (a file that could not be written), for the caller to show.</summary>
        public List<string> Problems { get; } = new List<string>();

        private ReviewCarryOver(ModInfo mod, string oldVersion) { this.mod = mod; this.oldVersion = oldVersion; }

        /// <summary>
        /// Adds the new-version twin of every record that names <paramref name="mod"/> at <paramref name="oldVersion"/>. The old records stay until
        /// <see cref="PruneOld"/>, so a publish that does not happen (the version is rolled back) leaves everything valid.
        /// Other mods' pins are only rewritten for mods installed locally; a Workshop copy is somebody else's file.
        /// </summary>
        public static ReviewCarryOver Apply(LauncherSettings settings, ModInfo mod, IEnumerable<ModInfo> allMods, string oldVersion, string newVersion)
        {
            ReviewCarryOver carry = new ReviewCarryOver(mod, (oldVersion ?? "").Trim());
            string wanted = (newVersion ?? "").Trim();
            if (carry.oldVersion.Length == 0 || string.Equals(carry.oldVersion, wanted, StringComparison.OrdinalIgnoreCase)) return carry;

            if (settings.AcknowledgedHooks == null) settings.AcknowledgedHooks = new List<string>();
            if (settings.AcknowledgedFileConflicts == null) settings.AcknowledgedFileConflicts = new List<string>();
            carry.SettingsChanged |= AddTwins(settings.AcknowledgedHooks, k => HookTwin(k, mod.Key, carry.oldVersion, wanted));
            carry.SettingsChanged |= AddTwins(settings.AcknowledgedFileConflicts, k => FileTwin(k, mod.StateKey, carry.oldVersion, wanted));

            foreach (ModInfo other in allMods.Where(m => m != null && m != mod && !m.IsWorkshop && m.HooksCoordinatedWith != null && m.HooksCoordinatedWith.Count > 0))
            {
                List<string> entries = other.HooksCoordinatedWith.ToList();
                List<string> old = entries.Where(e => PinsOldVersion(e, mod, carry.oldVersion)).ToList();
                if (old.Count == 0) continue;
                List<string> next = entries.ToList();
                foreach (string entry in old)
                {
                    string twin = HookOverlapAnalyzer.SplitCoordinationEntry(entry).Name + "@" + wanted;
                    if (!next.Contains(twin, StringComparer.OrdinalIgnoreCase)) next.Add(twin);
                }
                if (carry.TryWritePins(other, next)) carry.pinsToPrune.Add((other, old));
            }
            return carry;
        }

        /// <summary>Removes the old-version pins that <see cref="Apply"/> moved on from, once the new version is out.</summary>
        public void PruneOld()
        {
            foreach ((ModInfo other, List<string> pruned) in pinsToPrune)
            {
                List<string> next = other.HooksCoordinatedWith.Where(e => !pruned.Contains(e, StringComparer.OrdinalIgnoreCase)).ToList();
                TryWritePins(other, next);
            }
            pinsToPrune.Clear();
        }

        private bool TryWritePins(ModInfo other, List<string> entries)
        {
            try
            {
                LauncherMetaWriter.SetCoordinatedWith(other.Folder, entries);
                other.HooksCoordinatedWith = entries;
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogException("Carry the compatibility entry for " + (mod.DisplayName ?? mod.Id) + " into " + (other.DisplayName ?? other.Id), ex);
                Problems.Add((other.DisplayName ?? other.Id) + ": " + ex.Message);
                return false;
            }
        }

        private static bool PinsOldVersion(string entry, ModInfo mod, string oldVersion)
        {
            if (string.IsNullOrWhiteSpace(entry)) return false;
            (string name, string pinned) = HookOverlapAnalyzer.SplitCoordinationEntry(entry);
            return pinned != null && string.Equals(pinned, oldVersion, StringComparison.OrdinalIgnoreCase) && ModDependencies.Find(new[] { mod }, name) == mod;
        }

        private static bool AddTwins(List<string> keys, Func<string, string> twin)
        {
            bool changed = false;
            foreach (string key in keys.ToList())
            {
                string made = twin(key);
                if (made != null && !keys.Contains(made)) { keys.Add(made); changed = true; }
            }
            return changed;
        }

        // "target | sideA | sideB", a side being "<mod key>@<version>", the two sorted (HookOverlapAnalyzer.AcknowledgementKey).
        private static string HookTwin(string ack, string modKey, string oldVersion, string newVersion)
        {
            int second = ack.LastIndexOf(" | ", StringComparison.Ordinal);
            int first = second < 1 ? -1 : ack.LastIndexOf(" | ", second - 1, StringComparison.Ordinal);
            if (first < 0) return null;
            string[] sides = { ack.Substring(first + 3, second - first - 3), ack.Substring(second + 3) };
            if (!Retarget(sides, modKey, oldVersion, newVersion)) return null;
            Array.Sort(sides, StringComparer.OrdinalIgnoreCase);
            return ack.Substring(0, first) + " | " + sides[0] + " | " + sides[1];
        }

        // "file|sideA|sideB|...", a side being "<state key>@<version>", the sides sorted (ConflictAcknowledgement.Key).
        private static string FileTwin(string ack, string stateKey, string oldVersion, string newVersion)
        {
            string[] parts = ack.Split('|');
            if (parts.Length < 2) return null;
            string[] sides = parts.Skip(1).ToArray();
            if (!Retarget(sides, stateKey, oldVersion, newVersion)) return null;
            return parts[0] + "|" + string.Join("|", sides.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray());
        }

        private static bool Retarget(string[] sides, string key, string oldVersion, string newVersion)
        {
            bool changed = false;
            for (int i = 0; i < sides.Length; i++)
                if (string.Equals(sides[i], key + "@" + oldVersion, StringComparison.OrdinalIgnoreCase)) { sides[i] = key + "@" + newVersion; changed = true; }
            return changed;
        }
    }
}
