using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using System.Xml;
using DW2ModLauncher.Core.Models;
using DW2ModLauncher.Core.Services;

namespace DW2ModLauncherBeta
{
    public partial class MainForm
    {
        private bool IgnoreConflictFile(string relativePath) { return ConflictRules.IsIgnored(relativePath); }

        private IEnumerable<string> EnumerateConflictPaths(ModInfo mod)
        {
            if (mod == null) yield break;
            if (mod.ConflictPathCache != null)
            {
                foreach (string cached in mod.ConflictPathCache) yield return cached;
                yield break;
            }

            List<string> cache = new List<string>();
            string root = !string.IsNullOrWhiteSpace(mod.ContentRoot) ? mod.ContentRoot : mod.Folder;
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                mod.ConflictPathCache = cache;
                yield break;
            }
            string[] files;
            try { files = Directory.GetFiles(root, "*", SearchOption.AllDirectories); }
            catch
            {
                mod.ConflictPathCache = cache;
                yield break;
            }
            foreach (string file in files)
            {
                string rel;
                try
                {
                    rel = file.Substring(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length)
                              .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                              .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
                }
                catch { continue; }
                if (IgnoreConflictFile(rel)) continue;
                cache.Add(rel.ToLowerInvariant());
            }
            mod.ConflictPathCache = cache;
            foreach (string rel in cache) yield return rel;
        }

        private void AnalyzeConflicts()
        {
            currentCollisions = new Dictionary<string, List<ModInfo>>(StringComparer.OrdinalIgnoreCase);
            if (currentManagedMods == null) currentManagedMods = new List<ModInfo>();
            if (currentWorkshopMods == null) currentWorkshopMods = new List<ModInfo>();
            List<ModInfo> all = currentManagedMods.Concat(currentWorkshopMods).Where(m => m != null).ToList();
            foreach (ModInfo mod in all)
            {
                mod.ConflictCount = 0;
                if (mod.ConflictFiles == null) mod.ConflictFiles = new List<string>(); else mod.ConflictFiles.Clear();
                if (mod.ConflictMods == null) mod.ConflictMods = new List<string>(); else mod.ConflictMods.Clear();
                mod.IdenticalFileCount = 0;
                mod.LowRiskConflictCount = 0;
                mod.HighRiskConflictCount = 0;
            }

            List<ModInfo> enabledMods = all.Where(IsModEnabledForConflict).ToList();
            Dictionary<string, List<ModInfo>> owners = new Dictionary<string, List<ModInfo>>(StringComparer.OrdinalIgnoreCase);
            foreach (ModInfo mod in enabledMods)
            {
                HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string rel in EnumerateConflictPaths(mod))
                {
                    if (!seen.Add(rel)) continue;
                    List<ModInfo> list;
                    if (!owners.TryGetValue(rel, out list))
                    {
                        list = new List<ModInfo>();
                        owners[rel] = list;
                    }
                    list.Add(mod);
                }
            }

            foreach (KeyValuePair<string, List<ModInfo>> kv in owners)
            {
                List<ModInfo> unique = kv.Value
                    .Where(IsModEnabledForConflict)
                    .GroupBy(m => m.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .ToList();
                if (unique.Count < 2) continue;
                List<string> fileHashes = unique.Select(m => ConflictFileHash(m, kv.Key)).ToList();
                List<string> hashes = fileHashes.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (hashes.Count == 1 && fileHashes.All(x => !string.IsNullOrWhiteSpace(x)))
                {
                    foreach (ModInfo mod in unique) mod.IdenticalFileCount++;
                    continue;
                }
                currentCollisions[kv.Key] = unique;
                string extension = Path.GetExtension(kv.Key).ToLowerInvariant();
                bool highRisk = extension == ".dll" || extension == ".exe" || extension == ".xml" || extension == ".json" || extension == ".bin";
                foreach (ModInfo mod in unique)
                {
                    mod.ConflictFiles.Add(kv.Key);
                    if (highRisk) mod.HighRiskConflictCount++; else mod.LowRiskConflictCount++;
                    foreach (ModInfo other in unique)
                    {
                        if (other.Key == mod.Key) continue;
                        string otherName = other.DisplayName ?? other.Id ?? "Unknown";
                        if (!mod.ConflictMods.Contains(otherName)) mod.ConflictMods.Add(otherName);
                    }
                }
            }

            foreach (ModInfo mod in all) mod.ConflictCount = mod.ConflictFiles == null ? 0 : mod.ConflictFiles.Count;
        }

        private string ConflictFileHash(ModInfo mod, string relativePath)
        {
            try
            {
                string root = !string.IsNullOrWhiteSpace(mod.ContentRoot) ? mod.ContentRoot : mod.Folder;
                string file = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar));
                using (SHA256 sha = SHA256.Create())
                using (FileStream stream = File.OpenRead(file)) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
            }
            catch { return ""; }
        }

        private void AnalyzeDuplicates()
        {
            List<ModInfo> all = currentManagedMods.Concat(currentWorkshopMods).Where(m => m != null).ToList();
            foreach (ModInfo mod in all)
            {
                mod.DuplicateCount = 0;
                if (mod.DuplicateLocations == null) mod.DuplicateLocations = new List<string>();
                else mod.DuplicateLocations.Clear();
            }
            Dictionary<string, List<ModInfo>> groups = new Dictionary<string, List<ModInfo>>(StringComparer.OrdinalIgnoreCase);
            foreach (ModInfo mod in all)
            {
                string identity = System.Text.RegularExpressions.Regex.Replace((mod.DisplayName ?? mod.Id ?? Path.GetFileName(mod.Folder) ?? "").Trim().ToLowerInvariant(), "[^a-z0-9ぁ-んァ-ン一-龯]+", "");
                if (identity.Length < 3) continue;
                List<ModInfo> list;
                if (!groups.TryGetValue(identity, out list)) { list = new List<ModInfo>(); groups[identity] = list; }
                list.Add(mod);
            }
            foreach (List<ModInfo> group in groups.Values.Where(g => g.Select(x => x.Folder).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1))
            {
                foreach (ModInfo mod in group)
                {
                    foreach (ModInfo other in group.Where(x => !string.Equals(x.Folder, mod.Folder, StringComparison.OrdinalIgnoreCase)))
                    {
                        string location = (other.SourceName ?? "") + " | " + (other.Version ?? "") + " | " + (other.Folder ?? "");
                        if (!mod.DuplicateLocations.Contains(location)) mod.DuplicateLocations.Add(location);
                    }
                    mod.DuplicateCount = mod.DuplicateLocations.Count;
                }
            }
        }

        private void RefreshModStatusColumns()
        {
            RefreshOneModListStatus(managedList);
            RefreshOneModListStatus(workshopList);
            UpdateOverallStatus();
        }

        private void RefreshOneModListStatus(ListView list)
        {
            if (list == null) return;
            foreach (ListViewItem item in list.Items)
            {
                ModInfo mod = item.Tag as ModInfo;
                if (mod == null || item.SubItems.Count < 9) continue;
                item.UseItemStyleForSubItems = false;

                item.SubItems[2].Text = IncludedToolsSummary(mod);
                item.SubItems[2].ForeColor = mod.IncludedTools != null && mod.IncludedTools.Count > 0 ? Dw2Gold : Dw2Muted;
                item.SubItems[3].Text = IncludedDocumentsSummary(mod);
                item.SubItems[3].ForeColor = mod.IncludedDocuments != null && mod.IncludedDocuments.Count > 0 ? Dw2BlueGlow : Dw2Muted;
                item.SubItems[4].Text = IsModSelected(mod) ? T("有効（ON） ▼", "Enabled (ON) ▼") : T("無効（OFF） ▼", "Disabled (OFF) ▼");
                item.SubItems[4].ForeColor = IsModSelected(mod) ? Dw2Green : Dw2Muted;

                if (!IsModSelected(mod))
                {
                    item.SubItems[5].Text = T("● MOD無効", "● MOD disabled");
                    item.SubItems[5].ForeColor = Dw2Muted;
                }
                else if (mod.ConflictCount > 0)
                {
                    item.SubItems[5].Text = mod.HighRiskConflictCount > 0
                        ? T("● 高危険: " + mod.HighRiskConflictCount, "● High risk: " + mod.HighRiskConflictCount)
                        : T("● 低危険: " + mod.LowRiskConflictCount, "● Low risk: " + mod.LowRiskConflictCount);
                    item.SubItems[5].ForeColor = mod.HighRiskConflictCount > 0 ? Dw2Red : Dw2Gold;
                }
                else if (mod.IdenticalFileCount > 0)
                {
                    item.SubItems[5].Text = T("● 同一内容: " + mod.IdenticalFileCount, "● Identical: " + mod.IdenticalFileCount);
                    item.SubItems[5].ForeColor = Dw2BlueGlow;
                }
                else
                {
                    item.SubItems[5].Text = T("● 競合なし", "● No conflicts");
                    item.SubItems[5].ForeColor = Dw2Green;
                }

                item.SubItems[6].Text = mod.DuplicateCount > 0 ? T("● 重複導入: " + mod.DuplicateCount, "● Duplicates: " + mod.DuplicateCount) : T("● 重複なし", "● No duplicates");
                item.SubItems[6].ForeColor = mod.DuplicateCount > 0 ? Dw2Gold : Dw2Muted;

                if (!mod.IsWorkshop)
                {
                    item.SubItems[7].Text = "—";
                    item.SubItems[7].ForeColor = Dw2Muted;
                }
                else if (mod.UpdateState == "update")
                {
                    item.SubItems[7].Text = T("● 更新あり", "● Update available");
                    item.SubItems[7].ForeColor = Dw2Gold;
                }
                else if (mod.UpdateState == "current")
                {
                    item.SubItems[7].Text = T("● 最新", "● Current");
                    item.SubItems[7].ForeColor = Dw2Green;
                }
                else
                {
                    item.SubItems[7].Text = T("― 未確認", "— Not checked");
                    item.SubItems[7].ForeColor = Dw2Muted;
                }
            }
        }

        private void RefreshSelectedDetails()
        {
            if (managedList != null && managedList.SelectedItems.Count > 0)
                ShowModDetails(managedList.SelectedItems[0].Tag as ModInfo, managedPreview, managedName, managedDesc);
            if (workshopList != null && workshopList.SelectedItems.Count > 0)
                ShowModDetails(workshopList.SelectedItems[0].Tag as ModInfo, workshopPreview, workshopName, workshopDesc);
        }

        private void UpdateOverallStatus()
        {
            if (statusLabel == null) return;
            if (currentManagedMods == null) currentManagedMods = new List<ModInfo>();
            if (currentWorkshopMods == null) currentWorkshopMods = new List<ModInfo>();
            if (currentCollisions == null) currentCollisions = new Dictionary<string, List<ModInfo>>(StringComparer.OrdinalIgnoreCase);
            int updates = currentWorkshopMods.Count(m => m != null && m.UpdateState == "update");
            int selected = currentManagedMods.Concat(currentWorkshopMods).Where(m => m != null).Count(IsModSelected);
            string conflictText = currentCollisions.Count == 0 ? T("競合なし", "No conflicts") : T("競合ファイル ", "Conflict files ") + currentCollisions.Count;
            string updateText = updates == 0 ? T("更新なし/未確認", "No updates/unchecked") : T("更新あり ", "Updates ") + updates;
            if (modOrderReadFailed) updateText = T("mods.json 読込失敗", "mods.json ERROR");
            int duplicates = currentManagedMods.Concat(currentWorkshopMods).Count(m => m != null && m.DuplicateCount > 0);
            string duplicateText = duplicates == 0 ? T("重複導入なし", "No duplicate installations") : T("重複導入 ", "Duplicate installations ") + duplicates;
            statusLabel.Text = string.Format(T("DW2 MOD: {0}件｜Workshop: {1}件｜有効: {2}件｜{3}｜{4}｜{5}", "DW2 MODs: {0} | Workshop: {1} | Enabled: {2} | {3} | {4} | {5}"), currentManagedMods.Count, currentWorkshopMods.Count, selected, conflictText, duplicateText, updateText);
        }

        private string BuildConflictWarning()
        {
            StringBuilder b = new StringBuilder();
            b.AppendLine(T("選択中のMODに同じ相対パスのファイルが見つかりました。", "Selected mods contain files with the same relative path."));
            b.AppendLine(T("DW2では上書き競合になる可能性があります。自動マージは行いません。", "These may overwrite each other in DW2. No automatic merge will be performed."));
            b.AppendLine();
            foreach (KeyValuePair<string, List<ModInfo>> kv in currentCollisions.Take(10))
            {
                b.AppendLine("• " + kv.Key);
                b.AppendLine("  " + string.Join("  ↔  ", kv.Value.Select(m => m.DisplayName ?? m.Id).ToArray()));
            }
            if (currentCollisions.Count > 10) b.AppendLine("... +" + (currentCollisions.Count - 10));
            b.AppendLine();
            b.AppendLine(T("この構成のまま起動しますか？（自己責任で続行）", "Launch with this configuration anyway? (Proceed at your own peril)"));
            return b.ToString();
        }

        private List<string> BuildLaunchDiagnostics()
        {
            List<string> issues = new List<string>();
            List<ModInfo> enabled = (currentManagedMods ?? new List<ModInfo>()).Concat(currentWorkshopMods ?? new List<ModInfo>()).Where(IsModSelected).ToList();
            Func<ModInfo, string, bool> matches = delegate(ModInfo m, string identity)
            {
                if (m == null || string.IsNullOrWhiteSpace(identity)) return false;
                string folder = Path.GetFileName(m.Folder ?? "");
                return new string[] { m.Id, m.DisplayName, m.ActiveToken, folder }.Any(x => string.Equals(x, identity, StringComparison.OrdinalIgnoreCase));
            };
            foreach (ModInfo mod in enabled)
            {
                foreach (string required in mod.RequiredMods ?? new List<string>())
                    if (!enabled.Any(m => matches(m, required))) issues.Add("⚠ " + (mod.DisplayName ?? mod.Id) + ": " + T("必須MODがありません: ", "Missing required MOD: ") + required);
                foreach (string incompatible in mod.IncompatibleMods ?? new List<string>())
                    if (enabled.Any(m => m != mod && matches(m, incompatible))) issues.Add("⚠ " + (mod.DisplayName ?? mod.Id) + ": " + T("同時使用不可: ", "Incompatible MOD enabled: ") + incompatible);
                int ownIndex = currentModOrder == null ? -1 : currentModOrder.FindIndex(x => string.Equals(x, mod.ActiveToken, StringComparison.OrdinalIgnoreCase));
                foreach (string before in mod.LoadBefore ?? new List<string>())
                {
                    ModInfo target = enabled.FirstOrDefault(m => matches(m, before));
                    int targetIndex = target == null || currentModOrder == null ? -1 : currentModOrder.FindIndex(x => string.Equals(x, target.ActiveToken, StringComparison.OrdinalIgnoreCase));
                    if (target != null && ownIndex >= 0 && targetIndex >= 0 && ownIndex > targetIndex)
                        issues.Add("⚠ " + (mod.DisplayName ?? mod.Id) + T(" は次のMODより先にロードする必要があります: ", " must load before: ") + before);
                }
                foreach (string after in mod.LoadAfter ?? new List<string>())
                {
                    ModInfo target = enabled.FirstOrDefault(m => matches(m, after));
                    int targetIndex = target == null || currentModOrder == null ? -1 : currentModOrder.FindIndex(x => string.Equals(x, target.ActiveToken, StringComparison.OrdinalIgnoreCase));
                    if (target != null && ownIndex >= 0 && targetIndex >= 0 && ownIndex < targetIndex)
                        issues.Add("⚠ " + (mod.DisplayName ?? mod.Id) + T(" は次のMODより後にロードする必要があります: ", " must load after: ") + after);
                }
                ValidateJsonFile(mod.ModJsonPath, issues);
                ValidateJsonFile(Path.Combine(mod.Folder ?? "", "launcher.json"), issues);
                try
                {
                    foreach (string xml in Directory.GetFiles(mod.ContentRoot ?? mod.Folder, "*.xml", SearchOption.AllDirectories))
                    {
                        try { XmlDocument document = new XmlDocument(); document.Load(xml); }
                        catch (Exception ex) { issues.Add("⚠ " + T("XML破損: ", "Invalid XML: ") + xml + " (" + ex.Message + ")"); }
                    }
                }
                catch { }
            }
            Dictionary<string, List<string>> dlls = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (ModInfo mod in enabled)
            {
                try
                {
                    foreach (string dll in Directory.GetFiles(mod.ContentRoot ?? mod.Folder, "*.dll", SearchOption.AllDirectories))
                    {
                        List<string> owners;
                        if (!dlls.TryGetValue(Path.GetFileName(dll), out owners)) { owners = new List<string>(); dlls[Path.GetFileName(dll)] = owners; }
                        owners.Add(mod.DisplayName ?? mod.Id);
                    }
                }
                catch { }
            }
            foreach (KeyValuePair<string, List<string>> pair in dlls.Where(x => x.Value.Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1))
                issues.Add("⚠ " + T("重複DLL: ", "Duplicate DLL: ") + pair.Key + " — " + string.Join(" / ", pair.Value.Distinct().ToArray()));
            foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(BuildLaunchArguments(), "--low-level-inject\\s+(?:\\\"([^\\\"]+)\\\"|([^\\s!]+))!", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                string dll = !string.IsNullOrWhiteSpace(match.Groups[1].Value) ? match.Groups[1].Value : match.Groups[2].Value;
                string full = Path.IsPathRooted(dll) ? dll : Path.Combine(settings.GameRoot ?? "", dll.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(full)) issues.Add("⚠ " + T("起動引数のDLLがありません: ", "Launch argument DLL not found: ") + dll);
            }
            return issues.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private void ValidateJsonFile(string path, List<string> issues)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
            try { LooseJson.Parse(File.ReadAllText(path, Encoding.UTF8)); }
            catch (Exception ex) { issues.Add("⚠ " + T("JSON破損: ", "Invalid JSON: ") + path + " (" + ex.Message + ")"); }
        }
    }
}
