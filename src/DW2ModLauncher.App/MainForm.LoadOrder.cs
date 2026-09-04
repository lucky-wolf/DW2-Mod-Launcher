using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;
using DW2ModLauncher.Core.Diagnostics;
using DW2ModLauncher.Core.Models;

namespace DW2ModLauncherBeta
{
    public partial class MainForm
    {
        private string ModsJsonPath()
        {
            string root = settings.ManagedModsRoot;
            if (string.IsNullOrWhiteSpace(root) && !string.IsNullOrWhiteSpace(settings.GameRoot)) root = Path.Combine(settings.GameRoot, "mods");
            return string.IsNullOrWhiteSpace(root) ? null : Path.Combine(root, "mods.json");
        }

        private void LoadModOrder()
        {
            currentModOrder = new List<string>();
            modOrderFileFound = false;
            modOrderReadFailed = false;
            string path = ModsJsonPath();
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
                ModOrderDocument document = JsonSerializer.Deserialize<ModOrderDocument>(File.ReadAllText(path, Encoding.UTF8));
                if (document == null || document.order == null) { modOrderReadFailed = true; return; }
                currentModOrder = document.order.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                modOrderFileFound = true;
            }
            catch (Exception ex) { modOrderReadFailed = true; Logger.LogException("Read DW2 mods.json", ex); }
        }

        private bool IsGameRunning()
        {
            try { return Process.GetProcessesByName("DistantWorlds2").Length > 0; }
            catch { return false; }
        }

        private void SaveModOrderSelection(ModInfo mod, bool enabled)
        {
            if (populating || mod == null || string.IsNullOrWhiteSpace(mod.ActiveToken)) return;
            if (modOrderReadFailed)
            {
                MessageBox.Show(T("mods.jsonが壊れているか読み取れないため、上書きを中止しました。", "mods.json is invalid or unreadable. The launcher will not overwrite it."), Text);
                RefreshAll();
                return;
            }
            if (IsGameRunning())
            {
                MessageBox.Show(T("DW2起動中はMOD設定を変更できません。ゲーム終了後に変更してください。", "MOD settings cannot be changed while DW2 is running."), Text);
                RefreshAll();
                return;
            }
            string path = ModsJsonPath();
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(Path.GetDirectoryName(path))) return;
            List<string> next = new List<string>(currentModOrder ?? new List<string>());
            next.RemoveAll(x => x.Equals(mod.ActiveToken, StringComparison.OrdinalIgnoreCase));
            if (enabled) next.Add(mod.ActiveToken);
            ModOrderDocument document = new ModOrderDocument();
            document.order = next;
            string temp = path + ".launcher_tmp";
            string backup = path + ".launcher_backup";
            try
            {
                string output = JsonSerializer.Serialize(document);
                JsonSerializer.Deserialize<ModOrderDocument>(output);
                File.WriteAllText(temp, output, new UTF8Encoding(false));
                if (File.Exists(path))
                {
                    File.Copy(path, backup, true);
                    try { File.Replace(temp, path, null, true); }
                    catch { File.Copy(temp, path, true); File.Delete(temp); }
                }
                else File.Move(temp, path);
                currentModOrder = next;
                modOrderFileFound = true;
                SetStatus(T("DW2のMOD設定を保存しました。", "DW2 MOD settings saved."));
            }
            catch (Exception ex)
            {
                Logger.LogException("Write DW2 mods.json", ex);
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                MessageBox.Show(T("mods.jsonの保存に失敗しました。", "Failed to save mods.json.") + "\r\n" + ex.Message, Text);
                LoadModOrder();
            }
        }

        private void SaveLoadOrderFromList(ListView list)
        {
            if (list == null || IsGameRunning()) return;
            List<string> ordered = list.Items.Cast<ListViewItem>()
                .Select(i => i.Tag as ModInfo).Where(m => m != null && IsModSelected(m))
                .Select(m => m.ActiveToken).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            HashSet<string> category = new HashSet<string>(ordered, StringComparer.OrdinalIgnoreCase);
            List<string> next = new List<string>();
            int replacement = 0;
            foreach (string token in currentModOrder ?? new List<string>())
            {
                if (category.Contains(token))
                {
                    if (replacement < ordered.Count) next.Add(ordered[replacement++]);
                }
                else next.Add(token);
            }
            while (replacement < ordered.Count) next.Add(ordered[replacement++]);
            WriteModOrder(next);
        }

        private bool WriteModOrder(List<string> order)
        {
            string path = ModsJsonPath();
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(Path.GetDirectoryName(path))) return false;
            string temp = path + ".launcher_tmp";
            try
            {
                ModOrderDocument document = new ModOrderDocument();
                document.order = (order ?? new List<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                File.WriteAllText(temp, JsonSerializer.Serialize(document), new UTF8Encoding(false));
                if (File.Exists(path))
                {
                    File.Copy(path, path + ".launcher_backup", true);
                    try { File.Replace(temp, path, null, true); }
                    catch { File.Copy(temp, path, true); File.Delete(temp); }
                }
                else File.Move(temp, path);
                currentModOrder = document.order;
                modOrderFileFound = true;
                SetStatus(T("ロード順を保存しました。", "Load order saved."));
                UpdateCommandPreview();
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogException("Write load order", ex);
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                MessageBox.Show(T("ロード順を保存できませんでした。", "Could not save load order.") + "\r\n" + ex.Message, Text);
                return false;
            }
        }

        private void RefreshLoadOrderNumbers()
        {
            foreach (ListView list in new ListView[] { managedList, workshopList })
            {
                if (list == null) continue;
                foreach (ListViewItem item in list.Items)
                {
                    ModInfo mod = item.Tag as ModInfo;
                    if (mod == null || item.SubItems.Count < 9) continue;
                    int index = currentModOrder == null ? -1 : currentModOrder.FindIndex(x => string.Equals(x, mod.ActiveToken, StringComparison.OrdinalIgnoreCase));
                    item.SubItems[8].Text = index < 0 ? "—" : (index + 1).ToString(CultureInfo.InvariantCulture);
                    item.SubItems[8].ForeColor = index < 0 ? Dw2Muted : Dw2Gold;
                }
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
