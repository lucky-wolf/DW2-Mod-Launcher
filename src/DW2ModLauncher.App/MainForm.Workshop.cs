using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using DW2ModLauncher.Core.Diagnostics;
using DW2ModLauncher.Core.Models;
using DW2ModLauncher.Core.Services;

namespace DW2ModLauncherBeta
{
    public partial class MainForm
    {
        private string FindWorkshopManifestPath() { return AcfManifest.FindManifestPath(settings.WorkshopRoot, SteamLocator.AppId); }

        private Dictionary<string, long> ParseAcfSectionTimes(string text, string sectionName) { return AcfManifest.ParseSectionTimes(text, sectionName); }

        private void BeginWorkshopUpdateCheck(bool force)
        {
            if (updateCheckRunning || currentWorkshopMods == null || currentWorkshopMods.Count == 0) return;
            updateCheckRunning = true;
            workshopCheckWasManual = force;
            if (workshopUpdateButton != null) workshopUpdateButton.Enabled = false;
            SetStatus(T("CheckingSteamWorkshopUpdates"));

            string manifestPath = FindWorkshopManifestPath();
            List<string> ids = currentWorkshopMods.Where(m => m != null).Select(m => m.Id).Where(id => !string.IsNullOrWhiteSpace(id) && Regex.IsMatch(id, "^\\d+$")).Distinct().ToList();
            BackgroundWorker worker = new BackgroundWorker();
            worker.DoWork += delegate (object sender, DoWorkEventArgs e)
            {
                WorkshopUpdateCheckResult r = new WorkshopUpdateCheckResult();
                try
                {
                    if (!string.IsNullOrWhiteSpace(manifestPath) && File.Exists(manifestPath))
                    {
                        string acf = File.ReadAllText(manifestPath, Encoding.UTF8);
                        r.InstalledTimes = ParseAcfSectionTimes(acf, "WorkshopItemsInstalled");
                        r.DetailTimes = ParseAcfSectionTimes(acf, "WorkshopItemDetails");
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
                e.Result = r;
            };
            worker.RunWorkerCompleted += delegate (object sender, RunWorkerCompletedEventArgs e)
            {
                updateCheckRunning = false;
                if (workshopUpdateButton != null) workshopUpdateButton.Enabled = true;
                try
                {
                    if (e != null && e.Error != null)
                    {
                        Logger.LogException("Workshop background worker", e.Error);
                        SetStatus(T("WorkshopCheckFailed"));
                        UpdateOverallStatus();
                        return;
                    }
                    WorkshopUpdateCheckResult r = e == null ? null : e.Result as WorkshopUpdateCheckResult;
                    if (r != null) ApplyWorkshopUpdateResults(r);
                    else UpdateOverallStatus();
                }
                catch (Exception ex)
                {
                    Logger.LogException("Workshop completion", ex);
                    SetStatus(T("WorkshopResultsSkipped"));
                    UpdateOverallStatus();
                }
            };
            worker.RunWorkerAsync();
        }

        private void ApplyWorkshopUpdateResults(WorkshopUpdateCheckResult r)
        {
            if (r == null) return;
            if (currentWorkshopMods == null) currentWorkshopMods = new List<ModInfo>();
            int updates = 0;
            foreach (ModInfo mod in currentWorkshopMods)
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
            settings.LastWorkshopUpdateCheckUtc = DateTime.UtcNow.ToString("o");
            SaveSettings();
            AnalyzeDuplicates();
            if (modList != null)
                foreach (ListViewItem item in modList.Items)
                {
                    ModInfo itemMod = item.Tag as ModInfo;
                    if (itemMod != null && itemMod.IsWorkshop) item.Text = itemMod.DisplayName ?? itemMod.Id;
                }
            RefreshModStatusColumns();
            RefreshSelectedDetails();
            if (!string.IsNullOrWhiteSpace(r.Error))
                SetStatus(T("WorkshopCheckSteamFailed") + r.Error);
            else if (updates > 0)
            {
                SetStatus(T("WorkshopUpdatesAvailableStatus") + updates);
                if (workshopCheckWasManual)
                {
                    List<ModInfo> updateMods = currentWorkshopMods.Where(m => m != null && m.UpdateState == "update").ToList();
                    if (MessageBox.Show(T("BackupBeforeUpdatePrompt"),
                        T("PreUpdateBackup"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                        BackupWorkshopMods(updateMods);
                }
            }
            else
                SetStatus(T("SteamWorkshopNoUpdatesFound"));
        }

        private void BackupWorkshopMods(IEnumerable<ModInfo> mods)
        {
            string root = Path.Combine(appRoot, "WorkshopBackups", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            try
            {
                int count = 0;
                foreach (ModInfo mod in mods ?? Enumerable.Empty<ModInfo>())
                {
                    if (mod == null || string.IsNullOrWhiteSpace(mod.Folder) || !Directory.Exists(mod.Folder)) continue;
                    CopyDirectory(mod.Folder, Path.Combine(root, SafeFileName(mod.Id + "_v" + (mod.Version ?? "unknown"))));
                    count++;
                }
                MessageBox.Show(T("WorkshopBackupsSaved") + count + "\r\n" + root, Text);
            }
            catch (Exception ex) { Logger.LogException("Workshop backup", ex); MessageBox.Show(ex.Message, Text); }
        }

        private string UnixTimeText(long unix)
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
