using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;
using DW2ModLauncher.Core.Models;
using DW2ModLauncher.Core.Services;

namespace DW2ModLauncher.App
{
    public partial class MainForm
    {
        private List<ModInfo> OrderedEnabledMods()
        {
            return GameLauncher.OrderedEnabled((currentManagedMods ?? new List<ModInfo>()).Concat(currentWorkshopMods ?? new List<ModInfo>()), modOrder, settings);
        }

        // The launcher ships a single fixed loader DLL (see DW2ModLauncher.Loader) next to its
        // own executable; that loader is the ONLY --low-level-inject target ever used. It reads
        // manifest.json (written by GameLauncher.WriteLoaderManifest) and loads every enabled mod itself,
        // in order, via reflection - see docs/DLL Injection.md. This replaced composing every
        // mod's own dll!entryPoint into one CLI flag directly, since the game only honors the
        // last --low-level-inject occurrence and invokes entry points with zero arguments.
        private string LoaderDllPath() { return GameLauncher.LoaderDllPath(); }

        private void WriteLoaderManifest() { GameLauncher.WriteLoaderManifest(OrderedEnabledMods()); }

        private string BuildLaunchArguments()
        {
            EnsureSettingsState();
            return GameLauncher.BuildArguments(launchArgsBox == null ? settings.GlobalLaunchArguments : launchArgsBox.Text.Trim());
        }

        private void UpdateCommandPreview()
        {
            if (commandPreviewBox == null) return;
            string exe = string.IsNullOrEmpty(settings.GameRoot) ? "DistantWorlds2.exe" : Path.Combine(settings.GameRoot, "DistantWorlds2.exe");
            commandPreviewBox.Text = "\"" + exe + "\"" + (string.IsNullOrWhiteSpace(BuildLaunchArguments()) ? "" : " " + BuildLaunchArguments());
        }

        private void ImportSteamLaunchOptions()
        {
            SteamLaunchOptions options = SteamLaunchOptions.ReadForApp(SteamLocator.AppId);
            if (options == null)
            {
                MessageBox.Show(T("SteamLaunchOptionsNone"), Text);
                return;
            }
            EnsureSettingsState();
            if (launchEnvBox != null) launchEnvBox.Text = GameLauncher.FormatEnvironment(options.Environment);
            if (launchArgsBox != null) launchArgsBox.Text = options.Arguments;
            string env = options.Environment.Count == 0 ? "-" : string.Join(" ", options.Environment.Keys);
            string note = string.Format(T("SteamLaunchOptionsImported"), string.IsNullOrEmpty(options.Arguments) ? "-" : options.Arguments, env);
            if (!string.IsNullOrEmpty(options.Wrapper)) note += " | " + string.Format(T("SteamLaunchOptionsWrapperIgnored"), options.Wrapper);
            SetStatus(note);
            UpdateCommandPreview();
        }

        private void LaunchGame()
        {
            SaveSettingsFromUi();
            AnalyzeConflicts();
            RefreshModStatusColumns();
            List<string> diagnostics = BuildLaunchDiagnostics();
            if (diagnostics.Count > 0)
            {
                DialogResult diagnosticAnswer = MessageBox.Show(
                    T("DiagnosticsFoundIssues") +
                    string.Join("\r\n", diagnostics.Take(30).ToArray()) +
                    T("LaunchAnyway"),
                    T("PreLaunchDiagnostics"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (diagnosticAnswer != DialogResult.Yes) return;
            }
            if (currentCollisions.Count > 0)
            {
                string warning = BuildConflictWarning();
                DialogResult answer = MessageBox.Show(warning, T("ModConflictWarning"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (answer != DialogResult.Yes) return;
            }
            string exe = Path.Combine(settings.GameRoot ?? "", "DistantWorlds2.exe");
            if (!File.Exists(exe))
            {
                MessageBox.Show(T("GameExeNotFound"), Text);
                return;
            }
            try
            {
                WriteLoaderManifest();
                Process.Start(GameLauncher.BuildStartInfo(settings.GameRoot, BuildLaunchArguments(), settings.LaunchEnvironment));
                SetStatus(T("DistantWorlds2Launched"));
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, Text);
            }
        }
    }
}
