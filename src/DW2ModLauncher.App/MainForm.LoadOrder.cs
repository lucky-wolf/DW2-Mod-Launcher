using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DW2ModLauncher.Core.Diagnostics;
using DW2ModLauncher.Core.Models;
using DW2ModLauncher.Core.Services;

namespace DW2ModLauncher.App
{
    public partial class MainForm
    {
        private string ModsJsonPath() { return ModOrderStore.PathFor(settings.ManagedModsRoot, settings.GameRoot); }

        private void LoadModOrder() { modOrder = ModOrderStore.Read(ModsJsonPath()); }

        private bool IsGameRunning() { return GameProcess.IsRunning(); }

        private void SaveModOrderSelection(ModInfo mod, bool enabled)
        {
            if (populating || mod == null || string.IsNullOrWhiteSpace(mod.ActiveToken)) return;
            SetEnabledResult result = ModLibrary.SaveOrderSelection(mod, enabled, settings, modOrder);
            switch (result.Outcome)
            {
                case SetEnabledOutcome.ModsJsonInvalid:
                    MessageBox.Show(T("ModsJsonInvalidWarning"), Text);
                    RefreshAll();
                    break;
                case SetEnabledOutcome.GameRunning:
                    MessageBox.Show(T("GameRunningWarning"), Text);
                    RefreshAll();
                    break;
                case SetEnabledOutcome.Saved:
                    SetStatus(T("DW2ModSettingsSaved"));
                    break;
                case SetEnabledOutcome.WriteFailed:
                    Logger.LogException("Write DW2 mods.json", result.Error);
                    MessageBox.Show("Failed to save mods.json.\r\n" + result.Error.Message, Text);
                    LoadModOrder();
                    break;
            }
        }

        private void SaveLoadOrderFromList(ListView list)
        {
            if (list == null || IsGameRunning()) return;
            List<string> ordered = list.Items.Cast<ListViewItem>()
                .Select(i => i.Tag as ModInfo).Where(m => m != null && IsModSelected(m))
                .Select(m => m.ActiveToken).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            WriteModOrder(ModOrderStore.Reorder(modOrder.Order, ordered));
        }

        private bool WriteModOrder(List<string> order)
        {
            try
            {
                List<string> written = ModOrderStore.Write(ModsJsonPath(), order);
                if (written == null) return false;
                modOrder.Order = written;
                modOrder.FileFound = true;
                SetStatus(T("LoadOrderSaved"));
                UpdateCommandPreview();
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogException("Write load order", ex);
                MessageBox.Show("Could not save load order.\r\n" + ex.Message, Text);
                return false;
            }
        }

        private void RefreshLoadOrderNumbers()
        {
            if (modList == null) return;
            foreach (ListViewItem item in modList.Items)
            {
                ModInfo mod = item.Tag as ModInfo;
                if (mod == null || item.SubItems.Count <= ColumnLoadOrder) continue;
                int index = modOrder.IndexOf(mod.ActiveToken);
                item.SubItems[ColumnLoadOrder].Text = index < 0 ? "—" : (index + 1).ToString(CultureInfo.InvariantCulture);
                item.SubItems[ColumnLoadOrder].ForeColor = index < 0 ? Dw2Muted : Dw2Gold;
            }
        }

        private void ApplyAlternatingRowColors(ListView list)
        {
            if (list == null) return;
            for (int i = 0; i < list.Items.Count; i++)
            {
                Color rowBack = (i % 2 == 0) ? Dw2Deep : Dw2Panel;
                list.Items[i].BackColor = rowBack;
                foreach (ListViewItem.ListViewSubItem subItem in list.Items[i].SubItems) subItem.BackColor = rowBack;
            }
        }
    }
}
