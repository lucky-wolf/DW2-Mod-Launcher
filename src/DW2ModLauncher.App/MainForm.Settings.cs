using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;
using DW2ModLauncher.Core.Diagnostics;
using DW2ModLauncher.Core.Models;
using DW2ModLauncher.Core.Services;

namespace DW2ModLauncherBeta
{
    public partial class MainForm
    {
        private LauncherSettings LoadSettings()
        {
            try
            {
                if (File.Exists(settingsPath))
                {
                    LauncherSettings s = JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(settingsPath, Encoding.UTF8));
                    if (s != null)
                    {
                        if (s.SelectedMods == null) s.SelectedMods = new Dictionary<string, bool>();
                        return s;
                    }
                }
            }
            catch { }
            return new LauncherSettings();
        }

        private void SaveSettings()
        {
            try
            {
                File.WriteAllText(settingsPath, JsonSerializer.Serialize(settings), new UTF8Encoding(true));
            }
            catch (Exception ex)
            {
                SetStatus(T("設定保存エラー: ", "Settings save error: ") + ex.Message);
            }
        }

        private void SaveSettingsFromUi()
        {
            EnsureSettingsState();
            string game = gameRootBox == null ? settings.GameRoot : (gameRootBox.Text ?? "").Trim();
            string workshop = workshopRootBox == null ? settings.WorkshopRoot : (workshopRootBox.Text ?? "").Trim();
            string managed = managedRootBox == null ? settings.ManagedModsRoot : (managedRootBox.Text ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(game) && !IsGameRoot(game))
            {
                MessageBox.Show(T("DistantWorlds2.exeがあるゲームフォルダーを指定してください。", "Select the game folder containing DistantWorlds2.exe."), Text);
                return;
            }
            if (!string.IsNullOrWhiteSpace(workshop) && !Directory.Exists(workshop))
            {
                MessageBox.Show(T("Workshopフォルダーが見つかりません。", "The Workshop folder does not exist."), Text);
                return;
            }
            if (!string.IsNullOrWhiteSpace(managed) && !Directory.Exists(managed))
            {
                MessageBox.Show(T("DW2 MODフォルダーが見つかりません。", "The DW2 MOD folder does not exist."), Text);
                return;
            }
            settings.GameRoot = game;
            settings.WorkshopRoot = workshop;
            settings.ManagedModsRoot = managed;
            if (launchArgsBox != null) settings.GlobalLaunchArguments = (launchArgsBox.Text ?? "").Trim();
            SaveSettings();
            UpdatePathLabels();
            UpdateCommandPreview();
        }

        private string ProfilesRoot() { return Path.Combine(appRoot, "Profiles"); }
        private string SafeFileName(string value)
        {
            string result = value ?? "Profile";
            foreach (char c in Path.GetInvalidFileNameChars()) result = result.Replace(c, '_');
            return string.IsNullOrWhiteSpace(result) ? "Profile" : result.Trim();
        }

        private void RefreshProfileCombo()
        {
            if (profileCombo == null) return;
            string selected = settings == null ? "" : settings.ActiveProfile ?? "";
            profileCombo.Items.Clear();
            try
            {
                Directory.CreateDirectory(ProfilesRoot());
                foreach (string path in Directory.GetFiles(ProfilesRoot(), "*.json", SearchOption.TopDirectoryOnly))
                    profileCombo.Items.Add(Path.GetFileNameWithoutExtension(path));
            }
            catch { }
            if (!string.IsNullOrWhiteSpace(selected)) profileCombo.Text = selected;
        }

        private void SaveCurrentProfile()
        {
            string name = profileCombo == null ? "" : (profileCombo.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show(T("プロファイル名を入力してください。", "Enter a profile name."), Text);
                return;
            }
            ModProfile profile = new ModProfile();
            profile.Name = name;
            profile.Order = new List<string>(currentModOrder ?? new List<string>());
            profile.ManualLaunchArguments = launchArgsBox == null ? settings.GlobalLaunchArguments : launchArgsBox.Text.Trim();
            foreach (ModInfo mod in (currentManagedMods ?? new List<ModInfo>()).Concat(currentWorkshopMods ?? new List<ModInfo>()))
            {
                profile.Versions[mod.ActiveToken ?? mod.Key] = mod.Version ?? "";
                try
                {
                    foreach (string ini in Directory.GetFiles(mod.Folder, "*.ini", SearchOption.AllDirectories))
                    {
                        string rel = ini.Substring(mod.Folder.TrimEnd(Path.DirectorySeparatorChar).Length).TrimStart(Path.DirectorySeparatorChar);
                        profile.IniFiles[(mod.ActiveToken ?? mod.Key) + "|" + rel] = Convert.ToBase64String(File.ReadAllBytes(ini));
                    }
                }
                catch { }
            }
            try
            {
                Directory.CreateDirectory(ProfilesRoot());
                File.WriteAllText(Path.Combine(ProfilesRoot(), SafeFileName(name) + ".json"), JsonSerializer.Serialize(profile), new UTF8Encoding(false));
                settings.ActiveProfile = name;
                settings.GlobalLaunchArguments = profile.ManualLaunchArguments ?? "";
                SaveSettings();
                RefreshProfileCombo();
                SetStatus(T("MODプロファイルを保存しました: ", "MOD profile saved: ") + name);
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, Text); }
        }

        private void ApplySelectedProfile()
        {
            string name = profileCombo == null ? "" : (profileCombo.Text ?? "").Trim();
            string path = Path.Combine(ProfilesRoot(), SafeFileName(name) + ".json");
            if (!File.Exists(path)) { MessageBox.Show(T("プロファイルが見つかりません。", "Profile not found."), Text); return; }
            if (IsGameRunning()) { MessageBox.Show(T("DW2を終了してから切り替えてください。", "Close DW2 before switching profiles."), Text); return; }
            try
            {
                ModProfile profile = JsonSerializer.Deserialize<ModProfile>(File.ReadAllText(path, Encoding.UTF8));
                if (profile == null) return;
                List<ModInfo> all = (currentManagedMods ?? new List<ModInfo>()).Concat(currentWorkshopMods ?? new List<ModInfo>()).ToList();
                List<string> versionChanges = new List<string>();
                foreach (KeyValuePair<string, string> savedVersion in profile.Versions ?? new Dictionary<string, string>())
                {
                    ModInfo installed = all.FirstOrDefault(m => string.Equals(m.ActiveToken, savedVersion.Key, StringComparison.OrdinalIgnoreCase) || string.Equals(m.Key, savedVersion.Key, StringComparison.OrdinalIgnoreCase));
                    if (installed == null) versionChanges.Add(T("未導入: ", "Not installed: ") + savedVersion.Key);
                    else if (!string.Equals(installed.Version ?? "", savedVersion.Value ?? "", StringComparison.OrdinalIgnoreCase))
                        versionChanges.Add((installed.DisplayName ?? installed.Id) + ": " + savedVersion.Value + " → " + (installed.Version ?? "?"));
                }
                if (versionChanges.Count > 0 && MessageBox.Show(
                    T("保存時からMODバージョンが変わっています。\r\n", "MOD versions differ from the saved profile.\r\n") + string.Join("\r\n", versionChanges.Take(20).ToArray()) +
                    T("\r\n\r\n構成を適用しますか？", "\r\n\r\nApply the profile?"), T("バージョン差異", "Version differences"),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                WriteModOrder(profile.Order ?? new List<string>());
                settings.ActiveProfile = profile.Name ?? name;
                settings.GlobalLaunchArguments = profile.ManualLaunchArguments ?? "";
                if (launchArgsBox != null) launchArgsBox.Text = settings.GlobalLaunchArguments;
                foreach (KeyValuePair<string, string> kv in profile.IniFiles ?? new Dictionary<string, string>())
                {
                    int split = kv.Key.IndexOf('|');
                    if (split <= 0) continue;
                    string token = kv.Key.Substring(0, split);
                    string rel = kv.Key.Substring(split + 1);
                    ModInfo mod = all.FirstOrDefault(m => string.Equals(m.ActiveToken, token, StringComparison.OrdinalIgnoreCase) || string.Equals(m.Key, token, StringComparison.OrdinalIgnoreCase));
                    if (mod == null) continue;
                    string ini = Path.Combine(mod.Folder, rel);
                    if (File.Exists(ini)) File.Copy(ini, ini + ".launcher_backup", true);
                    Directory.CreateDirectory(Path.GetDirectoryName(ini));
                    File.WriteAllBytes(ini, Convert.FromBase64String(kv.Value));
                }
                SaveSettings();
                RefreshAll();
                SetStatus(T("MODプロファイルへ切り替えました: ", "Switched MOD profile: ") + settings.ActiveProfile);
            }
            catch (Exception ex) { Logger.LogException("Apply profile", ex); MessageBox.Show(ex.Message, Text); }
        }

        private void DeleteSelectedProfile()
        {
            string name = profileCombo == null ? "" : (profileCombo.Text ?? "").Trim();
            string path = Path.Combine(ProfilesRoot(), SafeFileName(name) + ".json");
            try { if (File.Exists(path)) File.Delete(path); if (settings.ActiveProfile == name) settings.ActiveProfile = ""; SaveSettings(); RefreshProfileCombo(); }
            catch (Exception ex) { MessageBox.Show(ex.Message, Text); }
        }

        private void CreateEnvironmentSnapshot()
        {
            string root = Path.Combine(appRoot, "Snapshots", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            try
            {
                Directory.CreateDirectory(root);
                string modsJson = ModsJsonPath();
                if (!string.IsNullOrWhiteSpace(modsJson) && File.Exists(modsJson)) File.Copy(modsJson, Path.Combine(root, "mods.json"), true);
                if (File.Exists(settingsPath)) File.Copy(settingsPath, Path.Combine(root, "launcher_settings.json"), true);
                List<ModInfo> enabled = (currentManagedMods ?? new List<ModInfo>()).Concat(currentWorkshopMods ?? new List<ModInfo>()).Where(IsModSelected).ToList();
                Dictionary<string, string> manifest = new Dictionary<string, string>();
                foreach (ModInfo mod in enabled)
                {
                    string destination = Path.Combine(root, "MODs", SafeFileName(mod.ActiveToken));
                    CopyDirectory(mod.Folder, destination);
                    manifest[mod.ActiveToken] = mod.Folder;
                }
                File.WriteAllText(Path.Combine(root, "snapshot_manifest.json"), JsonSerializer.Serialize(manifest), new UTF8Encoding(false));
                MessageBox.Show(T("スナップショットを保存しました。\r\n", "Snapshot saved.\r\n") + root, Text);
            }
            catch (Exception ex) { Logger.LogException("Create snapshot", ex); MessageBox.Show(ex.Message, Text); }
        }

        private void RestoreLatestSnapshot()
        {
            string snapshots = Path.Combine(appRoot, "Snapshots");
            string root = Directory.Exists(snapshots) ? Directory.GetDirectories(snapshots).OrderByDescending(x => x).FirstOrDefault() : null;
            if (string.IsNullOrWhiteSpace(root)) { MessageBox.Show(T("スナップショットがありません。", "No snapshot is available."), Text); return; }
            if (IsGameRunning()) { MessageBox.Show(T("DW2を終了してから復元してください。", "Close DW2 before restoring."), Text); return; }
            if (MessageBox.Show(T("最新のスナップショットへ戻しますか？\r\n", "Restore the latest snapshot?\r\n") + root,
                T("スナップショット復元", "Restore snapshot"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            try
            {
                string manifestPath = Path.Combine(root, "snapshot_manifest.json");
                Dictionary<string, string> manifest = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(manifestPath, Encoding.UTF8));
                foreach (KeyValuePair<string, string> kv in manifest)
                {
                    string source = Path.Combine(root, "MODs", SafeFileName(kv.Key));
                    if (Directory.Exists(source) && Directory.Exists(Path.GetDirectoryName(kv.Value))) CopyDirectory(source, kv.Value);
                }
                string savedOrder = Path.Combine(root, "mods.json");
                if (File.Exists(savedOrder) && !string.IsNullOrWhiteSpace(ModsJsonPath())) File.Copy(savedOrder, ModsJsonPath(), true);
                string savedSettings = Path.Combine(root, "launcher_settings.json");
                if (File.Exists(savedSettings)) File.Copy(savedSettings, settingsPath, true);
                settings = LoadSettings();
                EnsureSettingsState();
                RefreshAll();
                MessageBox.Show(T("最新のスナップショットを復元しました。", "Latest snapshot restored."), Text);
            }
            catch (Exception ex) { Logger.LogException("Restore snapshot", ex); MessageBox.Show(ex.Message, Text); }
        }

        private void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (string file in Directory.GetFiles(source, "*", SearchOption.TopDirectoryOnly)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
            foreach (string dir in Directory.GetDirectories(source, "*", SearchOption.TopDirectoryOnly)) CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
        }

        private void DetectPaths(bool overwrite)
        {
            string game = FindGameRoot();
            if (!string.IsNullOrEmpty(game) && (overwrite || string.IsNullOrEmpty(settings.GameRoot) || !Directory.Exists(settings.GameRoot)))
                settings.GameRoot = game;

            string workshop = FindWorkshopRoot(settings.GameRoot);
            if (!string.IsNullOrEmpty(workshop) && (overwrite || string.IsNullOrEmpty(settings.WorkshopRoot) || !Directory.Exists(settings.WorkshopRoot)))
                settings.WorkshopRoot = workshop;

            if (IsGameRoot(settings.GameRoot))
            {
                string dw2Mods = Path.Combine(settings.GameRoot, "mods");
                if (overwrite || string.IsNullOrEmpty(settings.ManagedModsRoot) || !Directory.Exists(settings.ManagedModsRoot))
                    settings.ManagedModsRoot = dw2Mods;
            }

            SaveSettings();
            UpdatePathLabels();
            if (gameRootBox != null) gameRootBox.Text = settings.GameRoot;
            if (workshopRootBox != null) workshopRootBox.Text = settings.WorkshopRoot;
            if (managedRootBox != null) managedRootBox.Text = settings.ManagedModsRoot;
            if (launchArgsBox != null) launchArgsBox.Text = settings.GlobalLaunchArguments ?? "";
        }

        private void UpdatePathLabels()
        {
            if (gamePathLabel != null) gamePathLabel.Text = "Game: " + (string.IsNullOrEmpty(settings.GameRoot) ? "Not found" : settings.GameRoot);
            if (workshopPathLabel != null) workshopPathLabel.Text = "Workshop: " + (string.IsNullOrEmpty(settings.WorkshopRoot) ? "Not found" : settings.WorkshopRoot);
        }

        private bool IsGameRoot(string p) { return SteamLocator.IsGameRoot(p); }
        private string FindGameRoot() { return SteamLocator.FindGameRoot(settings.GameRoot); }
        private string FindWorkshopRoot(string gameRoot) { return SteamLocator.FindWorkshopRoot(gameRoot, settings.WorkshopRoot); }
    }
}
