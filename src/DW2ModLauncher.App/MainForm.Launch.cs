using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DW2ModLauncher.Core.Models;

namespace DW2ModLauncherBeta
{
    public partial class MainForm
    {
        private string BuildLaunchArguments()
        {
            EnsureSettingsState();
            List<string> args = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<ModInfo> launchMods = (currentManagedMods ?? new List<ModInfo>())
                .Concat(currentWorkshopMods ?? new List<ModInfo>()).Where(IsModSelected).ToList();
            foreach (ModInfo mod in launchMods.OrderBy(m =>
            {
                int index = currentModOrder == null ? -1 : currentModOrder.FindIndex(x => x.Equals(m.ActiveToken, StringComparison.OrdinalIgnoreCase));
                return index < 0 ? int.MaxValue : index;
            }))
            {
                if (!string.IsNullOrWhiteSpace(mod.ModJsonLaunchArguments) && seen.Add(mod.ModJsonLaunchArguments.Trim()))
                    args.Add(mod.ModJsonLaunchArguments.Trim());
                LauncherMeta meta = ReadLauncherMeta(mod);
                if (meta != null && !string.IsNullOrWhiteSpace(meta.launchArguments) && seen.Add(meta.launchArguments.Trim()))
                    args.Add(meta.launchArguments.Trim());
            }
            string global = launchArgsBox == null ? settings.GlobalLaunchArguments : launchArgsBox.Text.Trim();
            if (!string.IsNullOrWhiteSpace(global) && seen.Add(global)) args.Add(global);
            return string.Join(" ", args.Where(a => !string.IsNullOrWhiteSpace(a)).ToArray()).Trim();
        }

        private void UpdateCommandPreview()
        {
            if (commandPreviewBox == null) return;
            string exe = string.IsNullOrEmpty(settings.GameRoot) ? "DistantWorlds2.exe" : Path.Combine(settings.GameRoot, "DistantWorlds2.exe");
            commandPreviewBox.Text = "\"" + exe + "\"" + (string.IsNullOrWhiteSpace(BuildLaunchArguments()) ? "" : " " + BuildLaunchArguments());
        }

        private void LaunchGame()
        {
            SaveSettingsFromUi();
            if (FindAiIni() != null) SaveAiSettings();
            AnalyzeConflicts();
            RefreshModStatusColumns();
            List<string> diagnostics = BuildLaunchDiagnostics();
            if (diagnostics.Count > 0)
            {
                DialogResult diagnosticAnswer = MessageBox.Show(
                    T("起動前診断で問題が見つかりました。\r\n\r\n", "Pre-launch diagnostics found issues.\r\n\r\n") +
                    string.Join("\r\n", diagnostics.Take(30).ToArray()) +
                    T("\r\n\r\nこのまま起動しますか？", "\r\n\r\nLaunch anyway?"),
                    T("起動前診断", "Pre-launch diagnostics"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (diagnosticAnswer != DialogResult.Yes) return;
            }
            if (currentCollisions.Count > 0)
            {
                string warning = BuildConflictWarning();
                DialogResult answer = MessageBox.Show(warning, T("MOD競合の警告", "MOD Conflict Warning"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (answer != DialogResult.Yes) return;
            }
            string exe = Path.Combine(settings.GameRoot ?? "", "DistantWorlds2.exe");
            if (!File.Exists(exe))
            {
                MessageBox.Show(T("DistantWorlds2.exe が見つかりません。設定タブでゲームフォルダーを指定してください。", "DistantWorlds2.exe was not found. Set the game folder in Settings."), Text);
                return;
            }
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = exe;
                psi.WorkingDirectory = settings.GameRoot;
                psi.Arguments = BuildLaunchArguments();
                psi.UseShellExecute = true;
                Process.Start(psi);
                SetStatus(T("Distant Worlds 2 を起動しました。", "Distant Worlds 2 launched."));
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, Text);
            }
        }
    }
}
