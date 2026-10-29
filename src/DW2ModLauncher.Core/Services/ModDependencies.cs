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
        /// <summary>Whether a dependency entry names a mod at all: by Workshop id, by name, or both.</summary>
        public static bool NamesAMod(LauncherDependency dep)
        {
            return dep != null && (dep.workshopId.HasValue || !string.IsNullOrWhiteSpace(dep.mod));
        }

        /// <summary>How a message calls the mod a dependency points at: its name when given, else its Workshop id.</summary>
        public static string Label(LauncherDependency dep)
        {
            return !string.IsNullOrWhiteSpace(dep.mod) ? dep.mod.Trim() : dep.workshopId.ToString();
        }

        /// <summary>The mod a dependency points at. A Workshop id wins: when the entry has one, the name is not looked at, and no mod with that id means no mod (never a guess from the name). Without an id the name is matched against id, display name, token or folder name, ignoring case.</summary>
        public static ModInfo Find(IEnumerable<ModInfo> mods, LauncherDependency dep)
        {
            if (dep == null) return null;
            if (dep.workshopId.HasValue)
            {
                string wanted = dep.workshopId.Value.ToString();
                return mods.FirstOrDefault(m => m != null && (string.Equals(m.WorkshopId?.Trim(), wanted, StringComparison.Ordinal) || (m.IsWorkshop && string.Equals(m.Id, wanted, StringComparison.Ordinal))));
            }
            if (string.IsNullOrWhiteSpace(dep.mod)) return null;
            return mods.FirstOrDefault(m => m != null && new string[] { m.Id, m.DisplayName, m.ActiveToken, Path.GetFileName(m.Folder ?? "") }
                .Any(x => string.Equals(x, dep.mod.Trim(), StringComparison.OrdinalIgnoreCase)));
        }

        /// <summary>The mod a plain name points at (id, display name, token or folder name, ignoring case).</summary>
        public static ModInfo Find(IEnumerable<ModInfo> mods, string name)
        {
            return Find(mods, new LauncherDependency { mod = name });
        }

        private static string Name(ModInfo mod) { return mod.DisplayName ?? mod.Id; }

        /// <summary>
        /// Why the enabled mods' dependencies are not met: a required mod that is not enabled, a version below minVersion (or one the
        /// other mod does not state), and an entry that cannot work. Each is a whole sentence naming the mod, worded through <paramref name="t"/>.
        /// </summary>
        public static List<string> Problems(IList<ModInfo> enabled, IEnumerable<ModInfo> installed, Func<string, string> t)
        {
            List<string> problems = new List<string>();
            foreach (ModInfo mod in enabled)
                foreach (string problem in ProblemsOf(mod, enabled, installed, t)) problems.Add("⚠ " + problem);
            return problems;
        }

        /// <summary><paramref name="installed"/> is every mod the launcher knows, so a missing requirement can say whether it is absent or only disabled. The same problems for one enabled mod.</summary>
        public static List<string> ProblemsOf(ModInfo mod, IList<ModInfo> enabled, IEnumerable<ModInfo> installed, Func<string, string> t)
        {
            List<string> problems = new List<string>();
            foreach (LauncherDependency dep in mod.Dependencies ?? new List<LauncherDependency>())
            {
                if (!NamesAMod(dep)) { problems.Add(NamedText.Fill(t("DependencyNamesNoMod"), ("Mod", Name(mod)))); continue; }
                if (dep.loadAfter && dep.loadBefore) problems.Add(NamedText.Fill(t("DependencyBothOrders"), ("Mod", Name(mod)), ("Target", Label(dep))));
                ModInfo target = Find(enabled, dep);
                if (target == null || target == mod)
                {
                    if (dep.required) problems.Add(NamedText.Fill(t(Find(installed, dep) == null ? "DependencyNotInstalled" : "DependencyNotEnabled"), ("Mod", Name(mod)), ("Target", Label(dep))));
                    continue;
                }
                VersionProblem(mod, dep, target, problems, t);
            }
            return problems;
        }

        /// <summary>Whether the other mod is new enough for the dependency; a problem is added to the list when it is not (or cannot be told).</summary>
        private static bool VersionProblem(ModInfo mod, LauncherDependency dep, ModInfo target, List<string> problems, Func<string, string> t)
        {
            if (string.IsNullOrWhiteSpace(dep.minVersion)) return false;
            if (!LauncherRequirement.TryParse(dep.minVersion, out Version needed))
            {
                problems.Add(NamedText.Fill(t("DependencyMinVersionInvalid"), ("Mod", Name(mod)), ("Version", dep.minVersion), ("Target", Label(dep))));
                return true;
            }
            if (!LauncherRequirement.TryParse(target.Version, out Version have))
            {
                problems.Add(string.IsNullOrWhiteSpace(target.Version)
                    ? NamedText.Fill(t("DependencyVersionUnusable"), ("Mod", Name(mod)), ("Target", Label(dep)), ("Version", dep.minVersion), ("Other", Name(target)))
                    : NamedText.Fill(t("DependencyVersionUnusableStated"), ("Mod", Name(mod)), ("Target", Label(dep)), ("Version", dep.minVersion), ("Other", Name(target)), ("Stated", target.Version)));
                return true;
            }
            if (have < needed)
            {
                problems.Add(NamedText.Fill(t("DependencyVersionTooOld"), ("Mod", Name(mod)), ("Target", Label(dep)), ("Version", dep.minVersion), ("Other", Name(target)), ("Found", target.Version)));
                return true;
            }
            return false;
        }

        /// <summary>The enabled mods <paramref name="mover"/> must load after (<paramref name="before"/>) and before (<paramref name="after"/>), by the rules on either side.</summary>
        private static void Relations(IList<ModInfo> enabled, ModInfo mover, List<ModInfo> before, List<ModInfo> after)
        {
            foreach (ModInfo mod in enabled)
            {
                foreach (LauncherDependency dep in mod.Dependencies ?? new List<LauncherDependency>())
                {
                    if (!NamesAMod(dep) || dep.loadAfter == dep.loadBefore) continue;
                    ModInfo target = Find(enabled, dep);
                    if (target == null || target == mod) continue;
                    ModInfo first = dep.loadAfter ? target : mod;   // the one that must load first
                    ModInfo second = dep.loadAfter ? mod : target;
                    if (second == mover) before.Add(first);
                    else if (first == mover) after.Add(second);
                }
            }
        }

        /// <summary>The enabled mods <paramref name="mover"/> must load after (<paramref name="mustFollow"/>) and before (<paramref name="mustPrecede"/>), by the rules on either side.</summary>
        public static void Limits(IList<ModInfo> enabled, ModInfo mover, out List<ModInfo> mustFollow, out List<ModInfo> mustPrecede)
        {
            mustFollow = new List<ModInfo>();
            mustPrecede = new List<ModInfo>();
            Relations(enabled.Contains(mover) ? enabled : enabled.Concat(new[] { mover }).ToList(), mover, mustFollow, mustPrecede);
            mustFollow = mustFollow.Distinct().ToList();
            mustPrecede = mustPrecede.Distinct().ToList();
        }

        /// <summary>The loadAfter / loadBefore rules that involve <paramref name="mover"/> and that the given order of the enabled mods breaks, one line each.</summary>
        public static List<string> Violations(IList<ModInfo> ordered, ModInfo mover, Func<string, string> t)
        {
            List<string> broken = new List<string>();
            int at = ordered.IndexOf(mover);
            if (at < 0) return broken;
            List<ModInfo> before = new List<ModInfo>(), after = new List<ModInfo>();
            Relations(ordered, mover, before, after);
            foreach (ModInfo b in before.Distinct()) if (ordered.IndexOf(b) > at) broken.Add(NamedText.Fill(t("LoadOrderMustFollow"), ("Mod", Name(mover)), ("Other", Name(b))));
            foreach (ModInfo a in after.Distinct()) if (ordered.IndexOf(a) < at) broken.Add(NamedText.Fill(t("LoadOrderMustPrecede"), ("Mod", Name(mover)), ("Other", Name(a))));
            return broken;
        }

        /// <summary>
        /// Where a mod being enabled goes among the <paramref name="others"/> (the enabled mods in load order): the place asked for, or the nearest
        /// one its rules allow. The result is the index to insert at (0 to others.Count). Rules that contradict each other leave the place asked for;
        /// the launch check reports those.
        /// </summary>
        public static int NearestValidIndex(IList<ModInfo> others, ModInfo mover, int desired)
        {
            List<ModInfo> all = others.Concat(new[] { mover }).ToList();
            List<ModInfo> before = new List<ModInfo>(), after = new List<ModInfo>();
            Relations(all, mover, before, after);
            int lower = before.Select(b => others.IndexOf(b) + 1).DefaultIfEmpty(0).Max();
            int upper = after.Select(a => others.IndexOf(a)).DefaultIfEmpty(others.Count).Min();
            desired = Math.Max(0, Math.Min(desired, others.Count));
            return lower > upper ? desired : Math.Max(lower, Math.Min(upper, desired));
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
        public static OrderResult Enforce(IList<ModInfo> ordered, Func<string, string> t)
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
                    if (!NamesAMod(dep)) continue;
                    ModInfo target = Find(ordered, dep);
                    int j = target == null ? -1 : ordered.IndexOf(target);
                    if (j < 0 || j == i) continue;
                    if (dep.loadAfter && !dep.loadBefore)
                    {
                        before[i].Add(j);
                        if (j > i) result.Moves.Add(NamedText.Fill(t("LoadOrderMustFollow"), ("Mod", Name(ordered[i])), ("Other", Name(target))));
                    }
                    else if (dep.loadBefore && !dep.loadAfter)
                    {
                        before[j].Add(i);
                        if (i > j) result.Moves.Add(NamedText.Fill(t("LoadOrderMustPrecede"), ("Mod", Name(ordered[i])), ("Other", Name(target))));
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
