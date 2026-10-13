using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using DW2ModLauncher.Core.Models;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>Pre-launch checks over the enabled mods. Messages come back localised through the supplied lookup.</summary>
    public static class LaunchDiagnostics
    {
        public static List<string> Build(IList<ModInfo> enabled, ModOrderState order, string loaderDllPath, IEnumerable<LoaderManifestEntry> manifestEntries, Func<string, string> t)
        {
            List<string> issues = new List<string>();
            issues.AddRange(ModDependencies.Problems(enabled));
            // providers are merged in load order, so the checks of what they supply need the mods in that order
            issues.AddRange(ModChannels.Problems(enabled.OrderBy(m => order.IndexOf(m.ActiveToken) < 0 ? int.MaxValue : order.IndexOf(m.ActiveToken)).ToList()));
            foreach (LoaderManifestEntry entry in manifestEntries ?? new List<LoaderManifestEntry>())
                if (entry.Files != null && entry.Files.Count > 0 && !InjectionScanner.AcceptsFiles(entry.HostDllPath ?? entry.DllPath))
                    issues.Add("⚠ " + string.Format(t("LaunchDiagFilesNotAccepted"), entry.DisplayName));
            foreach (ModInfo mod in enabled)
            {
                if (mod.ScanError != null) issues.Add("⚠ " + string.Format(t("LaunchDiagScanError"), mod.DisplayName ?? mod.Id, mod.ScanError));
                if (mod.ModJsonError != null) issues.Add("⚠ " + string.Format(t("LaunchDiagModJson"), mod.DisplayName ?? mod.Id, mod.ModJsonError));
                if (LauncherRequirement.IsUnmet(mod)) issues.Add("⚠ " + (mod.DisplayName ?? mod.Id) + ": " + t("LauncherTooOld") + mod.MinLauncherVersion + " (" + AppVersion.Display + ")");
                ValidateJsonFile(mod.ModJsonPath, issues, t);
                ValidateJsonFile(Path.Combine(mod.Folder ?? "", "dw2modlauncher.json"), issues, t);
                try
                {
                    foreach (string xml in Directory.GetFiles(mod.ContentRoot ?? mod.Folder, "*.xml", SearchOption.AllDirectories))
                    {
                        try { XmlDocument document = new XmlDocument(); document.Load(xml); }
                        catch (Exception ex) { issues.Add("⚠ " + t("InvalidXML") + xml + " (" + ex.Message + ")"); }
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    issues.Add("⚠ " + string.Format(t("LaunchDiagXmlUnchecked"), mod.DisplayName ?? mod.Id, ex.Message));
                }
            }
            foreach (var font in LoaderManifestBuilder.FontIssues(enabled))
            {
                if (font.Kind == "missing") issues.Add("⚠ " + font.Mod + ": " + t("FontBundleMissing") + font.Font + ".bundle");
                else issues.Add("⚠ " + font.Mod + ": " + t("FontOverridden") + font.Font + " → " + font.Winner);
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
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    issues.Add("⚠ " + string.Format(t("LaunchDiagDllUnchecked"), mod.DisplayName ?? mod.Id, ex.Message));
                }
            }
            foreach (KeyValuePair<string, List<string>> pair in dlls.Where(x => x.Value.Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1))
                issues.Add("⚠ " + t("DuplicateDLL") + pair.Key + " — " + string.Join(" / ", pair.Value.Distinct().ToArray()));
            if (!File.Exists(loaderDllPath)) issues.Add("⚠ " + t("LaunchArgumentDLLNotFound") + loaderDllPath);
            foreach (LoaderManifestEntry entry in manifestEntries)
                if (!File.Exists(entry.HostDllPath)) issues.Add("⚠ " + t("LaunchArgumentDLLNotFound") + entry.HostDllPath);
            return issues.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>The "these files collide, launch anyway?" prompt text.</summary>
        public static string BuildConflictWarning(Dictionary<string, List<ModInfo>> collisions, Func<string, string> t)
        {
            StringBuilder b = new StringBuilder();
            b.AppendLine(t("ConflictWarningIntro"));
            b.AppendLine(t("ConflictWarningBody"));
            b.AppendLine();
            foreach (KeyValuePair<string, List<ModInfo>> kv in collisions.Take(10))
            {
                b.AppendLine("• " + kv.Key);
                b.AppendLine("  " + string.Join("  ↔  ", kv.Value.Select(m => m.DisplayName ?? m.Id).ToArray()));
            }
            if (collisions.Count > 10) b.AppendLine("... +" + (collisions.Count - 10));
            b.AppendLine();
            b.AppendLine(t("LaunchAnywayConfirm"));
            return b.ToString();
        }

        private static void ValidateJsonFile(string path, List<string> issues, Func<string, string> t)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
            try { LooseJson.Parse(File.ReadAllText(path, Encoding.UTF8)); }
            catch (Exception ex) { issues.Add("⚠ " + t("InvalidJSON") + path + " (" + ex.Message + ")"); }
        }
    }
}
