using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DW2ModLauncher.Core.Models;
using DW2ModLauncher.Core.Services.Publishing;

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
        public static string BuildLinks(ModInfo mod, IList<ModInfo> orderedEnabled, Func<string, string> t)
        {
            return string.Join("\n", BuildLinkLines(mod, orderedEnabled, t).Select(l => l.Text));
        }

        /// <summary>The lines of <see cref="BuildLinks"/>, each with whether it is met (shown with a green check mark) or unmet and required (shown in red).</summary>
        public static List<DetailLine> BuildLinkLines(ModInfo mod, IList<ModInfo> orderedEnabled, Func<string, string> t)
        {
            List<DetailLine> lines = new List<DetailLine>();
            Func<IEnumerable<ModInfo>, string> names = mods => string.Join(t("ListSeparator"), mods.Select(m => m.DisplayName ?? m.Id));
            if (!string.IsNullOrWhiteSpace(mod.MinLauncherVersion))
            {
                bool tooOld = LauncherRequirement.IsUnmet(mod);
                lines.Add(new DetailLine(NamedText.Fill(t(tooOld ? "LinkLauncherTooOld" : "LinkLauncherNeeds"), ("Version", mod.MinLauncherVersion.Trim()), ("Current", AppVersion.Display)), tooOld ? LineState.Unmet : LineState.Met));
            }
            foreach (string channel in (mod.Receives ?? new List<string>()).Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                List<ModInfo> providers = orderedEnabled.Where(m => m != mod && (m.Provides ?? new List<LauncherProvision>())
                    .Any(p => p != null && string.Equals(p.channel?.Trim(), channel, StringComparison.OrdinalIgnoreCase))).ToList();
                LineState supplied = providers.Count == 0 ? LineState.None : LineState.Met;
                lines.Add(new DetailLine(NamedText.Fill(t("LinkReceives"), ("Channel", channel)), supplied));
                lines.Add(new DetailLine(providers.Count == 0 ? t("LinkProvidersNone") : NamedText.Fill(t("LinkProviders"), ("Mods", names(providers))), supplied));
            }
            foreach (LauncherProvision provision in mod.Provides ?? new List<LauncherProvision>())
            {
                if (provision == null || string.IsNullOrWhiteSpace(provision.channel)) continue;
                string channel = provision.channel.Trim();
                List<ModInfo> receivers = orderedEnabled.Where(m => m != mod && (m.Receives ?? new List<string>())
                    .Any(c => string.Equals(c?.Trim(), channel, StringComparison.OrdinalIgnoreCase))).ToList();
                LineState taken = receivers.Count == 0 ? LineState.None : LineState.Met;
                lines.Add(new DetailLine(NamedText.Fill(t("LinkProvides"), ("Channel", channel)), taken));
                lines.Add(new DetailLine(receivers.Count == 0 ? t("LinkReceiversNone") : NamedText.Fill(t("LinkReceivers"), ("Mods", names(receivers))), taken));
            }
            foreach (LauncherDependency dep in mod.Dependencies ?? new List<LauncherDependency>())
            {
                if (dep == null || string.IsNullOrWhiteSpace(dep.mod)) continue;
                if (ModDependencies.Find(orderedEnabled, dep.mod) != null) lines.Add(new DetailLine(NamedText.Fill(t("LinkDependsOn"), ("Mod", dep.mod.Trim())), LineState.Met));
                else lines.Add(new DetailLine(NamedText.Fill(t("LinkDependsOnNotEnabled"), ("Mod", dep.mod.Trim())), dep.required ? LineState.Unmet : LineState.None));
            }
            return lines;
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
                b.AppendLine(NamedText.Fill(t("IncludedToolsLabel"), ("Count", mod.IncludedTools.Count)));
                foreach (string tool in mod.IncludedTools) b.AppendLine("  • " + tool);
            }
            if (mod.IncludedDocuments != null && mod.IncludedDocuments.Count > 0)
            {
                b.AppendLine();
                b.AppendLine(NamedText.Fill(t("IncludedDocuments"), ("Count", mod.IncludedDocuments.Count)));
                foreach (string document in mod.IncludedDocuments) b.AppendLine("  • " + document);
            }

            if (isSelected && mod.ConflictFiles != null && mod.ConflictFiles.Count > 0)
            {
                b.AppendLine();
                b.AppendLine(t("ConflictFilesHeader"));
                foreach (string file in mod.ConflictFiles.Take(8)) b.AppendLine("  • " + file);
                if (mod.ConflictFiles.Count > 8) b.AppendLine("  " + NamedText.Fill(t("MoreItems"), ("Count", mod.ConflictFiles.Count - 8)));
            }

            if (mod.IsWorkshop)
            {
                b.AppendLine();
                // "Update available" is in the problems callout and "up to date" is the healthy default, so only the unknown case is worth a line.
                if (mod.UpdateState != "update" && mod.UpdateState != "current")
                    b.AppendLine(t("WorkshopStateUnknown"));

                if (mod.LocalWorkshopTimeUpdated > 0)
                    b.AppendLine(NamedText.Fill(t("LocalUpdate"), ("Time", WorkshopUpdateService.UnixTimeText(mod.LocalWorkshopTimeUpdated))));
                if (mod.RemoteWorkshopTimeUpdated > 0)
                    b.AppendLine(NamedText.Fill(t("SteamUpdate"), ("Time", WorkshopUpdateService.UnixTimeText(mod.RemoteWorkshopTimeUpdated))));

                b.AppendLine();
                if (mod.WorkshopFileSize > 0) b.AppendLine(NamedText.Fill(t("FileSize"), ("Bytes", mod.WorkshopFileSize)));
                if (!string.IsNullOrWhiteSpace(mod.WorkshopCreator)) b.AppendLine(NamedText.Fill(t("CreatorSteamID"), ("Id", mod.WorkshopCreator)));
                if (mod.WorkshopTimeCreated > 0) b.AppendLine(NamedText.Fill(t("Created"), ("Time", WorkshopUpdateService.UnixTimeText(mod.WorkshopTimeCreated))));
                if (!string.IsNullOrWhiteSpace(mod.WorkshopTags)) b.AppendLine(NamedText.Fill(t("Tags"), ("Tags", mod.WorkshopTags)));
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
                case PublishStatus.Current: return NamedText.Fill(t("PublishStateCurrent"), ("When", PublishedWhen(mod)));
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
                case PublishStatus.Current: return NamedText.Fill(t("PublishStateCurrentDetail"), ("When", when));
                case PublishStatus.Changed: return NamedText.Fill(t("PublishStateChangedDetail"), ("Count", mod.UnpublishedChanges.Count), ("When", when));
                default: return "";
            }
        }

        /// <summary>
        /// For a modified mod: first (when the mod's changelog.txt has unreleased lines) a "Change log:" heading with those lines indented under it,
        /// then the sentence ("N files changed since ...") heading up to <paramref name="maxFiles"/> of the changed files, one per line; for a tooltip.
        /// <paramref name="includeChangeLog"/> false leaves the change log out (the publish dialog shows it in its own section).
        /// </summary>
        public static string PublishStatusTooltip(ModInfo mod, Func<string, string> t, int maxFiles = 12, bool includeChangeLog = true)
        {
            string text = PublishStatusSentence(mod, t);
            if (mod.PublishStatus != PublishStatus.Changed) return text;
            string notes = includeChangeLog ? ModChangelog.UnreleasedText(mod.ContentRoot ?? mod.Folder) : "";
            if (notes.Length > 0)
                text = NamedText.Fill(t("PublishStateChangeLog"), ("Notes", string.Join("\n", notes.Split('\n').Select(l => "  " + l)))) + "\n\n" + text;
            text += "\n" + string.Join("\n", mod.UnpublishedChanges.Take(maxFiles).Select(c => "  " + c));
            if (mod.UnpublishedChanges.Count > maxFiles) text += "\n  " + NamedText.Fill(t("MoreItems"), ("Count", mod.UnpublishedChanges.Count - maxFiles));
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
        /// <summary>
        /// The problems of <see cref="BuildProblems"/> split by level, so the details can show a conflict callout and a caution callout apart:
        /// conflicts are what makes the mod's health a conflict (high-risk file clashes, hook conflicts, enabled twice), the rest are cautions.
        /// Both empty when <paramref name="severity"/> is below caution.
        /// </summary>
        public static (List<string> Conflicts, List<string> Cautions) BuildProblemGroups(ModInfo mod, int severity, Func<string, string> t)
        {
            List<string> conflicts = new List<string>(), cautions = new List<string>();
            if (severity < 2) return (conflicts, cautions);
            if (mod.HighRiskConflictCount > 0 || mod.LowRiskConflictCount > 0)
            {
                List<string> files = mod.HighRiskConflictCount > 0 ? conflicts : cautions;
                files.Add(ConflictsSummary(mod, t));
                if (mod.ConflictMods != null && mod.ConflictMods.Count > 0)
                    files.Add(ConflictsWith(mod, t));
            }
            foreach (string note in mod.HookNotes ?? new List<string>()) (mod.HookConflictNotes != null && mod.HookConflictNotes.Contains(note) ? conflicts : cautions).Add(note);
            if (mod.EnabledCopyCount > 0) conflicts.Add(t("EnabledMoreThanOnce"));
            if (mod.IdenticalFileCount > 0) cautions.Add(NamedText.Fill(t("SamePathAndIdenticalContent"), ("Count", mod.IdenticalFileCount)));
            if (mod.IsWorkshop && mod.UpdateState == "update") cautions.Add(t("SteamWorkshopUpdateAvailable"));
            if (LauncherRequirement.IsUnmet(mod)) conflicts.Add(NamedText.Fill(t("LauncherTooOld"), ("Required", mod.MinLauncherVersion), ("Current", AppVersion.Display)));
            foreach (string problem in mod.DependencyProblems ?? new List<string>()) cautions.Add(problem);
            if (mod.ModJsonError != null) cautions.Add(NamedText.Fill(t("ModJsonNotValid"), ("Problem", mod.ModJsonError)));
            return (conflicts, cautions);
        }

        private static string ConflictsSummary(ModInfo mod, Func<string, string> t)
        {
            return NamedText.Fill(t("ConflictsSummary"), ("Count", mod.ConflictCount), ("High", mod.HighRiskConflictCount), ("Low", mod.LowRiskConflictCount));
        }

        private static string ConflictsWith(ModInfo mod, Func<string, string> t)
        {
            return NamedText.Fill(t("ConflictsWith"), ("Mods", string.Join(t("ListSeparator"), mod.ConflictMods.Take(8).ToArray())));
        }

        public static List<string> BuildProblems(ModInfo mod, int severity, Func<string, string> t)
        {
            List<string> lines = new List<string>();
            if (severity < 2) return lines;
            if (mod.HighRiskConflictCount > 0 || mod.LowRiskConflictCount > 0)
            {
                lines.Add(ConflictsSummary(mod, t));
                if (mod.ConflictMods != null && mod.ConflictMods.Count > 0)
                    lines.Add(ConflictsWith(mod, t));
            }
            foreach (string note in mod.HookNotes ?? new List<string>()) lines.Add(note);
            if (mod.EnabledCopyCount > 0) lines.Add(t("EnabledMoreThanOnce"));
            if (mod.IdenticalFileCount > 0) lines.Add(NamedText.Fill(t("SamePathAndIdenticalContent"), ("Count", mod.IdenticalFileCount)));
            if (mod.IsWorkshop && mod.UpdateState == "update") lines.Add(t("SteamWorkshopUpdateAvailable"));
            if (LauncherRequirement.IsUnmet(mod)) lines.Add(NamedText.Fill(t("LauncherTooOld"), ("Required", mod.MinLauncherVersion), ("Current", AppVersion.Display)));
            foreach (string problem in mod.DependencyProblems ?? new List<string>()) lines.Add(problem);
            if (mod.ModJsonError != null) lines.Add(NamedText.Fill(t("ModJsonNotValid"), ("Problem", mod.ModJsonError)));
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

namespace DW2ModLauncher.Core.Services
{
    public enum LineState { None, Met, Unmet }

    /// <summary>One line of a mod's links in the details pane and whether what it asks for is there.</summary>
    public sealed class DetailLine
    {
        public DetailLine(string text, LineState state = LineState.None) { Text = text; State = state; }
        public string Text { get; }
        public LineState State { get; }
        public bool IsMet { get { return State == LineState.Met; } }
        public bool IsUnmet { get { return State == LineState.Unmet; } }
    }
}
