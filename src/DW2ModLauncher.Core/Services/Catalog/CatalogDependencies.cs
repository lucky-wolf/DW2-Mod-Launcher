using System;
using System.Collections.Generic;
using System.Linq;
using DW2ModLauncher.Core.Models;

namespace DW2ModLauncher.Core.Services.Catalog
{
    /// <summary>One dependency of a catalog entry that is not installed.</summary>
    public class MissingDependency
    {
        public CatalogDependency Dependency { get; set; }
        /// <summary>The catalog's listing of it, or null when the catalog has none (then it must come from elsewhere, e.g. Steam).</summary>
        public CatalogEntry Entry { get; set; }
        public bool Required => Dependency.Required;
    }

    /// <summary>Which dependencies of a catalog entry the player still lacks.</summary>
    public static class CatalogDependencies
    {
        /// <summary>
        /// The entry's dependencies that no installed mod satisfies (enabled or not; enabling is the launcher's dependency check at launch).
        /// A dependency is identified by uid, else Workshop id, else name, exactly as <see cref="ModDependencies.Find"/> does for an installed mod.
        /// Each comes with the catalog's own listing of it when there is one.
        /// </summary>
        public static List<MissingDependency> Missing(CatalogEntry entry, IReadOnlyCollection<CatalogEntry> catalog, IEnumerable<ModInfo> installed)
        {
            List<ModInfo> mods = installed.ToList();
            List<MissingDependency> missing = new List<MissingDependency>();
            foreach (CatalogDependency dep in entry.Dependencies)
            {
                CatalogEntry listed = ListingOf(dep, catalog);
                bool present = listed != null
                    ? CatalogStatusFinder.Find(listed, mods).State != CatalogInstallState.NotInstalled
                    : ModDependencies.Find(mods, AsLauncherDependency(dep)) != null;
                if (!present) missing.Add(new MissingDependency { Dependency = dep, Entry = listed });
            }
            return missing;
        }

        /// <summary>The catalog entry a dependency points at: by uid when it has one (and then only by it), else by Workshop id, else by name; null when none matches.</summary>
        public static CatalogEntry ListingOf(CatalogDependency dep, IEnumerable<CatalogEntry> catalog)
        {
            if (dep.Uid != null) return catalog.FirstOrDefault(e => ModUid.Same(e.Uid, dep.Uid)); // another uid is another mod, whatever it is called
            if (dep.WorkshopId != null) return catalog.FirstOrDefault(e => e.WorkshopId == dep.WorkshopId);
            return catalog.FirstOrDefault(e => string.Equals(e.Name, dep.Mod, StringComparison.OrdinalIgnoreCase));
        }

        private static LauncherDependency AsLauncherDependency(CatalogDependency dep)
        {
            return new LauncherDependency
            {
                uid = dep.Uid,
                mod = dep.Mod,
                workshopId = dep.WorkshopId != null ? long.Parse(dep.WorkshopId, System.Globalization.CultureInfo.InvariantCulture) : (long?)null
            };
        }
    }
}
