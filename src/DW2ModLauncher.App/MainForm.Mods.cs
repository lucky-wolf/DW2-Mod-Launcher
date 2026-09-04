using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
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
        private List<ModInfo> ScanMods(string root, bool workshop) { return ModScanner.ScanMods(root, workshop, T); }

        private void RefreshAll()
        {
            EnsureSettingsState();
            LoadModOrder();
            Dictionary<string, ModInfo> workshopState = (currentWorkshopMods ?? new List<ModInfo>())
                .Where(m => m != null && !string.IsNullOrWhiteSpace(m.Id))
                .GroupBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            SafeStage("Refresh path labels", delegate
            {
                UpdatePathLabels();
                if (gameRootBox != null) gameRootBox.Text = settings.GameRoot ?? "";
                if (workshopRootBox != null) workshopRootBox.Text = settings.WorkshopRoot ?? "";
                if (managedRootBox != null) managedRootBox.Text = settings.ManagedModsRoot ?? "";
                if (launchArgsBox != null) launchArgsBox.Text = settings.GlobalLaunchArguments ?? "";
            });

            SafeStage("Scan Metapo mods", delegate { currentManagedMods = ScanMods(settings.ManagedModsRoot, false) ?? new List<ModInfo>(); });
            SafeStage("Scan Workshop mods", delegate { currentWorkshopMods = ScanMods(settings.WorkshopRoot, true) ?? new List<ModInfo>(); });
            if (currentManagedMods == null) currentManagedMods = new List<ModInfo>();
            if (currentWorkshopMods == null) currentWorkshopMods = new List<ModInfo>();
            RestoreWorkshopRuntimeState(currentWorkshopMods, workshopState);
            currentManagedMods = OrderModsForDisplay(currentManagedMods);
            currentWorkshopMods = OrderModsForDisplay(currentWorkshopMods);
            RefreshAiCommanderAvailability();
            if (currentCollisions == null) currentCollisions = new Dictionary<string, List<ModInfo>>(StringComparer.OrdinalIgnoreCase);

            SafeStage("Populate Metapo list", delegate { PopulateList(managedList, managedImages, currentManagedMods, true); });
            SafeStage("Populate Workshop list", delegate { PopulateList(workshopList, workshopImages, currentWorkshopMods, false); });
            SafeStage("Conflict analysis", delegate { AnalyzeConflicts(); });
            SafeStage("Duplicate analysis", delegate { AnalyzeDuplicates(); });
            SafeStage("Refresh status columns", delegate { RefreshModStatusColumns(); });
            SafeStage("Load AI Commander settings", delegate { LoadAiSettings(); });
            SafeStage("Build launch command", delegate { UpdateCommandPreview(); });
            SafeStage("Overall status", delegate { UpdateOverallStatus(); });
        }

        private void RestoreWorkshopRuntimeState(List<ModInfo> scanned, Dictionary<string, ModInfo> previous)
        {
            if (scanned == null || previous == null || previous.Count == 0) return;
            foreach (ModInfo mod in scanned)
            {
                ModInfo old;
                if (mod == null || string.IsNullOrWhiteSpace(mod.Id) || !previous.TryGetValue(mod.Id, out old) || old == null) continue;
                mod.UpdateState = old.UpdateState;
                mod.LocalWorkshopTimeUpdated = old.LocalWorkshopTimeUpdated;
                mod.RemoteWorkshopTimeUpdated = old.RemoteWorkshopTimeUpdated;
                mod.WorkshopDescription = old.WorkshopDescription;
                mod.WorkshopTitle = old.WorkshopTitle;
                mod.WorkshopPreviewUrl = old.WorkshopPreviewUrl;
                mod.WorkshopCreator = old.WorkshopCreator;
                mod.WorkshopFileSize = old.WorkshopFileSize;
                mod.WorkshopTimeCreated = old.WorkshopTimeCreated;
                mod.WorkshopTags = old.WorkshopTags;
                if (!string.IsNullOrWhiteSpace(old.WorkshopTitle)) mod.DisplayName = old.DisplayName;
                if (!string.IsNullOrWhiteSpace(old.WorkshopDescription)) mod.Description = old.Description;
            }
        }

        private List<ModInfo> OrderModsForDisplay(List<ModInfo> mods)
        {
            return (mods ?? new List<ModInfo>()).OrderBy(m =>
            {
                int index = currentModOrder == null ? -1 : currentModOrder.FindIndex(x => string.Equals(x, m.ActiveToken, StringComparison.OrdinalIgnoreCase));
                return index < 0 ? int.MaxValue : index;
            }).ThenBy(m => m.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        private void RefreshAiCommanderAvailability()
        {
            if (tabs == null || aiTab == null || aiNavigationButton == null) return;
            bool found = (currentManagedMods ?? new List<ModInfo>()).Concat(currentWorkshopMods ?? new List<ModInfo>()).Any(IsAiCommanderMod);
            aiNavigationButton.Visible = found;
            if (settingsNavigationButton != null) settingsNavigationButton.Left = found ? 517 : 359;
            bool tabExists = tabs.TabPages.Contains(aiTab);
            if (!found && tabExists)
            {
                if (tabs.SelectedTab == aiTab) tabs.SelectedTab = managedTab;
                tabs.TabPages.Remove(aiTab);
            }
            else if (found && !tabExists)
            {
                int settingsIndex = tabs.TabPages.IndexOf(settingsTab);
                tabs.TabPages.Insert(settingsIndex < 0 ? tabs.TabPages.Count : settingsIndex, aiTab);
            }
            RefreshNavigationButtons();
        }

        private bool IsAiCommanderMod(ModInfo mod)
        {
            if (mod == null) return false;
            string identity = ((mod.DisplayName ?? "") + " " + (mod.Id ?? "") + " " + Path.GetFileName(mod.Folder ?? "")).Replace("_", " ");
            if (identity.IndexOf("AI Commander", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            try
            {
                return !string.IsNullOrWhiteSpace(mod.Folder) && Directory.GetFiles(mod.Folder, "DW2AICommander.dll", SearchOption.AllDirectories).Length > 0;
            }
            catch { return false; }
        }

        private string IncludedDocumentsSummary(ModInfo mod)
        {
            int count = mod == null || mod.IncludedDocuments == null ? 0 : mod.IncludedDocuments.Count;
            if (count == 0) return "—";
            return count == 1 ? T("● 文書あり", "● Document found") : T("● 付属文書: " + count + "件", "● Documents: " + count);
        }

        private string IncludedToolsSummary(ModInfo mod)
        {
            List<string> tools = mod == null || mod.IncludedTools == null ? new List<string>() : mod.IncludedTools;
            if (tools.Count == 0) return "—";
            if (tools.Count > 1) return T("● 付属ツール: " + tools.Count + "件", "● Included tools: " + tools.Count);
            bool installer = tools.Any(x => Path.GetFileName(x).Equals("INSTALL.bat", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(x).StartsWith("INSTALL_", StringComparison.OrdinalIgnoreCase));
            bool updater = tools.Any(x => Path.GetFileName(x).IndexOf("UPDATE", StringComparison.OrdinalIgnoreCase) >= 0);
            bool config = tools.Any(x => Path.GetFileName(x).IndexOf("CONFIG", StringComparison.OrdinalIgnoreCase) >= 0 || Path.GetFileName(x).IndexOf("SETTING", StringComparison.OrdinalIgnoreCase) >= 0);
            if (installer) return T("● インストーラーあり", "● Installer found");
            if (updater) return T("● 更新ツールあり", "● Update tool found");
            if (config) return T("● 設定ツールあり", "● Config tool found");
            bool bat = tools.Any(x => Path.GetExtension(x).Equals(".bat", StringComparison.OrdinalIgnoreCase));
            bool exe = tools.Any(x => Path.GetExtension(x).Equals(".exe", StringComparison.OrdinalIgnoreCase));
            if (bat && exe) return T("● BAT／EXEあり", "● BAT / EXE found");
            return bat ? T("● BATあり", "● BAT found") : T("● EXEあり", "● EXE found");
        }

        private void PopulateList(ListView list, ImageList images, List<ModInfo> mods, bool managed)
        {
            EnsureSettingsState();
            if (list == null || images == null) return;
            if (mods == null) mods = new List<ModInfo>();
            populating = true;
            list.BeginUpdate();
            try
            {
                list.Items.Clear();
                images.Images.Clear();

                int imageNumber = 0;
                foreach (ModInfo mod in mods)
                {
                    if (mod == null) continue;
                    if (mod.ConflictFiles == null) mod.ConflictFiles = new List<string>();
                    if (mod.ConflictMods == null) mod.ConflictMods = new List<string>();
                    string imageKey = "mod_image_" + imageNumber.ToString(CultureInfo.InvariantCulture);
                    imageNumber++;
                    Image thumb = LoadImageNoLock(mod.PreviewImage);
                    if (thumb != null) images.Images.Add(imageKey, thumb);

                    ListViewItem item = new ListViewItem(mod.DisplayName ?? mod.Id ?? "Unknown");
                    item.Tag = mod;
                    if (thumb != null) item.ImageKey = imageKey;
                    item.SubItems.Add(mod.SourceName ?? "");
                    item.SubItems.Add(IncludedToolsSummary(mod));
                    item.SubItems.Add(IncludedDocumentsSummary(mod));
                    item.SubItems.Add("");
                    item.SubItems.Add("");
                    item.SubItems.Add("");
                    item.SubItems.Add(managed ? "—" : T("未確認", "Not checked"));
                    int orderIndex = currentModOrder == null ? -1 : currentModOrder.FindIndex(x => string.Equals(x, mod.ActiveToken, StringComparison.OrdinalIgnoreCase));
                    item.SubItems.Add(orderIndex < 0 ? "—" : (orderIndex + 1).ToString(CultureInfo.InvariantCulture));
                    item.UseItemStyleForSubItems = false;
                    Color rowBack = (list.Items.Count % 2 == 0) ? Dw2Deep : Dw2Panel;
                    item.BackColor = rowBack;
                    foreach (ListViewItem.ListViewSubItem subItem in item.SubItems) subItem.BackColor = rowBack;
                    list.Items.Add(item);
                }
            }
            finally
            {
                list.EndUpdate();
                populating = false;
            }
        }

        private Image LoadImageNoLock(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                using (MemoryStream ms = new MemoryStream(bytes))
                using (Image temp = Image.FromStream(ms))
                    return new Bitmap(temp);
            }
            catch { return null; }
        }

        private void ShowModDetails(ModInfo mod, PictureBox preview, Label name, Label desc)
        {
            if (mod == null || preview == null || name == null || desc == null) return;
            if (preview.Image != null) { Image old = preview.Image; preview.Image = null; old.Dispose(); }
            preview.Image = LoadImageNoLock(mod.PreviewImage);
            name.Text = (mod.DisplayName ?? "") + (string.IsNullOrWhiteSpace(mod.Version) ? "" : "  v" + mod.Version);

            StringBuilder b = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(mod.Description)) b.AppendLine(mod.Description.Trim());
            b.AppendLine();
            b.AppendLine(T("取得元: ", "Source: ") + (mod.SourceName ?? ""));
            b.AppendLine(T("状態: ", "State: ") + (IsModSelected(mod) ? "ON" : "OFF"));
            b.AppendLine(mod.Folder ?? "");
            if (!string.IsNullOrWhiteSpace(mod.ModJsonLaunchArguments)) b.AppendLine(T("自動起動引数: ", "Automatic launch arguments: ") + mod.ModJsonLaunchArguments);
            if (mod.IncludedTools != null && mod.IncludedTools.Count > 0)
            {
                b.AppendLine();
                b.AppendLine(T("付属ツール: ", "Included tools: ") + mod.IncludedTools.Count);
                foreach (string tool in mod.IncludedTools) b.AppendLine("  • " + tool);
            }
            if (mod.IncludedDocuments != null && mod.IncludedDocuments.Count > 0)
            {
                b.AppendLine();
                b.AppendLine(T("付属文書: ", "Included documents: ") + mod.IncludedDocuments.Count);
                foreach (string document in mod.IncludedDocuments) b.AppendLine("  • " + document);
            }
            if (mod.RequiredMods != null && mod.RequiredMods.Count > 0) b.AppendLine("Required: " + string.Join(", ", mod.RequiredMods.ToArray()));
            if (mod.OptionalMods != null && mod.OptionalMods.Count > 0) b.AppendLine("Optional: " + string.Join(", ", mod.OptionalMods.ToArray()));
            if (mod.IncompatibleMods != null && mod.IncompatibleMods.Count > 0) b.AppendLine("Incompatible: " + string.Join(", ", mod.IncompatibleMods.ToArray()));
            if (mod.LoadBefore != null && mod.LoadBefore.Count > 0) b.AppendLine("LoadBefore: " + string.Join(", ", mod.LoadBefore.ToArray()));
            if (mod.LoadAfter != null && mod.LoadAfter.Count > 0) b.AppendLine("LoadAfter: " + string.Join(", ", mod.LoadAfter.ToArray()));

            if (mod.DuplicateCount > 0)
            {
                b.AppendLine();
                b.AppendLine(T("● 重複導入: ", "● Duplicate installations: ") + mod.DuplicateCount + T("か所", " locations"));
                foreach (string location in mod.DuplicateLocations.Take(8)) b.AppendLine("  • " + location);
            }

            if (IsModSelected(mod))
            {
                if (mod.ConflictCount > 0)
                {
                    b.AppendLine();
                    b.AppendLine(T("● 競合あり: ", "● Conflicts: ") + mod.ConflictCount + T("ファイル", " files") +
                        T("（高危険 ", " (high ") + mod.HighRiskConflictCount + T("／低危険 ", " / low ") + mod.LowRiskConflictCount + "）");
                    if (mod.ConflictMods != null && mod.ConflictMods.Count > 0)
                        b.AppendLine(T("競合MOD: ", "Conflicts with: ") + string.Join(", ", mod.ConflictMods.Take(8).ToArray()));
                    if (mod.ConflictFiles != null)
                    {
                        foreach (string file in mod.ConflictFiles.Take(8)) b.AppendLine("  • " + file);
                        if (mod.ConflictFiles.Count > 8) b.AppendLine("  ... +" + (mod.ConflictFiles.Count - 8));
                    }
                }
                else
                {
                    b.AppendLine();
                    b.AppendLine(T("● 競合なし", "● No file conflicts"));
                    if (mod.IdenticalFileCount > 0) b.AppendLine(T("同一パス・同一内容: ", "Same path and identical content: ") + mod.IdenticalFileCount);
                }
            }
            else
            {
                b.AppendLine();
                b.AppendLine(T("MODはOFFです。競合判定の対象外です。", "MOD is OFF and excluded from conflict analysis."));
            }

            if (mod.IsWorkshop)
            {
                b.AppendLine();
                if (mod.UpdateState == "update")
                    b.AppendLine(T("⚠ Steam Workshop: 更新あり", "⚠ Steam Workshop: Update available"));
                else if (mod.UpdateState == "current")
                    b.AppendLine(T("● Steam Workshop: 最新", "● Steam Workshop: Up to date"));
                else
                    b.AppendLine(T("? Steam Workshop: 更新状態未確認", "? Steam Workshop: Update state unknown"));

                if (mod.LocalWorkshopTimeUpdated > 0)
                    b.AppendLine(T("ローカル更新: ", "Local update: ") + UnixTimeText(mod.LocalWorkshopTimeUpdated));
                if (mod.RemoteWorkshopTimeUpdated > 0)
                    b.AppendLine(T("Steam更新: ", "Steam update: ") + UnixTimeText(mod.RemoteWorkshopTimeUpdated));
            }
            desc.Text = b.ToString();
        }

        private bool IsModSelected(ModInfo mod)
        {
            if (mod == null) return false;
            if (modOrderFileFound && !string.IsNullOrWhiteSpace(mod.ActiveToken))
                return currentModOrder.Any(x => x.Equals(mod.ActiveToken, StringComparison.OrdinalIgnoreCase));
            bool selected;
            if (settings.SelectedMods != null && settings.SelectedMods.TryGetValue(mod.Key, out selected)) return selected;
            return false;
        }

        // Red conflicts are based only on the authoritative enabled set.
        // Installed but disabled Workshop/local copies must not participate.
        private bool IsModEnabledForConflict(ModInfo mod)
        {
            if (mod == null || string.IsNullOrWhiteSpace(mod.ActiveToken)) return false;
            if (modOrderFileFound)
                return currentModOrder != null && currentModOrder.Any(x =>
                    string.Equals(x, mod.ActiveToken, StringComparison.OrdinalIgnoreCase));

            bool enabled;
            return settings.SelectedMods != null &&
                   settings.SelectedMods.TryGetValue(mod.Key, out enabled) && enabled;
        }

        private void ShowStateDropDown(ListView list, Point location)
        {
            if (list == null || populating) return;
            ListViewHitTestInfo hit = list.HitTest(location);
            if (hit == null || hit.Item == null || hit.SubItem == null) return;
            int column = hit.Item.SubItems.IndexOf(hit.SubItem);
            if (column != 4) return;

            HideStateDropDown();
            stateEditorList = list;
            stateEditorItem = hit.Item;
            ModInfo mod = stateEditorItem.Tag as ModInfo;
            if (mod == null) { HideStateDropDown(); return; }

            stateEditor = new ComboBox();
            stateEditor.DropDownStyle = ComboBoxStyle.DropDownList;
            stateEditor.Items.Add(T("有効（ON）", "Enabled (ON)"));
            stateEditor.Items.Add(T("無効（OFF）", "Disabled (OFF)"));
            stateEditor.SelectedIndex = IsModSelected(mod) ? 0 : 1;
            Rectangle bounds = hit.SubItem.Bounds;
            stateEditor.Bounds = new Rectangle(bounds.X, bounds.Y, Math.Max(105, bounds.Width), bounds.Height + 2);
            stateEditor.Font = list.Font;
            ComboBox editor = stateEditor;
            ListViewItem editedItem = stateEditorItem;
            stateEditor.SelectionChangeCommitted += delegate
            {
                ModInfo selectedMod = editedItem == null ? null : editedItem.Tag as ModInfo;
                bool enabled = editor.SelectedIndex == 0;
                HideStateDropDown();
                ApplyModEnabledSelection(selectedMod, enabled);
            };
            stateEditor.DropDownClosed += delegate
            {
                if (stateEditor == editor) BeginInvoke(new MethodInvoker(HideStateDropDown));
            };
            list.Controls.Add(stateEditor);
            stateEditor.BringToFront();
            stateEditor.Focus();
            stateEditor.DroppedDown = true;
        }

        private void HideStateDropDown()
        {
            ComboBox old = stateEditor;
            stateEditor = null;
            stateEditorList = null;
            stateEditorItem = null;
            if (old != null)
            {
                try { old.DroppedDown = false; old.Parent.Controls.Remove(old); old.Dispose(); }
                catch { }
            }
        }

        private void ApplyModEnabledSelection(ModInfo mod, bool enabled)
        {
            if (mod == null) return;
            EnsureSettingsState();
            settings.SelectedMods[mod.Key] = enabled;
            SaveSettings();
            ApplyManagedSelectionToIni(mod, enabled);
            SaveModOrderSelection(mod, enabled);
            AnalyzeConflicts();
            RefreshModStatusColumns();
            RefreshSelectedDetails();
            UpdateCommandPreview();
        }

        private void OpenFolder(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
                {
                    MessageBox.Show(T("フォルダーが見つかりません。", "Folder not found."), Text);
                    return;
                }
                Process.Start("explorer.exe", "\"" + path + "\"");
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, Text); }
        }

        private void OpenSelectedModFolder(ListView list)
        {
            if (list == null || list.SelectedItems.Count == 0)
            {
                MessageBox.Show(T("一覧からMODを選択してください。", "Select a MOD from the list."), Text);
                return;
            }
            ModInfo mod = list.SelectedItems[0].Tag as ModInfo;
            if (mod == null || string.IsNullOrWhiteSpace(mod.Folder) || !Directory.Exists(mod.Folder))
            {
                MessageBox.Show(T("選択したMODのフォルダーが見つかりません。", "The selected MOD folder was not found."), Text);
                return;
            }
            OpenFolder(mod.Folder);
        }

        private void BrowseFolderInto(TextBox box)
        {
            using (FolderBrowserDialog d = new FolderBrowserDialog())
            {
                d.SelectedPath = Directory.Exists(box.Text) ? box.Text : appRoot;
                if (d.ShowDialog(this) == DialogResult.OK) box.Text = d.SelectedPath;
            }
        }

        private void OpenSelectedModDetails(ListView list)
        {
            if (list == null || list.SelectedItems.Count == 0) return;
            ModInfo mod = list.SelectedItems[0].Tag as ModInfo;
            if (mod == null) return;
            using (Form detail = new Form())
            {
                detail.Text = T("MOD詳細 - ", "MOD Details - ") + (mod.DisplayName ?? mod.Id);
                detail.StartPosition = FormStartPosition.CenterParent;
                detail.Size = new Size(900, 680);
                detail.MinimumSize = new Size(720, 520);
                detail.BackColor = Dw2Deep;
                detail.ForeColor = Dw2Text;
                detail.Font = Font;

                PictureBox image = new PictureBox();
                image.Location = new Point(18, 18);
                image.Size = new Size(260, 150);
                image.SizeMode = PictureBoxSizeMode.Zoom;
                image.BackColor = Dw2Void;
                image.Image = LoadImageNoLock(mod.PreviewImage);
                detail.Controls.Add(image);

                Label title = new Label();
                title.Location = new Point(300, 20);
                title.Size = new Size(560, 58);
                title.Font = new Font("Segoe UI Semibold", 15F, FontStyle.Bold);
                title.Text = mod.WorkshopTitle ?? mod.DisplayName ?? mod.Id;
                detail.Controls.Add(title);

                Label meta = new Label();
                meta.Location = new Point(302, 84);
                meta.Size = new Size(550, 85);
                meta.Text = T("取得元: ", "Source: ") + (mod.SourceName ?? "") + "\r\n" +
                    T("バージョン: ", "Version: ") + (mod.Version ?? "") + "\r\n" +
                    (mod.IsWorkshop ? "Workshop ID: " + mod.Id + "\r\n" : "") +
                    T("保存場所: ", "Location: ") + (mod.Folder ?? "");
                detail.Controls.Add(meta);

                TextBox information = new TextBox();
                information.Location = new Point(18, 184);
                information.Size = new Size(844, 390);
                information.Multiline = true;
                information.ReadOnly = true;
                information.ScrollBars = ScrollBars.Vertical;
                information.BackColor = Dw2Void;
                information.ForeColor = Dw2Text;
                StringBuilder body = new StringBuilder();
                string description = !string.IsNullOrWhiteSpace(mod.WorkshopDescription) ? mod.WorkshopDescription : mod.Description;
                if (!string.IsNullOrWhiteSpace(description)) body.AppendLine(Regex.Replace(description, "\\[/?[^\\]]+\\]", ""));
                body.AppendLine();
                if (mod.WorkshopFileSize > 0) body.AppendLine(T("ファイルサイズ: ", "File size: ") + mod.WorkshopFileSize + " bytes");
                if (!string.IsNullOrWhiteSpace(mod.WorkshopCreator)) body.AppendLine(T("作者Steam ID: ", "Creator Steam ID: ") + mod.WorkshopCreator);
                if (mod.WorkshopTimeCreated > 0) body.AppendLine(T("作成日時: ", "Created: ") + UnixTimeText(mod.WorkshopTimeCreated));
                if (mod.RemoteWorkshopTimeUpdated > 0) body.AppendLine(T("更新日時: ", "Updated: ") + UnixTimeText(mod.RemoteWorkshopTimeUpdated));
                if (!string.IsNullOrWhiteSpace(mod.WorkshopTags)) body.AppendLine(T("タグ: ", "Tags: ") + mod.WorkshopTags);
                if (mod.ConflictCount > 0)
                {
                    body.AppendLine(); body.AppendLine(T("競合ファイル:", "Conflict files:"));
                    foreach (string f in mod.ConflictFiles) body.AppendLine(" • " + f);
                }
                if (mod.DuplicateCount > 0)
                {
                    body.AppendLine(); body.AppendLine(T("重複インストール場所:", "Duplicate installation locations:"));
                    body.AppendLine(T("現在: ", "Current: ") + mod.SourceName + " | " + mod.Folder);
                    foreach (string d in mod.DuplicateLocations) body.AppendLine(" • " + d);
                }
                information.Text = body.ToString();
                detail.Controls.Add(information);

                Button folder = MakeButton(T("MODフォルダーを開く", "Open MOD Folder"), 18, 590, 180, 34);
                folder.Click += delegate { OpenFolder(mod.Folder); };
                detail.Controls.Add(folder);
                if (mod.IsWorkshop)
                {
                    Button steam = MakeButton(T("Workshopページ", "Workshop Page"), 212, 590, 170, 34);
                    steam.Click += delegate { try { Process.Start("steam://url/CommunityFilePage/" + mod.Id); } catch { } };
                    detail.Controls.Add(steam);
                }
                string ini = FindManagedIni(mod);
                if (ini != null)
                {
                    Button iniButton = MakeButton(T("INI個別設定", "INI Settings"), 396, 590, 150, 34);
                    iniButton.Click += delegate { detail.Close(); OpenIniEditor(mod); };
                    detail.Controls.Add(iniButton);
                }
                Button close = MakeButton(T("閉じる", "Close"), 732, 590, 130, 34);
                close.DialogResult = DialogResult.OK;
                detail.Controls.Add(close);
                detail.ShowDialog(this);
                if (image.Image != null) image.Image.Dispose();
            }
        }

        private void RunSelectedModTool(ListView list)
        {
            if (list == null || list.SelectedItems.Count == 0) return;
            ModInfo mod = list.SelectedItems[0].Tag as ModInfo;
            if (mod == null || mod.IncludedTools == null || mod.IncludedTools.Count == 0) return;
            if (mod.IncludedTools.Count == 1)
            {
                ExecuteModTool(mod, mod.IncludedTools[0]);
                return;
            }
            using (Form picker = new Form())
            {
                picker.Text = T("付属ツールを選択", "Select Included Tool");
                picker.StartPosition = FormStartPosition.CenterParent;
                picker.FormBorderStyle = FormBorderStyle.Sizable;
                picker.MinimumSize = new Size(620, 240);
                picker.Size = new Size(650, Math.Min(560, 150 + mod.IncludedTools.Count * 45));
                picker.BackColor = Dw2Deep;
                picker.ForeColor = Dw2Text;

                Label note = new Label();
                note.Dock = DockStyle.Top;
                note.Height = 55;
                note.Padding = new Padding(14, 12, 14, 4);
                note.ForeColor = Dw2Gold;
                note.Text = T("実行する付属ツールを選択してください。各ボタンを押すと最終確認が表示されます。", "Select a tool to run. A final confirmation appears after clicking each button.");
                picker.Controls.Add(note);

                FlowLayoutPanel buttons = new FlowLayoutPanel();
                buttons.Dock = DockStyle.Fill;
                buttons.FlowDirection = FlowDirection.TopDown;
                buttons.WrapContents = false;
                buttons.AutoScroll = true;
                buttons.Padding = new Padding(12, 8, 12, 8);
                picker.Controls.Add(buttons);
                buttons.BringToFront();
                foreach (string tool in mod.IncludedTools)
                {
                    string toolPath = tool;
                    Button run = MakeButton(toolPath, 0, 0, 585, 36);
                    run.Margin = new Padding(3, 3, 3, 6);
                    run.TextAlign = ContentAlignment.MiddleLeft;
                    run.Click += delegate { ExecuteModTool(mod, toolPath); };
                    buttons.Controls.Add(run);
                }
                picker.ShowDialog(this);
            }
        }

        private void ExecuteModTool(ModInfo mod, string selected)
        {
            string root = !string.IsNullOrWhiteSpace(mod.ContentRoot) ? mod.ContentRoot : mod.Folder;
            if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(selected)) return;
            string fullPath = Path.GetFullPath(Path.Combine(root, selected));
            string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
            {
                MessageBox.Show(T("付属ツールが見つからないか、MODフォルダー外を参照しています。", "The tool is missing or points outside the MOD folder."), Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            string warning = T(
                "外部プログラムを実行します。信頼できるMODに付属するファイルだけ実行してください。\r\n\r\nファイル: ",
                "This will run an external program. Only run files supplied by a MOD you trust.\r\n\r\nFile: ") + fullPath;
            if (MessageBox.Show(warning, T("付属ツールの実行確認", "Confirm Tool Execution"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            try
            {
                ProcessStartInfo start = new ProcessStartInfo();
                start.FileName = fullPath;
                start.WorkingDirectory = Path.GetDirectoryName(fullPath);
                start.UseShellExecute = true;
                Process.Start(start);
            }
            catch (Exception ex)
            {
                Logger.LogException("Run included tool", ex);
                MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OpenSelectedModDocument(ListView list)
        {
            if (list == null || list.SelectedItems.Count == 0) return;
            ModInfo mod = list.SelectedItems[0].Tag as ModInfo;
            if (mod == null || mod.IncludedDocuments == null || mod.IncludedDocuments.Count == 0) return;
            if (mod.IncludedDocuments.Count == 1)
            {
                OpenModDocument(mod, mod.IncludedDocuments[0]);
                return;
            }
            using (Form picker = new Form())
            {
                picker.Text = T("付属文書を選択", "Select Included Document");
                picker.StartPosition = FormStartPosition.CenterParent;
                picker.MinimumSize = new Size(620, 240);
                picker.Size = new Size(650, Math.Min(560, 150 + mod.IncludedDocuments.Count * 45));
                picker.BackColor = Dw2Deep;
                picker.ForeColor = Dw2Text;
                Label note = new Label();
                note.Dock = DockStyle.Top;
                note.Height = 55;
                note.Padding = new Padding(14, 12, 14, 4);
                note.ForeColor = Dw2Gold;
                note.Text = T("開くREADME／マニュアルを選択してください。", "Select a README or manual to open.");
                picker.Controls.Add(note);
                FlowLayoutPanel buttons = new FlowLayoutPanel();
                buttons.Dock = DockStyle.Fill;
                buttons.FlowDirection = FlowDirection.TopDown;
                buttons.WrapContents = false;
                buttons.AutoScroll = true;
                buttons.Padding = new Padding(12, 8, 12, 8);
                picker.Controls.Add(buttons);
                buttons.BringToFront();
                foreach (string document in mod.IncludedDocuments)
                {
                    string documentPath = document;
                    Button open = MakeButton(documentPath, 0, 0, 585, 36);
                    open.Margin = new Padding(3, 3, 3, 6);
                    open.TextAlign = ContentAlignment.MiddleLeft;
                    open.Click += delegate { OpenModDocument(mod, documentPath); };
                    buttons.Controls.Add(open);
                }
                picker.ShowDialog(this);
            }
        }

        private void OpenModDocument(ModInfo mod, string selected)
        {
            try
            {
                string root = !string.IsNullOrWhiteSpace(mod.ContentRoot) ? mod.ContentRoot : mod.Folder;
                if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(selected)) return;
                string fullPath = Path.GetFullPath(Path.Combine(root, selected));
                string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
                {
                    MessageBox.Show(T("付属文書が見つからないか、MODフォルダー外を参照しています。", "The document is missing or points outside the MOD folder."), Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                ProcessStartInfo start = new ProcessStartInfo();
                start.FileName = fullPath;
                start.WorkingDirectory = Path.GetDirectoryName(fullPath);
                start.UseShellExecute = true;
                Process.Start(start);
            }
            catch (Exception ex)
            {
                Logger.LogException("Open included document", ex);
                MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
