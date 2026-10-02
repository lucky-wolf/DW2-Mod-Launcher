using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DW2ModLauncher.Core.Models;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>Workshop update detection and pre-update backups. No UI; Check blocks, so run it off the UI thread.</summary>
    public static class WorkshopUpdateService
    {
        /// <summary>Never throws: failures land in the result's Error.</summary>
        public static WorkshopUpdateCheckResult Check(string workshopRoot, IEnumerable<ModInfo> workshopMods)
        {
            WorkshopUpdateCheckResult r = new WorkshopUpdateCheckResult();
            try
            {
                string manifestPath = AcfManifest.FindManifestPath(workshopRoot, SteamLocator.AppId);
                List<string> ids = workshopMods.Where(m => m != null).Select(m => m.Id)
                    .Where(id => !string.IsNullOrWhiteSpace(id) && Regex.IsMatch(id, "^\\d+$")).Distinct().ToList();
                if (!string.IsNullOrWhiteSpace(manifestPath) && File.Exists(manifestPath))
                {
                    string acf = File.ReadAllText(manifestPath, Encoding.UTF8);
                    r.InstalledTimes = AcfManifest.ParseSectionTimes(acf, "WorkshopItemsInstalled");
                    r.DetailTimes = AcfManifest.ParseSectionTimes(acf, "WorkshopItemDetails");
                }
                try
                {
                    Dictionary<string, WorkshopRemoteDetail> details;
                    r.RemoteTimes = WorkshopApiClient.FetchRemoteTimes(ids, out details);
                    r.Details = details;
                }
                catch (Exception ex) { r.Error = ex.Message; }
            }
            catch (Exception ex) { r.Error = ex.Message; }
            return r;
        }

        /// <summary>Copies the result onto the mods (state, times, Workshop details) and returns how many have updates.</summary>
        public static int Apply(WorkshopUpdateCheckResult r, IEnumerable<ModInfo> workshopMods)
        {
            int updates = 0;
            foreach (ModInfo mod in workshopMods)
            {
                if (mod == null) continue;
                long installed = 0;
                long detail = 0;
                long remote = 0;
                if (r.InstalledTimes != null) r.InstalledTimes.TryGetValue(mod.Id ?? "", out installed);
                if (r.DetailTimes != null) r.DetailTimes.TryGetValue(mod.Id ?? "", out detail);
                if (r.RemoteTimes != null) r.RemoteTimes.TryGetValue(mod.Id ?? "", out remote);
                WorkshopRemoteDetail remoteDetail = null;
                if (r.Details != null) r.Details.TryGetValue(mod.Id ?? "", out remoteDetail);
                if (remoteDetail != null)
                {
                    mod.WorkshopTitle = remoteDetail.Title;
                    mod.WorkshopDescription = remoteDetail.Description;
                    mod.WorkshopPreviewUrl = remoteDetail.PreviewUrl;
                    mod.WorkshopCreator = remoteDetail.Creator;
                    mod.WorkshopFileSize = remoteDetail.FileSize;
                    mod.WorkshopTimeCreated = remoteDetail.TimeCreated;
                    mod.WorkshopTags = remoteDetail.Tags;
                    if (!string.IsNullOrWhiteSpace(remoteDetail.Title)) mod.DisplayName = remoteDetail.Title;
                    if (!string.IsNullOrWhiteSpace(remoteDetail.Description)) mod.Description = remoteDetail.Description;
                }
                mod.LocalWorkshopTimeUpdated = installed;
                mod.RemoteWorkshopTimeUpdated = remote > 0 ? remote : detail;
                long latest = Math.Max(detail, remote);
                if (installed > 0 && latest > installed + 2)
                {
                    mod.UpdateState = "update";
                    updates++;
                }
                else if (installed > 0 && latest > 0)
                    mod.UpdateState = "current";
                else
                    mod.UpdateState = "unknown";
            }
            return updates;
        }

        /// <summary>Copies each mod's folder under backupRoot and returns how many were copied. Throws on I/O failure.</summary>
        public static int Backup(string backupRoot, IEnumerable<ModInfo> mods)
        {
            int count = 0;
            foreach (ModInfo mod in mods ?? Enumerable.Empty<ModInfo>())
            {
                if (mod == null || string.IsNullOrWhiteSpace(mod.Folder) || !Directory.Exists(mod.Folder)) continue;
                FileNames.CopyDirectory(mod.Folder, Path.Combine(backupRoot, FileNames.Safe(mod.Id + "_v" + (mod.Version ?? "unknown"))));
                count++;
            }
            return count;
        }

        public static string UnixTimeText(long unix)
        {
            try
            {
                DateTime dt = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(unix).ToLocalTime();
                return dt.ToString("yyyy/MM/dd HH:mm");
            }
            catch { return unix.ToString(); }
        }
    }
}
