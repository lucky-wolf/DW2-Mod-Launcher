using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DW2ModLauncher.Core.Models;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>
    /// Keeps a local mod the same mod when its folder is renamed. Until a mod has a Workshop id the folder name is all
    /// that identifies it; once it has one, the launcher remembers the folder name it had (the "mods/&lt;folder&gt;" token DW2's
    /// mods.json uses), and when the folder later has a different name it carries the mod's mods.json entry (enabled, same
    /// place in the load order), stored settings and editor state over to the new name. Local and subscribed copies of the
    /// same item stay separate ("local/&lt;id&gt;" versus "steam/&lt;id&gt;").
    /// </summary>
    public static class LocalModIdentity
    {
        /// <summary>
        /// Adopts every published local mod in <paramref name="localMods"/>. Returns the problems met (each is one message for the
        /// user); <paramref name="settingsChanged"/> says whether the launcher settings were edited and need saving.
        /// <paramref name="order"/> is updated in place when a renamed mod's mods.json entry is rewritten.
        /// </summary>
        public static List<string> Adopt(IEnumerable<ModInfo> localMods, LauncherSettings settings, ModOrderState order, string modsJsonPath, out bool settingsChanged)
        {
            List<string> problems = new List<string>();
            settingsChanged = false;
            if (settings.LocalModTokens == null) settings.LocalModTokens = new Dictionary<string, string>();
            foreach (ModInfo mod in (localMods ?? new List<ModInfo>()).Where(m => m != null && m.PublishedLocalKey != null))
            {
                string key = mod.PublishedLocalKey;
                settings.LocalModTokens.TryGetValue(key, out string remembered);
                try
                {
                    // Stored settings: from the folder name it had when last seen, and from its plain folder name (the state before it had an id).
                    ModSettingsStore.AdoptStoredUnder(mod, remembered);
                    ModSettingsStore.AdoptStoredUnder(mod, mod.ActiveToken);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    Diagnostics.Logger.LogException("Move stored settings of " + mod.DisplayName, ex);
                    problems.Add(mod.DisplayName + ": its stored settings could not be moved to its new name.\n" + ex.Message);
                }

                AdoptEditorState(settings, key, mod.Id);
                if (!string.IsNullOrWhiteSpace(remembered) && !string.Equals(remembered, mod.ActiveToken, StringComparison.OrdinalIgnoreCase))
                {
                    AdoptEditorState(settings, key, remembered.StartsWith("mods/", StringComparison.OrdinalIgnoreCase) ? remembered.Substring(5) : remembered);
                    if (order != null && order.FileFound && order.Contains(remembered) && !order.Contains(mod.ActiveToken))
                    {
                        try
                        {
                            List<string> next = order.Order.Select(t => string.Equals(t, remembered, StringComparison.OrdinalIgnoreCase) ? mod.ActiveToken : t).ToList();
                            List<string> written = ModOrderStore.Write(modsJsonPath, next);
                            if (written != null) order.Order = written;
                        }
                        catch (Exception ex)
                        {
                            Diagnostics.Logger.LogException("Rename load-order entry of " + mod.DisplayName, ex);
                            problems.Add(mod.DisplayName + ": its folder was renamed (" + remembered + " is now " + mod.ActiveToken + ") but its entry in DW2's mods.json could not be updated, so it is not enabled.\n" + ex.Message);
                            continue;
                        }
                    }
                }
                if (!string.Equals(remembered, mod.ActiveToken, StringComparison.Ordinal))
                {
                    settings.LocalModTokens[key] = mod.ActiveToken;
                    settingsChanged = true;
                }
            }
            return problems;
        }

        // Moves the editor state kept under a folder-name key to the id-based key, unless the id-based key already has some.
        private static void AdoptEditorState(LauncherSettings settings, string key, string oldKey)
        {
            if (string.IsNullOrWhiteSpace(oldKey) || oldKey == key) return;
            Move(settings.CollapsedSettingGroups, oldKey, key);
            Move(settings.SettingsScrollOffsets, oldKey, key);
            Move(settings.VersionBump, oldKey, key);
        }

        private static void Move<T>(Dictionary<string, T> map, string from, string to)
        {
            if (map == null || !map.TryGetValue(from, out T value)) return;
            if (!map.ContainsKey(to)) map[to] = value;
            map.Remove(from);
        }
    }
}
