using System;
using System.Collections.Generic;
using System.Linq;
using DW2ModLauncher.Core.Models;
using DW2ModLauncher.Core.Services.Updates;

namespace DW2ModLauncher.Core.Services.Catalog
{
    public enum CatalogInstallState
    {
        /// <summary>No installed mod matches.</summary>
        NotInstalled,
        /// <summary>The installed local copy is this version or newer.</summary>
        UpToDate,
        /// <summary>The installed local copy is older than the catalog's.</summary>
        UpdateAvailable,
        /// <summary>A Steam Workshop subscription matches; Steam keeps it up to date, so the catalog offers nothing.</summary>
        FromSteam
    }

    public class CatalogStatus
    {
        public CatalogInstallState State { get; set; }
        /// <summary>The matching installed mod, or null when <see cref="State"/> is NotInstalled.</summary>
        public ModInfo Installed { get; set; }
    }

    /// <summary>Matches catalog entries to what is installed.</summary>
    public static class CatalogStatusFinder
    {
        /// <summary>
        /// The entry's <c>uid</c> is the identity: a Workshop subscription with the same uid (Steam keeps it current, the catalog offers
        /// nothing) or a local mod with it. A mod that has a different uid is a different mod, whatever it is called. Mods installed before
        /// they had a uid are matched the old way: by Workshop id when the entry has one (a subscription or a local mod whose mod.json carries
        /// it), else by a local folder of the same name.
        /// </summary>
        public static CatalogStatus Find(CatalogEntry entry, IEnumerable<ModInfo> installed)
        {
            List<ModInfo> mods = installed.ToList();
            ModInfo byUid = mods.FirstOrDefault(m => m.IsWorkshop && ModUid.Same(m.Uid, entry.Uid)) ?? mods.FirstOrDefault(m => ModUid.Same(m.Uid, entry.Uid));
            if (byUid != null) return Matched(entry, byUid);
            ModInfo local = null;
            foreach (ModInfo mod in mods)
            {
                if (mod.Uid != null) continue; // has an identity, and it is not this entry's
                if (entry.WorkshopId != null)
                {
                    if (ModDetails.WorkshopId(mod) != entry.WorkshopId) continue;
                    if (mod.IsWorkshop) return new CatalogStatus { State = CatalogInstallState.FromSteam, Installed = mod };
                    local = mod;
                }
                else if (!mod.IsWorkshop && local == null && string.Equals(mod.Id, entry.Name, StringComparison.OrdinalIgnoreCase))
                {
                    local = mod;
                }
            }
            if (local == null) return new CatalogStatus { State = CatalogInstallState.NotInstalled };
            return Matched(entry, local);
        }

        private static CatalogStatus Matched(CatalogEntry entry, ModInfo found)
        {
            if (found.IsWorkshop) return new CatalogStatus { State = CatalogInstallState.FromSteam, Installed = found };
            bool older = UpdateChecker.IsNewer(entry.Version, found.Version);
            return new CatalogStatus { State = older ? CatalogInstallState.UpdateAvailable : CatalogInstallState.UpToDate, Installed = found };
        }
    }
}
