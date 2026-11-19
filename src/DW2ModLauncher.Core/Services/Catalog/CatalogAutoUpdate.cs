using System;
using System.Collections.Generic;
using System.Linq;
using DW2ModLauncher.Core.Models;
using DW2ModLauncher.Core.Services.Updates;

namespace DW2ModLauncher.Core.Services.Catalog
{
    /// <summary>A newer catalog release of a mod that is installed from the catalog.</summary>
    public class CatalogPendingUpdate
    {
        public CatalogEntry Entry { get; set; }
        public ModInfo Installed { get; set; }
    }

    /// <summary>
    /// Which installed mods the catalog has a newer release of, and may be updated without asking (Steam does the same for its subscriptions).
    /// Never the player's own mods (their folder is their work, not a download), never a Workshop subscription (Steam updates those), and never
    /// a mod whose newer release needs a newer launcher.
    /// </summary>
    public static class CatalogAutoUpdate
    {
        /// <summary>A local mod that is the player's own: it names no author yet, or names the player's Author setting.</summary>
        public static bool IsOwn(ModInfo mod, string authorName)
        {
            string author = (mod.Author ?? "").Trim();
            return author.Length == 0 || string.Equals(author, (authorName ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Whether the catalog could have anything for the player: a local mod that someone else made is installed. Without one the catalog need not be contacted.</summary>
        public static bool HasCandidates(IEnumerable<ModInfo> installed, string authorName)
        {
            return installed.Any(m => !m.IsWorkshop && !IsOwn(m, authorName));
        }

        public static List<CatalogPendingUpdate> Find(CatalogIndex index, IEnumerable<ModInfo> installed, string modsRoot, string authorName, string launcherVersion)
        {
            List<ModInfo> mods = installed.ToList();
            List<CatalogPendingUpdate> found = new List<CatalogPendingUpdate>();
            foreach (CatalogEntry entry in index.Entries)
            {
                CatalogStatus status = CatalogStatusFinder.Find(entry, mods);
                if (status.State != CatalogInstallState.UpdateAvailable) continue;
                if (IsOwn(status.Installed, authorName)) continue;
                if (!LocalModManager.CanDelete(status.Installed, modsRoot)) continue;
                if (entry.MinLauncherVersion != null && UpdateChecker.IsNewer(entry.MinLauncherVersion, launcherVersion)) continue;
                found.Add(new CatalogPendingUpdate { Entry = entry, Installed = status.Installed });
            }
            return found;
        }
    }
}
