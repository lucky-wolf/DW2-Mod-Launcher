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
            if (mod.RequiredMods != null && mod.RequiredMods.Count > 0) b.AppendLine("Required: " + string.Join(", ", mod.RequiredMods.ToArray()));
            if (mod.OptionalMods != null && mod.OptionalMods.Count > 0) b.AppendLine("Optional: " + string.Join(", ", mod.OptionalMods.ToArray()));
            if (mod.IncompatibleMods != null && mod.IncompatibleMods.Count > 0) b.AppendLine("Incompatible: " + string.Join(", ", mod.IncompatibleMods.ToArray()));
            if (mod.LoadBefore != null && mod.LoadBefore.Count > 0) b.AppendLine("LoadBefore: " + string.Join(", ", mod.LoadBefore.ToArray()));
            if (mod.LoadAfter != null && mod.LoadAfter.Count > 0) b.AppendLine("LoadAfter: " + string.Join(", ", mod.LoadAfter.ToArray()));

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

        /// <summary>
        /// Where the mod comes from, with its folder: "Local\MyMod" (the path under the managed Mod folder, so nested folders show),
        /// "Steam Workshop\1234567". Just the source name when the folder is unknown.
        /// </summary>
        public static string SourceText(ModInfo mod, string managedModsRoot)
        {
            string source = mod.SourceName ?? "";
            if (string.IsNullOrWhiteSpace(mod.Folder)) return source;
            string folder = Path.GetFileName(mod.Folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (!mod.IsWorkshop && !string.IsNullOrWhiteSpace(managedModsRoot) && ModFileImporter.IsInside(managedModsRoot, mod.Folder))
                folder = Path.GetRelativePath(managedModsRoot, mod.Folder);
            return folder.Length == 0 ? source : source + Path.DirectorySeparatorChar + folder;
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
