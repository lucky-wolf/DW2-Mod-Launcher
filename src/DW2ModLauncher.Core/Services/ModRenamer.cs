using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using DW2ModLauncher.Core.Models;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>What a rename did.</summary>
    public class ModRenameResult
    {
        public string NewFolder { get; set; }
        public string NewToken { get; set; }
        /// <summary>The cover art file's new name when it carried the old name and was renamed with the mod, else null.</summary>
        public string RenamedPreview { get; set; }
        /// <summary>Things the rename could not carry over after the folder had already moved (each is one message for the user).</summary>
        public List<string> Problems { get; } = new List<string>();
        /// <summary>Other mods whose dw2modlauncher.json dependencies name this mod by its old name and now point at nothing.</summary>
        public List<string> BrokenDependents { get; } = new List<string>();
        /// <summary>The launcher settings were edited and need saving.</summary>
        public bool SettingsChanged { get; set; }
    }

    /// <summary>
    /// Renames a local mod: its mod.json displayName, its folder (exactly the sanitized name, see <see cref="LocalModManager.FolderNameFor"/>), its
    /// cover art when that carries the old name, and everything kept under the old name: DW2's mods.json and every named profile (the entry is
    /// replaced in the same slot, so enabled state and load order are unchanged), the stored settings and remembered defaults, editor state,
    /// version-bump policy, selection, and code-overlap acknowledgements.
    /// Everything that can be checked is checked before anything is touched; the folder move comes first, so a locked folder changes nothing.
    /// </summary>
    public static class ModRenamer
    {
        public static ModRenameResult Rename(ModInfo mod, string newName, LauncherSettings settings, ModOrderState order, IEnumerable<ModInfo> allMods)
        {
            if (!LocalModManager.CanRename(mod, settings.ManagedModsRoot)) throw new InvalidOperationException("Only a local Mod in the game's Mod folder can be renamed.");
            string name = (newName ?? "").Trim();
            if (name.Length == 0) throw new ArgumentException("A Mod name is required.", nameof(newName));
            if (string.IsNullOrWhiteSpace(mod.ModJsonPath) || !File.Exists(mod.ModJsonPath)) throw new FileNotFoundException("The Mod has no mod.json to rename in.", mod.ModJsonPath);

            string oldFolderName = Path.GetFileName(mod.Folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            string newFolderName = LocalModManager.FolderNameFor(name);
            string newFolder = Path.Combine(settings.ManagedModsRoot, newFolderName);
            bool folderChanges = !string.Equals(oldFolderName, newFolderName, StringComparison.Ordinal);
            bool caseOnly = folderChanges && string.Equals(oldFolderName, newFolderName, StringComparison.OrdinalIgnoreCase) && OperatingSystem.IsWindows();
            if (folderChanges && !caseOnly && (Directory.Exists(newFolder) || File.Exists(newFolder)))
                throw new IOException("A Mod folder named \"" + newFolderName + "\" already exists.");

            string oldToken = mod.ActiveToken;
            string newToken = "mods/" + newFolderName;
            string oldDisplayName = mod.DisplayName;
            ModRenameResult result = new ModRenameResult { NewFolder = newFolder, NewToken = newToken };

            JsonObject root = JsonNode.Parse(File.ReadAllText(mod.ModJsonPath, Encoding.UTF8)) as JsonObject;
            if (root == null) throw new InvalidDataException("mod.json is not a JSON object: " + mod.ModJsonPath);

            // Cover art that carries the old name (what the publish dialog offers by default) follows the mod; any other name is the author's own.
            string previewKey = root.Select(kv => kv.Key).FirstOrDefault(k => k.Equals("previewImage", StringComparison.OrdinalIgnoreCase));
            string previewRel = previewKey == null ? null : root[previewKey]?.ToString();
            string newPreviewRel = null;
            if (!string.IsNullOrWhiteSpace(previewRel))
            {
                previewRel = previewRel.Replace('\\', '/');
                string stem = Path.GetFileNameWithoutExtension(previewRel);
                bool defaultName = string.Equals(stem, LocalModManager.FolderNameFor(oldDisplayName), StringComparison.OrdinalIgnoreCase)
                    || string.Equals(stem, oldFolderName, StringComparison.OrdinalIgnoreCase);
                string from = Path.Combine(mod.Folder, previewRel.Replace('/', Path.DirectorySeparatorChar));
                if (defaultName && File.Exists(from))
                {
                    int slash = previewRel.LastIndexOf('/');
                    newPreviewRel = (slash < 0 ? "" : previewRel.Substring(0, slash + 1)) + newFolderName + Path.GetExtension(previewRel);
                    string to = Path.Combine(mod.Folder, newPreviewRel.Replace('/', Path.DirectorySeparatorChar));
                    if (string.Equals(from, to, StringComparison.Ordinal)) newPreviewRel = null;
                    else if (File.Exists(to) && !string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("The cover art cannot be renamed to \"" + newPreviewRel + "\": a file of that name already exists in the Mod.");
                }
            }

            // 1. The folder (the likeliest thing to fail: something has a file open in it). Nothing has changed if this throws.
            if (folderChanges) MoveDirectory(mod.Folder, newFolder, caseOnly);

            // 2. Inside the moved folder: cover art, then mod.json.
            string modJsonPath = Path.Combine(folderChanges ? newFolder : mod.Folder, Path.GetFileName(mod.ModJsonPath));
            if (newPreviewRel != null)
            {
                string inside = folderChanges ? newFolder : mod.Folder;
                MoveFile(Path.Combine(inside, previewRel.Replace('/', Path.DirectorySeparatorChar)), Path.Combine(inside, newPreviewRel.Replace('/', Path.DirectorySeparatorChar)));
                root[previewKey] = newPreviewRel;
                result.RenamedPreview = newPreviewRel;
            }
            string displayKey = root.Select(kv => kv.Key).FirstOrDefault(k => k.Equals("displayName", StringComparison.OrdinalIgnoreCase));
            if (displayKey != null) root.Remove(displayKey);
            root["displayName"] = name;
            ModJsonFile.Save(modJsonPath, root);

            if (folderChanges)
            {
                // 3. DW2's mods.json and every named profile: the entry is replaced in place, so it keeps its slot.
                string modsJson = ModOrderStore.PathFor(settings.ManagedModsRoot, settings.GameRoot);
                RenameInOrder(modsJson, oldToken, newToken, result, "mods.json", order);
                GameProfileStore profiles = new GameProfileStore(Path.GetDirectoryName(modsJson));
                foreach (string profile in profiles.ListNames())
                    RenameInOrder(profiles.PathFor(profile), oldToken, newToken, result, "profile \"" + profile + "\"", null);

                // 4. Stored settings and remembered defaults (a published mod's are kept under its Workshop id and do not move).
                if (mod.PublishedLocalKey == null)
                {
                    ModInfo renamed = new ModInfo { Id = newFolderName, Folder = newFolder, ActiveToken = newToken, IsWorkshop = false, DisplayName = name };
                    try
                    {
                        ModSettingsStore.AdoptStoredUnder(renamed, oldToken);
                        foreach (string leftover in new[] { ModSettingsStore.GetSettingsPath(mod), ModSettingsStore.GetDefaultsPath(mod) }.Where(File.Exists))
                            result.Problems.Add("Settings were already stored under the new name, so the old ones were left in place: " + leftover);
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                        Diagnostics.Logger.LogException("Move stored settings of " + oldDisplayName, ex);
                        result.Problems.Add("Its stored settings could not be moved to the new name.\n" + ex.Message);
                    }
                }

                // 5. The launcher's own state.
                RenameInSettings(settings, mod, oldFolderName, newFolderName, oldToken, newToken, result);
            }

            // Other mods find this one by id, display name, token or folder name; an old one that no longer matches is a dead reference.
            string[] oldNames = { mod.Id, oldDisplayName, oldToken, oldFolderName };
            string[] newNames = { newFolderName, name, newToken };
            foreach (ModInfo other in (allMods ?? new List<ModInfo>()).Where(m => m != null && m != mod))
                foreach (LauncherDependency dep in other.Dependencies ?? new List<LauncherDependency>())
                {
                    if (dep == null || string.IsNullOrWhiteSpace(dep.mod)) continue;
                    string wanted = dep.mod.Trim();
                    if (oldNames.Any(n => string.Equals(n, wanted, StringComparison.OrdinalIgnoreCase)) && !newNames.Any(n => string.Equals(n, wanted, StringComparison.OrdinalIgnoreCase)))
                    {
                        result.BrokenDependents.Add(other.DisplayName ?? other.Id);
                        break;
                    }
                }
            return result;
        }

        private static void RenameInOrder(string path, string oldToken, string newToken, ModRenameResult result, string what, ModOrderState live)
        {
            ModOrderState state = ModOrderStore.Read(path);
            if (state.ReadFailed) { result.Problems.Add("DW2's " + what + " could not be read, so its entry for this mod was not renamed: " + path); return; }
            if (!state.FileFound || !state.Contains(oldToken)) return;
            try
            {
                List<string> next = state.Order.Select(t => string.Equals(t, oldToken, StringComparison.OrdinalIgnoreCase) ? newToken : t).ToList();
                List<string> written = ModOrderStore.Write(path, next);
                if (live != null && written != null) { live.Order = written; live.FileFound = true; }
            }
            catch (Exception ex)
            {
                Diagnostics.Logger.LogException("Rename entry in " + what, ex);
                result.Problems.Add("Its entry in DW2's " + what + " could not be renamed (" + oldToken + " is now " + newToken + ").\n" + ex.Message);
            }
        }

        private static void RenameInSettings(LauncherSettings settings, ModInfo mod, string oldFolderName, string newFolderName, string oldToken, string newToken, ModRenameResult result)
        {
            string oldKey = "managed:" + (mod.Id ?? oldFolderName);
            string newKey = "managed:" + newFolderName;
            result.SettingsChanged |= Rekey(settings.SelectedMods, oldKey, newKey);
            if (string.Equals(settings.LastSelectedMod, oldToken, StringComparison.OrdinalIgnoreCase)) { settings.LastSelectedMod = newToken; result.SettingsChanged = true; }
            if (mod.PublishedLocalKey != null)
            {
                if (settings.LocalModTokens != null && settings.LocalModTokens.ContainsKey(mod.PublishedLocalKey)) { settings.LocalModTokens[mod.PublishedLocalKey] = newToken; result.SettingsChanged = true; }
            }
            else
            {
                string oldState = mod.Id ?? oldFolderName;
                result.SettingsChanged |= Rekey(settings.CollapsedSettingGroups, oldState, newFolderName);
                result.SettingsChanged |= Rekey(settings.SettingsScrollOffsets, oldState, newFolderName);
                result.SettingsChanged |= Rekey(settings.VersionBump, oldState, newFolderName);
            }
            if (settings.AcknowledgedHooks != null)
            {
                List<string> next = settings.AcknowledgedHooks.Select(k => RenameInAcknowledgement(k, oldKey, newKey)).ToList();
                if (!next.SequenceEqual(settings.AcknowledgedHooks)) { settings.AcknowledgedHooks = next; result.SettingsChanged = true; }
            }
        }

        // "target | sideA | sideB" where a side is "<mod key>@<version>", the two sides sorted (HookOverlapAnalyzer.AcknowledgementKey); rename a side and re-sort.
        private static string RenameInAcknowledgement(string ack, string oldKey, string newKey)
        {
            int second = ack.LastIndexOf(" | ", StringComparison.Ordinal);
            int first = second < 1 ? -1 : ack.LastIndexOf(" | ", second - 1, StringComparison.Ordinal);
            if (first < 0) return ack;
            string target = ack.Substring(0, first);
            string[] sides = { ack.Substring(first + 3, second - first - 3), ack.Substring(second + 3) };
            bool changed = false;
            for (int i = 0; i < 2; i++)
                if (sides[i].StartsWith(oldKey + "@", StringComparison.OrdinalIgnoreCase)) { sides[i] = newKey + sides[i].Substring(oldKey.Length); changed = true; }
            if (!changed) return ack;
            Array.Sort(sides, StringComparer.OrdinalIgnoreCase);
            return target + " | " + sides[0] + " | " + sides[1];
        }

        private static bool Rekey<T>(Dictionary<string, T> map, string from, string to)
        {
            if (map == null || string.Equals(from, to, StringComparison.Ordinal)) return false;
            string existing = map.Keys.FirstOrDefault(k => string.Equals(k, from, StringComparison.OrdinalIgnoreCase));
            if (existing == null) return false;
            T value = map[existing];
            map.Remove(existing);
            map[to] = value;
            return true;
        }

        private static void MoveDirectory(string from, string to, bool caseOnly)
        {
            if (!caseOnly) { Directory.Move(from, to); return; }
            // Windows refuses a move that changes only case, so go through a temporary name.
            string temp = from.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + ".renaming-" + Guid.NewGuid().ToString("N");
            Directory.Move(from, temp);
            Directory.Move(temp, to);
        }

        private static void MoveFile(string from, string to)
        {
            if (!string.Equals(from, to, StringComparison.OrdinalIgnoreCase)) { File.Move(from, to); return; }
            string temp = from + ".renaming-" + Guid.NewGuid().ToString("N");
            File.Move(from, temp);
            File.Move(temp, to);
        }
    }
}
