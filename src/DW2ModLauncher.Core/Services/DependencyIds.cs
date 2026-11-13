using System;
using System.Collections.Generic;
using System.Linq;
using DW2ModLauncher.Core.Diagnostics;
using DW2ModLauncher.Core.Models;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>
    /// Completes the dependencies a local mod writes down, once the other mod can be identified: a missing "uid" is written from the other mod's own uid and a missing
    /// "workshopId" from its mod.json "workshopId" (or the folder of a subscribed Workshop mod), beside the entry's "mod". Both are kept: an id already there is never
    /// replaced and none is ever removed, so a dependency named by Workshop id alone gains its uid and one named by uid alone gains its Workshop id. Workshop mods' files
    /// are never touched.
    /// </summary>
    public static class DependencyIds
    {
        /// <summary>Completes the dependencies of the <paramref name="local"/> mods against <paramref name="all"/>; returns the mod and the error for each file that could not be written.</summary>
        public static List<(string Mod, string Problem)> Resolve(IList<ModInfo> all, IEnumerable<ModInfo> local)
        {
            List<(string Mod, string Problem)> problems = new List<(string Mod, string Problem)>();
            foreach (ModInfo mod in local.Where(m => m != null && !m.IsWorkshop && !string.IsNullOrWhiteSpace(m.Folder)))
            {
                Dictionary<int, (string Uid, long? WorkshopId)> found = new Dictionary<int, (string Uid, long? WorkshopId)>();
                List<LauncherDependency> deps = mod.Dependencies ?? new List<LauncherDependency>();
                for (int i = 0; i < deps.Count; i++)
                {
                    LauncherDependency dep = deps[i];
                    if (dep == null) continue;
                    bool needUid = string.IsNullOrWhiteSpace(dep.uid);
                    bool needId = !dep.workshopId.HasValue;
                    if (!needUid && !needId) continue;
                    ModInfo target = ModDependencies.Find(all, dep);
                    if (target == null || target == mod) continue;
                    string uid = needUid && !string.IsNullOrWhiteSpace(target.Uid) ? target.Uid : null;
                    string id = target.IsWorkshop ? target.Id : target.WorkshopId;
                    long? workshopId = needId && long.TryParse(id?.Trim(), out long parsed) && parsed > 0 ? parsed : (long?)null;
                    if (uid != null || workshopId.HasValue) found[i] = (uid, workshopId);
                }
                if (found.Count == 0) continue;
                try
                {
                    LauncherMetaWriter.AddDependencyIds(mod.Folder, found);
                    foreach (KeyValuePair<int, (string Uid, long? WorkshopId)> pair in found)
                    {
                        if (pair.Value.Uid != null) deps[pair.Key].uid = pair.Value.Uid;
                        if (pair.Value.WorkshopId.HasValue) deps[pair.Key].workshopId = pair.Value.WorkshopId;
                    }
                    Logger.Log("Dependency ids", mod.DisplayName + ": " + string.Join(", ", found.Select(p => (deps[p.Key].mod ?? "(by id)") + " = "
                        + string.Join(" and ", new[] { p.Value.Uid, p.Value.WorkshopId?.ToString() }.Where(x => x != null)))));
                }
                catch (Exception ex)
                {
                    Logger.LogException("Write dependency ids of " + mod.DisplayName, ex);
                    problems.Add((mod.DisplayName ?? mod.Id, ex.Message));
                }
            }
            return problems;
        }
    }
}
