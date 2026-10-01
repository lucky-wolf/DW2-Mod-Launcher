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
        private List<ModInfo> ScanMods(string root, bool workshop) { return ModScanner.ScanMods(root, workshop, key => T(key)); }

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
            if (currentCollisions == null) currentCollisions = new Dictionary<string, List<ModInfo>>(StringComparer.OrdinalIgnoreCase);

            List<ModInfo> combinedMods = OrderModsForDisplay(currentManagedMods.Concat(currentWorkshopMods).ToList());
            SafeStage("Populate MOD list", delegate { PopulateList(modList, modImages, combinedMods); });
            SafeStage("Conflict analysis", delegate { AnalyzeConflicts(); });
            SafeStage("Duplicate analysis", delegate { AnalyzeDuplicates(); });
            SafeStage("Refresh status columns", delegate { RefreshModStatusColumns(); });
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


        private void PopulateList(ListView list, ImageList images, List<ModInfo> mods)
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
                    item.SubItems.Add(""); // MOD State - filled in by RefreshModStatusColumns
                    item.SubItems.Add(""); // Health - filled in by RefreshModStatusColumns
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

        private void ShowModDetails(ModInfo mod, PictureBox preview, Label name, Panel problemsPanel, Label problemsLabel, Control desc)
        {
            if (mod == null || preview == null || name == null || desc == null) return;
            if (preview.Image != null) { Image old = preview.Image; preview.Image = null; old.Dispose(); }
            preview.Image = LoadImageNoLock(mod.PreviewImage);
            name.Text = (mod.DisplayName ?? "") + (string.IsNullOrWhiteSpace(mod.Version) ? "" : "  v" + mod.Version);

            UpdateProblemsPanel(problemsPanel, problemsLabel, mod);

            StringBuilder b = new StringBuilder();
            string description = !string.IsNullOrWhiteSpace(mod.WorkshopDescription) ? mod.WorkshopDescription : mod.Description;
            if (!string.IsNullOrWhiteSpace(description)) b.AppendLine(Regex.Replace(description.Trim(), "\\[/?[^\\]]+\\]", ""));
            b.AppendLine();
            b.AppendLine(Labeled("Source", mod.SourceName ?? ""));
            b.AppendLine(T("State") + (IsModSelected(mod) ? "ON" : "OFF"));
            b.AppendLine(mod.Folder ?? "");
            if (mod.IncludedTools != null && mod.IncludedTools.Count > 0)
            {
                b.AppendLine();
                b.AppendLine(T("IncludedToolsLabel") + mod.IncludedTools.Count);
                foreach (string tool in mod.IncludedTools) b.AppendLine("  • " + tool);
            }
            if (mod.IncludedDocuments != null && mod.IncludedDocuments.Count > 0)
            {
                b.AppendLine();
                b.AppendLine(T("IncludedDocuments") + mod.IncludedDocuments.Count);
                foreach (string document in mod.IncludedDocuments) b.AppendLine("  • " + document);
            }
            if (mod.RequiredMods != null && mod.RequiredMods.Count > 0) b.AppendLine("Required: " + string.Join(", ", mod.RequiredMods.ToArray()));
            if (mod.OptionalMods != null && mod.OptionalMods.Count > 0) b.AppendLine("Optional: " + string.Join(", ", mod.OptionalMods.ToArray()));
            if (mod.IncompatibleMods != null && mod.IncompatibleMods.Count > 0) b.AppendLine("Incompatible: " + string.Join(", ", mod.IncompatibleMods.ToArray()));
            if (mod.LoadBefore != null && mod.LoadBefore.Count > 0) b.AppendLine("LoadBefore: " + string.Join(", ", mod.LoadBefore.ToArray()));
            if (mod.LoadAfter != null && mod.LoadAfter.Count > 0) b.AppendLine("LoadAfter: " + string.Join(", ", mod.LoadAfter.ToArray()));

            if (!IsModSelected(mod))
            {
                b.AppendLine();
                b.AppendLine(T("ModDisabledNote"));
            }
            else if (mod.ConflictCount == 0 && mod.IdenticalFileCount == 0)
            {
                b.AppendLine();
                b.AppendLine(T("NoFileConflicts"));
            }

            if (IsModSelected(mod) && mod.ConflictFiles != null && mod.ConflictFiles.Count > 0)
            {
                b.AppendLine();
                b.AppendLine(T("ConflictFilesHeader"));
                foreach (string file in mod.ConflictFiles.Take(8)) b.AppendLine("  • " + file);
                if (mod.ConflictFiles.Count > 8) b.AppendLine("  ... +" + (mod.ConflictFiles.Count - 8));
            }

            if (mod.DuplicateCount > 0)
            {
                b.AppendLine();
                b.AppendLine(T("DuplicateLocationsHeader"));
                foreach (string location in mod.DuplicateLocations.Take(8)) b.AppendLine("  • " + location);
            }

            if (mod.IsWorkshop)
            {
                b.AppendLine();
                if (mod.UpdateState == "update")
                    b.AppendLine(T("SteamWorkshopUpdateAvailable"));
                else if (mod.UpdateState == "current")
                    b.AppendLine(T("SteamWorkshopUpToDate"));
                else
                    b.AppendLine(T("WorkshopStateUnknown"));

                if (mod.LocalWorkshopTimeUpdated > 0)
                    b.AppendLine(T("LocalUpdate") + UnixTimeText(mod.LocalWorkshopTimeUpdated));
                if (mod.RemoteWorkshopTimeUpdated > 0)
                    b.AppendLine(T("SteamUpdate") + UnixTimeText(mod.RemoteWorkshopTimeUpdated));

                b.AppendLine();
                if (mod.WorkshopFileSize > 0) b.AppendLine(T("FileSize") + mod.WorkshopFileSize + " bytes");
                if (!string.IsNullOrWhiteSpace(mod.WorkshopCreator)) b.AppendLine(T("CreatorSteamID") + mod.WorkshopCreator);
                if (mod.WorkshopTimeCreated > 0) b.AppendLine(T("Created") + UnixTimeText(mod.WorkshopTimeCreated));
                if (!string.IsNullOrWhiteSpace(mod.WorkshopTags)) b.AppendLine(T("Tags") + mod.WorkshopTags);
            }
            desc.Text = b.ToString();
        }

        // A short, colored callout for whatever is actually wrong with the mod
        // (conflicts, duplicates, a pending update) - collapsed entirely when
        // there is nothing to flag, so a clean mod shows no box at all.
        private void UpdateProblemsPanel(Panel panel, Label label, ModInfo mod)
        {
            if (panel == null || label == null) return;
            List<string> lines = new List<string>();
            if (HealthSeverity(mod) >= 2)
            {
                if (mod.HighRiskConflictCount > 0 || mod.LowRiskConflictCount > 0)
                {
                    lines.Add(T("Conflicts") + mod.ConflictCount + T("Files") +
                        T("High") + mod.HighRiskConflictCount + T("Low") + mod.LowRiskConflictCount + "）");
                    if (mod.ConflictMods != null && mod.ConflictMods.Count > 0)
                        lines.Add(T("ConflictsWith") + string.Join(", ", mod.ConflictMods.Take(8).ToArray()));
                }
                if (mod.IdenticalFileCount > 0) lines.Add(T("SamePathAndIdenticalContent") + mod.IdenticalFileCount);
                if (mod.DuplicateCount > 0) lines.Add(T("DuplicateInstallationsDetail") + mod.DuplicateCount + T("Locations"));
                if (mod.IsWorkshop && mod.UpdateState == "update") lines.Add(T("SteamWorkshopUpdateAvailable"));
            }

            if (lines.Count == 0)
            {
                panel.Visible = false;
                panel.Height = 0;
                return;
            }

            bool conflict = HealthSeverity(mod) == 3;
            panel.BackColor = conflict ? Dw2Red : Dw2Gold;
            label.ForeColor = conflict ? Color.White : Color.Black;
            label.Text = (conflict ? T("HealthConflict") : T("HealthCaution")) + "\r\n" + string.Join("\r\n", lines.ToArray());

            int width = panel.ClientSize.Width > 0 ? panel.ClientSize.Width
                : (panel.Parent != null && panel.Parent.ClientSize.Width > 0 ? panel.Parent.ClientSize.Width : 300);
            Size measured = TextRenderer.MeasureText(label.Text, label.Font,
                new Size(Math.Max(50, width - panel.Padding.Horizontal), int.MaxValue), TextFormatFlags.WordBreak);
            panel.Height = measured.Height + panel.Padding.Vertical;
            panel.Visible = true;
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

        private void ToggleModStateAtLocation(ListView list, Point location)
        {
            if (list == null || populating) return;
            ListViewHitTestInfo hit = list.HitTest(location);
            if (hit == null || hit.Item == null || hit.SubItem == null) return;
            int column = hit.Item.SubItems.IndexOf(hit.SubItem);
            if (column != ColumnModState) return;
            ModInfo mod = hit.Item.Tag as ModInfo;
            if (mod == null) return;
            ApplyModEnabledSelection(mod, !IsModSelected(mod));
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
                    MessageBox.Show(T("FolderNotFound"), Text);
                    return;
                }
                // explorer.exe's own argument parsing silently falls back to its default folder
                // (observed: opens Documents) on a path with a mix of '/' and '\' separators -
                // e.g. GameRoot-derived paths like "c:/program files (x86)/steam\steamapps\..."
                // (settings.GameRoot itself can be stored that way; ManagedModsRoot inherits it).
                // .NET's own Directory.Exists/Process.Start tolerate the mix fine, so this went
                // unnoticed until explorer.exe itself had to parse it. Path.GetFullPath
                // canonicalizes to all-backslash on Windows, which explorer.exe parses correctly.
                Process.Start("explorer.exe", "\"" + Path.GetFullPath(path) + "\"");
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, Text); }
        }

        private void OpenSelectedModFolder(ListView list)
        {
            if (list == null || list.SelectedItems.Count == 0)
            {
                MessageBox.Show(T("SelectAMODFromTheList"), Text);
                return;
            }
            ModInfo mod = list.SelectedItems[0].Tag as ModInfo;
            if (mod == null || string.IsNullOrWhiteSpace(mod.Folder) || !Directory.Exists(mod.Folder))
            {
                MessageBox.Show(T("ModFolderNotFound"), Text);
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
                picker.Text = T("SelectIncludedDocument");
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
                note.Text = T("SelectAREADMEOrManualToOpen");
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
                    MessageBox.Show(T("DocumentMissingWarning"), Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
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
