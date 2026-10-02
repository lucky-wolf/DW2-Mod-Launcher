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

namespace DW2ModLauncherBeta
{
    public partial class MainForm
    {
        private List<ModInfo> OrderedEnabledMods()
        {
            List<ModInfo> launchMods = (currentManagedMods ?? new List<ModInfo>())
                .Concat(currentWorkshopMods ?? new List<ModInfo>()).Where(IsModSelected).ToList();
            return launchMods.OrderBy(m =>
            {
                int index = currentModOrder == null ? -1 : currentModOrder.FindIndex(x => x.Equals(m.ActiveToken, StringComparison.OrdinalIgnoreCase));
                return index < 0 ? int.MaxValue : index;
            }).ToList();
        }

        // The launcher ships a single fixed loader DLL (see DW2ModLauncher.Loader) next to its
        // own executable; that loader is the ONLY --low-level-inject target ever used. It reads
        // manifest.json (written by WriteLoaderManifest below) and loads every enabled mod itself,
        // in order, via reflection - see docs/DLL Injection.md. This replaced composing every
        // mod's own dll!entryPoint into one CLI flag directly, since the game only honors the
        // last --low-level-inject occurrence and invokes entry points with zero arguments.
        private string LoaderDllPath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Loader", "DW2ModLauncher.Loader.dll");
        }

        private void WriteLoaderManifest()
        {
            LoaderManifest manifest = LoaderManifestBuilder.Build(OrderedEnabledMods());
            string loaderDir = Path.GetDirectoryName(LoaderDllPath());
            Directory.CreateDirectory(loaderDir);
            File.WriteAllText(Path.Combine(loaderDir, "manifest.json"), JsonSerializer.Serialize(manifest), new UTF8Encoding(false));
        }

        private string BuildLaunchArguments()
        {
            EnsureSettingsState();
            List<string> args = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string loaderDll = LoaderDllPath();
            string token = (loaderDll.IndexOf(' ') >= 0 ? "\"" + loaderDll + "\"" : loaderDll) + "!DW2ModLauncher.Loader.Entry.Init";
            args.Add("--low-level-inject " + token);
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
                DialogResult answer = MessageBox.Show(warning, T("MODConflictWarning"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
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
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = exe;
                psi.WorkingDirectory = settings.GameRoot;
                psi.Arguments = BuildLaunchArguments();
                psi.UseShellExecute = true;
                Process.Start(psi);
                SetStatus(T("DistantWorlds2Launched"));
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, Text);
            }
        }
    }
}
