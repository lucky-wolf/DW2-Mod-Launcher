using System;
using System.Collections.Generic;
using System.Linq;
using DW2ModLauncher.Core.Diagnostics;
using DW2ModLauncher.Core.Models;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>
    /// Turns the dependencies a local mod names by name into Workshop ids once the other mod has one (its mod.json "workshopId", or the folder of a
    /// subscribed Workshop mod): the id is written into the local mod's dw2modlauncher.json, beside the name. Workshop mods' files are never touched.
    /// </summary>
    public static class DependencyIds
    {
        /// <summary>Resolves the name-only dependencies of the <paramref name="local"/> mods against <paramref name="all"/>; returns the mod and the error for each file that could not be written.</summary>
        public static List<(string Mod, string Problem)> Resolve(IList<ModInfo> all, IEnumerable<ModInfo> local)
        {
            List<(string Mod, string Problem)> problems = new List<(string Mod, string Problem)>();
            foreach (ModInfo mod in local.Where(m => m != null && !m.IsWorkshop && !string.IsNullOrWhiteSpace(m.Folder)))
            {
                Dictionary<int, long> found = new Dictionary<int, long>();
                List<LauncherDependency> deps = mod.Dependencies ?? new List<LauncherDependency>();
                for (int i = 0; i < deps.Count; i++)
                {
                    LauncherDependency dep = deps[i];
                    if (dep == null || dep.workshopId.HasValue || string.IsNullOrWhiteSpace(dep.mod)) continue;
                    ModInfo target = ModDependencies.Find(all, dep);
                    if (target == null || target == mod) continue;
                    string id = target.IsWorkshop ? target.Id : target.WorkshopId;
                    if (long.TryParse(id?.Trim(), out long parsed) && parsed > 0) found[i] = parsed;
                }
                if (found.Count == 0) continue;
                try
                {
                    LauncherMetaWriter.AddDependencyWorkshopIds(mod.Folder, found);
                    foreach (KeyValuePair<int, long> pair in found) deps[pair.Key].workshopId = pair.Value;
                    Logger.Log("Dependency ids", mod.DisplayName + ": " + string.Join(", ", found.Select(p => deps[p.Key].mod + " = " + p.Value)));
                }
                catch (Exception ex)
                {
                    Logger.LogException("Write dependency Workshop ids of " + mod.DisplayName, ex);
                    problems.Add((mod.DisplayName ?? mod.Id, ex.Message));
                }
            }
            return problems;
        }
    }
}
