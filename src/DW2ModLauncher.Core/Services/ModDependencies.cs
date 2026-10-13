using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DW2ModLauncher.Core.Models;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>
    /// What a mod's dw2modlauncher.json "dependencies" ask of the launcher (docs/plans/mod-dependencies.md): who must be enabled and how
    /// new, and the order against them. (Data handed between mods is ModChannels.) Pure logic; no UI.
    /// </summary>
    public static class ModDependencies
    {
        /// <summary>The mod a dependency's "mod" names: matched against id, display name, token or folder name, ignoring case.</summary>
        public static ModInfo Find(IEnumerable<ModInfo> mods, string identity)
        {
            if (string.IsNullOrWhiteSpace(identity)) return null;
            return mods.FirstOrDefault(m => m != null && new string[] { m.Id, m.DisplayName, m.ActiveToken, Path.GetFileName(m.Folder ?? "") }
                .Any(x => string.Equals(x, identity.Trim(), StringComparison.OrdinalIgnoreCase)));
        }

        private static string Name(ModInfo mod) { return mod.DisplayName ?? mod.Id; }

        /// <summary>
        /// Why the enabled mods' dependencies are not met: a required mod that is not enabled, a version below minVersion (or one the
        /// other mod does not state), and an entry that cannot work.
        /// </summary>
        public static List<string> Problems(IList<ModInfo> enabled)
        {
            List<string> problems = new List<string>();
            foreach (ModInfo mod in enabled)
            {
                foreach (LauncherDependency dep in mod.Dependencies ?? new List<LauncherDependency>())
                {
                    if (dep == null || string.IsNullOrWhiteSpace(dep.mod)) { problems.Add("⚠ " + Name(mod) + ": a dependency in dw2modlauncher.json names no mod"); continue; }
                    if (dep.loadAfter && dep.loadBefore) problems.Add("⚠ " + Name(mod) + ": dw2modlauncher.json asks to load both before and after " + dep.mod);
                    ModInfo target = Find(enabled, dep.mod);
                    if (target == null || target == mod)
                    {
                        if (dep.required) problems.Add("⚠ " + Name(mod) + ": requires " + dep.mod + ", which is not installed or not enabled");
                        continue;
                    }
                    VersionProblem(mod, dep, target, problems);
                }
            }
            return problems;
        }

        /// <summary>Whether the other mod is new enough for the dependency; a problem is added to the list when it is not (or cannot be told).</summary>
        private static bool VersionProblem(ModInfo mod, LauncherDependency dep, ModInfo target, List<string> problems)
        {
            if (string.IsNullOrWhiteSpace(dep.minVersion)) return false;
            if (!LauncherRequirement.TryParse(dep.minVersion, out Version needed))
            {
                problems.Add("⚠ " + Name(mod) + ": its minVersion '" + dep.minVersion + "' for " + dep.mod + " is not a version");
                return true;
            }
            if (!LauncherRequirement.TryParse(target.Version, out Version have))
            {
                problems.Add("⚠ " + Name(mod) + ": needs " + dep.mod + " " + dep.minVersion + " or newer, but " + Name(target) + " states no usable version" + (string.IsNullOrWhiteSpace(target.Version) ? "" : " ('" + target.Version + "')"));
                return true;
            }
            if (have < needed)
            {
                problems.Add("⚠ " + Name(mod) + ": needs " + dep.mod + " " + dep.minVersion + " or newer, but " + Name(target) + " is " + target.Version);
                return true;
            }
            return false;
        }

        /// <summary>The result of putting the enabled mods in an order that satisfies every loadAfter and loadBefore.</summary>
        public class OrderResult
        {
            /// <summary>The mods in the order to load them.</summary>
            public List<ModInfo> Order = new List<ModInfo>();
            /// <summary>One line for each rule the given order broke (so the order changed).</summary>
            public List<string> Moves = new List<string>();
            /// <summary>The mods whose rules contradict each other, left in the order given; empty when the rules can all be met.</summary>
            public List<ModInfo> Cycle = new List<ModInfo>();
            public bool Changed { get { return Moves.Count > 0; } }
        }

        /// <summary>
        /// Orders the enabled mods (given in load order) so each loadAfter / loadBefore holds, disturbing the given order as little as
        /// it can: the mod that comes first in the given order goes next whenever its rules allow. A rule about a mod that is not enabled does nothing.
        /// </summary>
        public static OrderResult Enforce(IList<ModInfo> ordered)
        {
            OrderResult result = new OrderResult();
            int count = ordered.Count;
            // before[i] = the mods that must load before mod i
            List<int>[] before = new List<int>[count];
            for (int i = 0; i < count; i++) before[i] = new List<int>();
            for (int i = 0; i < count; i++)
            {
                foreach (LauncherDependency dep in ordered[i].Dependencies ?? new List<LauncherDependency>())
                {
                    if (dep == null) continue;
                    ModInfo target = Find(ordered, dep.mod);
                    int j = target == null ? -1 : ordered.IndexOf(target);
                    if (j < 0 || j == i) continue;
                    if (dep.loadAfter && !dep.loadBefore)
                    {
                        before[i].Add(j);
                        if (j > i) result.Moves.Add(Name(ordered[i]) + " must load after " + Name(target));
                    }
                    else if (dep.loadBefore && !dep.loadAfter)
                    {
                        before[j].Add(i);
                        if (i > j) result.Moves.Add(Name(ordered[i]) + " must load before " + Name(target));
                    }
                }
            }
            bool[] placed = new bool[count];
            for (int step = 0; step < count; step++)
            {
                int next = -1;
                for (int i = 0; i < count && next < 0; i++)
                    if (!placed[i] && before[i].All(j => placed[j])) next = i;
                if (next < 0) break;
                placed[next] = true;
                result.Order.Add(ordered[next]);
            }
            for (int i = 0; i < count; i++)
                if (!placed[i]) { result.Cycle.Add(ordered[i]); result.Order.Add(ordered[i]); }
            return result;
        }
    }
}
