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

namespace DW2ModLauncher.App
{
    public partial class MainForm
    {
        private void BeginWorkshopUpdateCheck(bool force)
        {
            if (updateCheckRunning || currentWorkshopMods == null || currentWorkshopMods.Count == 0) return;
            updateCheckRunning = true;
            workshopCheckWasManual = force;
            if (workshopUpdateButton != null) workshopUpdateButton.Enabled = false;
            SetStatus(T("CheckingSteamWorkshopUpdates"));

            List<ModInfo> workshopSnapshot = currentWorkshopMods.ToList();
            string workshopRoot = settings.WorkshopRoot;
            BackgroundWorker worker = new BackgroundWorker();
            worker.DoWork += delegate (object sender, DoWorkEventArgs e) { e.Result = WorkshopUpdateService.Check(workshopRoot, workshopSnapshot); };
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
            int updates = WorkshopUpdateService.Apply(r, currentWorkshopMods);
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
                int count = WorkshopUpdateService.Backup(root, mods);
                MessageBox.Show(T("WorkshopBackupsSaved") + count + "\r\n" + root, Text);
            }
            catch (Exception ex) { Logger.LogException("Workshop backup", ex); MessageBox.Show(ex.Message, Text); }
        }

        private string UnixTimeText(long unix) { return WorkshopUpdateService.UnixTimeText(unix); }
    }
}
