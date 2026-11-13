using System.Collections.Generic;
using System.Linq;
using DW2ModLauncher.Core.Models;

namespace DW2ModLauncher.Core.Services.Publishing
{
    /// <summary>What a publish should make the Steam page list as the mod's dependencies, and which listed mods cannot be linked.</summary>
    public class SteamLinkPlan
    {
        /// <summary>Workshop ids to link, without duplicates, in the order the mod lists them.</summary>
        public List<long> Wanted = new List<long>();
        /// <summary>Names of mods that should be linked but have no Workshop item yet (or none could be found), so cannot be.</summary>
        public List<string> Unlinkable = new List<string>();
    }

    /// <summary>
    /// Which of a mod's dw2modlauncher.json dependencies are linked on its Steam page (Workshop "Required items"). A required dependency is
    /// linked; an optional one when its "steamLink" says so, else when the launcher setting says so. "steamLink": false wins over required.
    /// </summary>
    public static class SteamLinks
    {
        public static bool IsLinked(LauncherDependency dep, bool linkOptional)
        {
            if (!ModDependencies.NamesAMod(dep)) return false;
            return dep.steamLink ?? (dep.required || linkOptional);
        }

        /// <summary>A copy of the entry, so edits in a dialog change nothing until they are saved.</summary>
        public static LauncherDependency Clone(LauncherDependency dep)
        {
            return new LauncherDependency { mod = dep.mod, uid = dep.uid, workshopId = dep.workshopId, required = dep.required, steamLink = dep.steamLink, minVersion = dep.minVersion, loadAfter = dep.loadAfter, loadBefore = dep.loadBefore };
        }

        /// <summary>The entry's fields on one line, for telling whether a list was edited.</summary>
        public static string Key(LauncherDependency dep)
        {
            return string.Join("|", dep.mod, dep.uid, dep.workshopId, dep.required, dep.steamLink, dep.minVersion, dep.loadAfter, dep.loadBefore);
        }

        public static SteamLinkPlan Plan(ModInfo mod, IList<ModInfo> all, bool linkOptional)
        {
            SteamLinkPlan plan = new SteamLinkPlan();
            foreach (LauncherDependency dep in mod.Dependencies ?? new List<LauncherDependency>())
            {
                if (!IsLinked(dep, linkOptional)) continue;
                ModInfo target = ModDependencies.Find(all, dep);
                if (target == mod) continue;
                long id = 0;
                string text = target == null ? null : (target.IsWorkshop ? target.Id : target.WorkshopId);
                if (!(long.TryParse(text?.Trim(), out id) && id > 0)) id = dep.workshopId ?? 0;
                if (id <= 0) plan.Unlinkable.Add(ModDependencies.Label(dep));
                else if (!plan.Wanted.Contains(id)) plan.Wanted.Add(id);
            }
            return plan;
        }

        /// <summary>The ids to add and (when trimming) to remove to turn the page's current <paramref name="listed"/> links into <paramref name="wanted"/>.</summary>
        public static void Diff(IReadOnlyCollection<long> wanted, IReadOnlyCollection<long> listed, bool trim, out List<long> add, out List<long> remove)
        {
            add = wanted.Where(id => !listed.Contains(id)).ToList();
            remove = trim ? listed.Where(id => !wanted.Contains(id)).ToList() : new List<long>();
        }
    }
}
