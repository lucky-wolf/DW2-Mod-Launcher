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
            string description = !string.IsNullOrWhiteSpace(mod.WorkshopDescription) ? mod.WorkshopDescription : mod.Description;
            if (!string.IsNullOrWhiteSpace(description)) b.AppendLine(Regex.Replace(description.Trim(), "\\[/?[^\\]]+\\]", ""));
            b.AppendLine();
            b.AppendLine(t("Source") + ": " + (mod.SourceName ?? ""));
            b.AppendLine(t("State") + (isSelected ? "ON" : "OFF"));
            b.AppendLine(mod.Folder ?? "");
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

            if (!isSelected)
            {
                b.AppendLine();
                b.AppendLine(t("ModDisabledNote"));
            }
            else if (mod.ConflictCount == 0 && mod.IdenticalFileCount == 0)
            {
                b.AppendLine();
                b.AppendLine(t("NoFileConflicts"));
            }

            if (isSelected && mod.ConflictFiles != null && mod.ConflictFiles.Count > 0)
            {
                b.AppendLine();
                b.AppendLine(t("ConflictFilesHeader"));
                foreach (string file in mod.ConflictFiles.Take(8)) b.AppendLine("  • " + file);
                if (mod.ConflictFiles.Count > 8) b.AppendLine("  ... +" + (mod.ConflictFiles.Count - 8));
            }

            if (mod.DuplicateCount > 0)
            {
                b.AppendLine();
                b.AppendLine(t("DuplicateLocationsHeader"));
                foreach (string location in mod.DuplicateLocations.Take(8)) b.AppendLine("  • " + location);
            }

            if (mod.IsWorkshop)
            {
                b.AppendLine();
                if (mod.UpdateState == "update")
                    b.AppendLine(t("SteamWorkshopUpdateAvailable"));
                else if (mod.UpdateState == "current")
                    b.AppendLine(t("SteamWorkshopUpToDate"));
                else
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
            if (mod.IdenticalFileCount > 0) lines.Add(t("SamePathAndIdenticalContent") + mod.IdenticalFileCount);
            if (mod.DuplicateCount > 0) lines.Add(t("DuplicateInstallationsDetail") + mod.DuplicateCount + t("Locations"));
            if (mod.IsWorkshop && mod.UpdateState == "update") lines.Add(t("SteamWorkshopUpdateAvailable"));
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
