using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DW2ModLauncher.Core.Models;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>Creating and deleting local Mods in the game's own Mod folder (never Steam Workshop copies).</summary>
    public static class LocalModManager
    {
        public const string DefaultFolderName = "NewMod";
        public const string DefaultVersion = "1.0.0";

        // Device names Windows refuses as file names, with or without an extension.
        private static readonly string[] ReservedNames =
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        /// <summary>Turns a display name into a folder name that is valid on Windows and Linux: "My Mod: Part 2" becomes "My_Mod_Part_2".</summary>
        public static string FolderNameFor(string displayName)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            StringBuilder sb = new StringBuilder();
            bool pendingSeparator = false;
            foreach (char c in (displayName ?? "").Trim())
            {
                if (char.IsWhiteSpace(c) || Array.IndexOf(invalid, c) >= 0 || "<>:\"/\\|?*".IndexOf(c) >= 0)
                {
                    pendingSeparator = sb.Length > 0;
                    continue;
                }
                if (pendingSeparator) sb.Append('_');
                pendingSeparator = false;
                sb.Append(c);
            }
            // Windows silently drops trailing dots and spaces, which makes the folder impossible to address.
            string name = sb.ToString().TrimEnd('.', '_');
            if (name.Length == 0) return DefaultFolderName;
            string stem = name.Contains('.') ? name.Substring(0, name.IndexOf('.')) : name;
            if (ReservedNames.Contains(stem, StringComparer.OrdinalIgnoreCase)) name = "_" + name;
            return name;
        }

        /// <summary>
        /// Creates <paramref name="managedRoot"/>/&lt;sanitized name&gt;/mod.json and returns the new folder.
        /// Throws <see cref="IOException"/> if the folder already exists, so an existing Mod is never overwritten.
        /// </summary>
        public static string Create(string managedRoot, string displayName, string description = "")
        {
            if (string.IsNullOrWhiteSpace(managedRoot)) throw new DirectoryNotFoundException("The DW2 Mod folder is not set.");
            string name = (displayName ?? "").Trim();
            if (name.Length == 0) throw new ArgumentException("A Mod name is required.", nameof(displayName));

            string folder = Path.Combine(managedRoot, FolderNameFor(name));
            if (Directory.Exists(folder) || File.Exists(folder)) throw new IOException("A Mod folder named \"" + Path.GetFileName(folder) + "\" already exists.");

            Directory.CreateDirectory(folder);
            JsonObject modJson = new JsonObject
            {
                ["displayName"] = name,
                ["description"] = description ?? "",
                ["version"] = DefaultVersion
            };
            ModJsonFile.Save(Path.Combine(folder, "mod.json"), modJson);
            return folder;
        }

        /// <summary>True only for a local Mod whose folder sits directly inside the game's Mod folder. Workshop copies and anything outside it are refused.</summary>
        public static bool CanDelete(ModInfo mod, string managedRoot)
        {
            if (mod == null || mod.IsWorkshop) return false;
            if (string.IsNullOrWhiteSpace(mod.Folder) || string.IsNullOrWhiteSpace(managedRoot)) return false;
            string folder = Path.GetFullPath(mod.Folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string root = Path.GetFullPath(managedRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string parent = Path.GetDirectoryName(folder);
            // Compare case-insensitively on Windows only; Linux folders "Mod" and "mod" are different.
            StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return parent != null && parent.Equals(root, comparison);
        }

        /// <summary>Deletes the Mod's folder and everything in it. Throws <see cref="InvalidOperationException"/> if <see cref="CanDelete"/> is false.</summary>
        public static void Delete(ModInfo mod, string managedRoot)
        {
            if (!CanDelete(mod, managedRoot)) throw new InvalidOperationException("Only a local Mod in the game's Mod folder can be deleted.");
            if (Directory.Exists(mod.Folder)) Directory.Delete(mod.Folder, true);
        }
    }
}
