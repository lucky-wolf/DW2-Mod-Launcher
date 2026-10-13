using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DW2ModLauncher.Core.Models;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>The text shown in a mod's details panel. Localised through the supplied lookup. No UI.</summary>
    public static class ModDetails
    {
        /// <summary>
        /// What the mod declares in dw2modlauncher.json about other mods, as "label: value" lines with who is on the other end among the enabled mods (in load order):
        /// the channels it receives and who supplies them, the channels it provides and who receives them, and its dependencies.
        /// Empty when it declares nothing.
        /// </summary>
        public static string BuildLinks(ModInfo mod, IList<ModInfo> orderedEnabled)
        {
            List<string> lines = new List<string>();
            Func<IEnumerable<ModInfo>, string> names = mods => string.Join(", ", mods.Select(m => m.DisplayName ?? m.Id));
            foreach (string channel in (mod.Receives ?? new List<string>()).Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                List<ModInfo> providers = orderedEnabled.Where(m => m != mod && (m.Provides ?? new List<LauncherProvision>())
                    .Any(p => p != null && string.Equals(p.channel?.Trim(), channel, StringComparison.OrdinalIgnoreCase))).ToList();
                lines.Add("Receives: " + channel);
                lines.Add("Providers: " + (providers.Count == 0 ? "none enabled" : names(providers)));
            }
            foreach (LauncherProvision provision in mod.Provides ?? new List<LauncherProvision>())
            {
                if (provision == null || string.IsNullOrWhiteSpace(provision.channel)) continue;
                string channel = provision.channel.Trim();
                List<ModInfo> receivers = orderedEnabled.Where(m => m != mod && (m.Receives ?? new List<string>())
                    .Any(c => string.Equals(c?.Trim(), channel, StringComparison.OrdinalIgnoreCase))).ToList();
                lines.Add("Provides: " + channel);
                lines.Add("Receivers: " + (receivers.Count == 0 ? "none enabled" : names(receivers)));
            }
            foreach (LauncherDependency dep in mod.Dependencies ?? new List<LauncherDependency>())
            {
                if (dep == null || string.IsNullOrWhiteSpace(dep.mod)) continue;
                List<string> parts = new List<string>();
                parts.Add(dep.required ? "required" : "optional");
                if (!string.IsNullOrWhiteSpace(dep.minVersion)) parts.Add("min " + dep.minVersion.Trim());
                if (dep.loadAfter) parts.Add("loads after");
                if (dep.loadBefore) parts.Add("loads before");
                lines.Add("Depends on: " + dep.mod.Trim() + " (" + string.Join(", ", parts) + ")");
                lines.Add("Status: " + (ModDependencies.Find(orderedEnabled, dep.mod) == null ? "not enabled" : "enabled"));
            }
            return string.Join("\n", lines);
        }

        public static string BuildText(ModInfo mod, bool isSelected, Func<string, string> t)
        {
            StringBuilder b = new StringBuilder();
            // The long descriptionFile overrides the short mod.json description; Steam's text only fills in when neither is local.
            string description = !string.IsNullOrWhiteSpace(mod.DescriptionOverride) ? mod.DescriptionOverride
                : !string.IsNullOrWhiteSpace(mod.WorkshopDescription) ? mod.WorkshopDescription : mod.Description;
            // Source and enabled state are not repeated here: the list already shows the state, and SourceText sits above this text.
            if (!string.IsNullOrWhiteSpace(description))
            {
                b.AppendLine(Regex.Replace(description.Trim(), "\\[/?[^\\]]+\\]", ""));
                b.AppendLine();
            }
            if (mod.IncludedTools != null && mod.IncludedTools.Count > 0)
            {
                b.AppendLine();
                b.AppendLine(t("IncludedToolsLabel") + mod.IncludedTools.Count);
                foreach (string tool in mod.IncludedTools) b.AppendLine("  • " + tool);
            }
            if (mod.IncludedDocuments != null && mod.IncludedDocuments.Count > 0)
            {
                b.AppendLine();
                b.AppendLine(t("IncludedDocuments") + mod.IncludedDocuments.Count);
                foreach (string document in mod.IncludedDocuments) b.AppendLine("  • " + document);
            }

            if (isSelected && mod.ConflictFiles != null && mod.ConflictFiles.Count > 0)
            {
                b.AppendLine();
                b.AppendLine(t("ConflictFilesHeader"));
                foreach (string file in mod.ConflictFiles.Take(8)) b.AppendLine("  • " + file);
                if (mod.ConflictFiles.Count > 8) b.AppendLine("  ... +" + (mod.ConflictFiles.Count - 8));
            }

            if (mod.IsWorkshop)
            {
                b.AppendLine();
                // "Update available" is in the problems callout and "up to date" is the healthy default, so only the unknown case is worth a line.
                if (mod.UpdateState != "update" && mod.UpdateState != "current")
                    b.AppendLine(t("WorkshopStateUnknown"));

                if (mod.LocalWorkshopTimeUpdated > 0)
                    b.AppendLine(t("LocalUpdate") + WorkshopUpdateService.UnixTimeText(mod.LocalWorkshopTimeUpdated));
                if (mod.RemoteWorkshopTimeUpdated > 0)
                    b.AppendLine(t("SteamUpdate") + WorkshopUpdateService.UnixTimeText(mod.RemoteWorkshopTimeUpdated));

                b.AppendLine();
                if (mod.WorkshopFileSize > 0) b.AppendLine(t("FileSize") + mod.WorkshopFileSize + " bytes");
                if (!string.IsNullOrWhiteSpace(mod.WorkshopCreator)) b.AppendLine(t("CreatorSteamID") + mod.WorkshopCreator);
                if (mod.WorkshopTimeCreated > 0) b.AppendLine(t("Created") + WorkshopUpdateService.UnixTimeText(mod.WorkshopTimeCreated));
                if (!string.IsNullOrWhiteSpace(mod.WorkshopTags)) b.AppendLine(t("Tags") + mod.WorkshopTags);
            }
            return b.ToString();
        }

        /// <summary>The short status line for a published local mod ("Status: up to date"), or "" when the mod has no publish state.</summary>
        public static string PublishStatusShort(ModInfo mod, Func<string, string> t)
        {
            switch (mod.PublishStatus)
            {
                case PublishStatus.Unpublished: return t("PublishStateUnpublished");
                case PublishStatus.Unknown: return t("PublishStateUnknown");
                case PublishStatus.Current: return string.Format(t("PublishStateCurrent"), PublishedWhen(mod));
                case PublishStatus.Changed: return t("PublishStateChanged");
                default: return "";
            }
        }

        /// <summary>When the last publish happened: the date, plus the time if that was today. Empty when unknown.</summary>
        public static string PublishedWhen(ModInfo mod)
        {
            if (!mod.PublishedUtc.HasValue) return "";
            DateTime local = mod.PublishedUtc.Value.ToLocalTime();
            return local.ToString(local.Date == DateTime.Today ? "g" : "d", System.Globalization.CultureInfo.CurrentCulture);
        }

        /// <summary>What the status means in a sentence ("3 files changed since the last publish on ..."), or "" when there is no publish state.</summary>
        public static string PublishStatusSentence(ModInfo mod, Func<string, string> t)
        {
            string when = PublishedWhen(mod);
            switch (mod.PublishStatus)
            {
                case PublishStatus.Unpublished: return t("PublishStateUnpublishedDetail");
                case PublishStatus.Unknown: return t("PublishStateUnknownDetail");
                case PublishStatus.Current: return string.Format(t("PublishStateCurrentDetail"), when);
                case PublishStatus.Changed: return string.Format(t("PublishStateChangedDetail"), mod.UnpublishedChanges.Count, when);
                default: return "";
            }
        }

        /// <summary>The sentence plus (when files changed) up to <paramref name="maxFiles"/> of them, one per line; for a tooltip.</summary>
        public static string PublishStatusTooltip(ModInfo mod, Func<string, string> t, int maxFiles = 12)
        {
            string text = PublishStatusSentence(mod, t);
            if (mod.PublishStatus != PublishStatus.Changed) return text;
            text += "\n" + string.Join("\n", mod.UnpublishedChanges.Take(maxFiles).Select(c => "  " + c));
            if (mod.UnpublishedChanges.Count > maxFiles) text += "\n  ... +" + (mod.UnpublishedChanges.Count - maxFiles);
            return text;
        }

        /// <summary>
        /// Where the mod comes from: "MyMod" for a local mod (the path under the managed mods folder, so nested folders show, and
        /// nothing about where that folder is), "Steam Workshop" for a Workshop item. The source name when the folder is unknown.
        /// </summary>
        public static string SourceText(ModInfo mod, string managedModsRoot)
        {
            if (mod.IsWorkshop || string.IsNullOrWhiteSpace(mod.Folder)) return mod.SourceName ?? "";
            string folder = Path.GetFileName(mod.Folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (!string.IsNullOrWhiteSpace(managedModsRoot) && ModFileImporter.IsInside(managedModsRoot, mod.Folder))
                folder = Path.GetRelativePath(managedModsRoot, mod.Folder);
            return folder.Length == 0 ? mod.SourceName ?? "" : folder;
        }

        /// <summary>The Workshop item id: the folder name of a Workshop copy, or the id a publish wrote into mod.json. Null for an unpublished local mod.</summary>
        public static string WorkshopId(ModInfo mod)
        {
            string id = mod.IsWorkshop ? mod.Id : mod.WorkshopId;
            return string.IsNullOrWhiteSpace(id) ? null : id.Trim();
        }

        /// <summary>
        /// The lines of the coloured callout shown under the title: whatever is actually wrong with the mod.
        /// Empty when there is nothing to flag, so a clean mod shows no box at all.
        /// </summary>
        public static List<string> BuildProblems(ModInfo mod, int severity, Func<string, string> t)
        {
            List<string> lines = new List<string>();
            if (severity < 2) return lines;
            if (mod.HighRiskConflictCount > 0 || mod.LowRiskConflictCount > 0)
            {
                lines.Add(t("Conflicts") + mod.ConflictCount + t("Files") +
                    t("High") + mod.HighRiskConflictCount + t("Low") + mod.LowRiskConflictCount + "）");
                if (mod.ConflictMods != null && mod.ConflictMods.Count > 0)
                    lines.Add(t("ConflictsWith") + string.Join(", ", mod.ConflictMods.Take(8).ToArray()));
            }
            if (mod.EnabledCopyCount > 0) lines.Add(t("EnabledMoreThanOnce"));
            if (mod.IdenticalFileCount > 0) lines.Add(t("SamePathAndIdenticalContent") + mod.IdenticalFileCount);
            if (mod.IsWorkshop && mod.UpdateState == "update") lines.Add(t("SteamWorkshopUpdateAvailable"));
            if (LauncherRequirement.IsUnmet(mod)) lines.Add(t("LauncherTooOld") + mod.MinLauncherVersion + " (" + AppVersion.Display + ")");
            if (mod.ModJsonError != null) lines.Add("mod.json is not valid JSON: " + mod.ModJsonError);
            return lines;
        }
    }

    public static class ModDocuments
    {
        /// <summary>
        /// The full path of one of the mod's included documents, or null if it doesn't exist or would resolve
        /// outside the mod's own folder (e.g. "../../something").
        /// </summary>
        public static string ResolveSafe(ModInfo mod, string selected)
        {
            string root = !string.IsNullOrWhiteSpace(mod.ContentRoot) ? mod.ContentRoot : mod.Folder;
            if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(selected)) return null;
            string fullPath = Path.GetFullPath(Path.Combine(root, selected));
            string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return fullPath.StartsWith(fullRoot, comparison) && File.Exists(fullPath) ? fullPath : null;
        }
    }
}
