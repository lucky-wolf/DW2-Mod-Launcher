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
            Func<ModInfo, string, bool> matches = delegate (ModInfo m, string identity)
            {
                if (m == null || string.IsNullOrWhiteSpace(identity)) return false;
                string folder = Path.GetFileName(m.Folder ?? "");
                return new string[] { m.Id, m.DisplayName, m.ActiveToken, folder }.Any(x => string.Equals(x, identity, StringComparison.OrdinalIgnoreCase));
            };
            foreach (ModInfo mod in enabled)
            {
                foreach (string required in mod.RequiredMods ?? new List<string>())
                    if (!enabled.Any(m => matches(m, required))) issues.Add("⚠ " + (mod.DisplayName ?? mod.Id) + ": " + t("MissingRequiredMod") + required);
                foreach (string incompatible in mod.IncompatibleMods ?? new List<string>())
                    if (enabled.Any(m => m != mod && matches(m, incompatible))) issues.Add("⚠ " + (mod.DisplayName ?? mod.Id) + ": " + t("IncompatibleModEnabled") + incompatible);
                int ownIndex = order.IndexOf(mod.ActiveToken);
                foreach (string before in mod.LoadBefore ?? new List<string>())
                {
                    ModInfo target = enabled.FirstOrDefault(m => matches(m, before));
                    int targetIndex = target == null ? -1 : order.IndexOf(target.ActiveToken);
                    if (target != null && ownIndex >= 0 && targetIndex >= 0 && ownIndex > targetIndex)
                        issues.Add("⚠ " + (mod.DisplayName ?? mod.Id) + t("MustLoadBefore") + before);
                }
                foreach (string after in mod.LoadAfter ?? new List<string>())
                {
                    ModInfo target = enabled.FirstOrDefault(m => matches(m, after));
                    int targetIndex = target == null ? -1 : order.IndexOf(target.ActiveToken);
                    if (target != null && ownIndex >= 0 && targetIndex >= 0 && ownIndex < targetIndex)
                        issues.Add("⚠ " + (mod.DisplayName ?? mod.Id) + t("MustLoadAfter") + after);
                }
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
