using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using DW2ModLauncher.Core.Models;
using DW2ModLauncher.Core.Services;

namespace DW2ModLauncher.App
{
    public partial class MainForm
    {
        private void AnalyzeConflicts()
        {
            if (currentManagedMods == null) currentManagedMods = new List<ModInfo>();
            if (currentWorkshopMods == null) currentWorkshopMods = new List<ModInfo>();
            currentCollisions = ConflictAnalyzer.Analyze(currentManagedMods.Concat(currentWorkshopMods), IsModEnabledForConflict);
        }

        private void AnalyzeDuplicates()
        {
            ConflictAnalyzer.AnalyzeDuplicates(currentManagedMods.Concat(currentWorkshopMods));
        }

        private void RefreshModStatusColumns()
        {
            RefreshOneModListStatus(modList);
            UpdateOverallStatus();
        }

        private int HealthSeverity(ModInfo mod) { return ModHealth.Severity(mod, IsModSelected(mod)); }

        private void RefreshOneModListStatus(ListView list)
        {
            if (list == null) return;
            foreach (ListViewItem item in list.Items)
            {
                ModInfo mod = item.Tag as ModInfo;
                if (mod == null || item.SubItems.Count <= ColumnLoadOrder) continue;
                item.UseItemStyleForSubItems = false;

                bool enabled = IsModSelected(mod);
                item.SubItems[ColumnModState].Text = enabled ? CheckedGlyph : UncheckedGlyph;
                item.SubItems[ColumnModState].ForeColor = enabled ? Dw2Green : Dw2Muted;

                switch (HealthSeverity(mod))
                {
                    case 3:
                        item.SubItems[ColumnHealth].Text = T("HealthConflict");
                        item.SubItems[ColumnHealth].ForeColor = Dw2Red;
                        break;
                    case 2:
                        item.SubItems[ColumnHealth].Text = T("HealthCaution");
                        item.SubItems[ColumnHealth].ForeColor = Dw2Gold;
                        break;
                    case 1:
                        item.SubItems[ColumnHealth].Text = T("HealthOk");
                        item.SubItems[ColumnHealth].ForeColor = Dw2Green;
                        break;
                    default:
                        item.SubItems[ColumnHealth].Text = T("ModDisabled");
                        item.SubItems[ColumnHealth].ForeColor = Dw2Muted;
                        break;
                }
            }
        }

        private void RefreshSelectedDetails()
        {
            if (modList != null && modList.SelectedItems.Count > 0)
                ShowModDetails(modList.SelectedItems[0].Tag as ModInfo, modPreview, modName, modProblemsPanel, modProblemsLabel, modDesc);
        }

        private void UpdateOverallStatus()
        {
            if (statusLabel == null) return;
            if (currentManagedMods == null) currentManagedMods = new List<ModInfo>();
            if (currentWorkshopMods == null) currentWorkshopMods = new List<ModInfo>();
            if (currentCollisions == null) currentCollisions = new Dictionary<string, List<ModInfo>>(StringComparer.OrdinalIgnoreCase);
            int updates = currentWorkshopMods.Count(m => m != null && m.UpdateState == "update");
            int selected = currentManagedMods.Concat(currentWorkshopMods).Where(m => m != null).Count(IsModSelected);
            string conflictText = currentCollisions.Count == 0 ? T("NoConflictsStatus") : T("ConflictFiles") + currentCollisions.Count;
            string updateText = updates == 0 ? T("NoUpdatesUnchecked") : T("Updates") + updates;
            if (modOrder.ReadFailed) updateText = T("ModsJsonERROR");
            int duplicates = currentManagedMods.Concat(currentWorkshopMods).Count(m => m != null && m.DuplicateCount > 0);
            string duplicateText = duplicates == 0 ? T("NoDuplicateInstallations") : T("DuplicateInstallations") + duplicates;
            statusLabel.Text = string.Format(T("DW2ModsWorkshopEnabled"), currentManagedMods.Count, currentWorkshopMods.Count, selected, conflictText, duplicateText, updateText);
        }

        private string BuildConflictWarning()
        {
            StringBuilder b = new StringBuilder();
            b.AppendLine(T("ConflictWarningIntro"));
            b.AppendLine(T("ConflictWarningBody"));
            b.AppendLine();
            foreach (KeyValuePair<string, List<ModInfo>> kv in currentCollisions.Take(10))
            {
                b.AppendLine("• " + kv.Key);
                b.AppendLine("  " + string.Join("  ↔  ", kv.Value.Select(m => m.DisplayName ?? m.Id).ToArray()));
            }
            if (currentCollisions.Count > 10) b.AppendLine("... +" + (currentCollisions.Count - 10));
            b.AppendLine();
            b.AppendLine(T("LaunchAnywayConfirm"));
            return b.ToString();
        }

        private List<string> BuildLaunchDiagnostics()
        {
            List<ModInfo> enabled = (currentManagedMods ?? new List<ModInfo>()).Concat(currentWorkshopMods ?? new List<ModInfo>()).Where(IsModSelected).ToList();
            return LaunchDiagnostics.Build(enabled, modOrder, LoaderDllPath(), LoaderManifestBuilder.Build(OrderedEnabledMods()).Entries, key => T(key));
        }
    }
}
