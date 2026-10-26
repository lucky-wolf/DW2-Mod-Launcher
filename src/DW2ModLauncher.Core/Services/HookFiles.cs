using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using DW2ModLauncher.Core.Models;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>Where the hook files are, and reading them (docs/plans/hook-manifest.md). A file that is missing is "none"; one that is unreadable is an error the caller shows.</summary>
    public static class HookFiles
    {
        /// <summary>The name of the hooks a mod declares, beside its dw2modlauncher.json.</summary>
        public const string ManifestFileName = "dw2modlauncher.hooks.json";

        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

        /// <summary>Where the loader writes what it saw patched in the game: the launcher's data folder.</summary>
        public static string ObservedPath()
        {
            return Path.Combine(UserDataRoot.Get(), "hooks-observed.json");
        }

        /// <summary>The mod's declared hooks. Manifest is null when the mod ships none (Error null) or the file cannot be read (Error says why).</summary>
        public static (HookManifest Manifest, string Error) ReadManifest(ModInfo mod, Func<string, string> t)
        {
            string root = mod?.ContentRoot ?? mod?.Folder;
            if (string.IsNullOrWhiteSpace(root)) return (null, null);
            return Read<HookManifest>(Path.Combine(root, ManifestFileName), t);
        }

        /// <summary>What the loader saw patched in the last session. File is null when there is none yet (Error null) or it cannot be read (Error says why).</summary>
        public static (ObservedHooksFile File, string Error) ReadObserved(string path, Func<string, string> t)
        {
            return Read<ObservedHooksFile>(path, t);
        }

        /// <summary>
        /// Where the launcher keeps what every game session has seen, per mod. The loader's file (<see cref="ObservedPath"/>) is rewritten by each
        /// session with only the mods that session loaded, so a mod taken out of the launch list would lose its record; this file keeps it.
        /// </summary>
        public static string HistoryPath()
        {
            return Path.Combine(UserDataRoot.Get(), "hooks-observed-history.json");
        }

        private static readonly JsonSerializerOptions WriteOptions = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        /// <summary>
        /// The session's record folded into the history: each mod the session loaded replaces whatever the history held for it, a mod the session did
        /// not load keeps its earlier record (version and patches), and the session's own details (time, game version, channel problems) are the latest.
        /// </summary>
        public static ObservedHooksFile MergeObserved(ObservedHooksFile history, ObservedHooksFile session)
        {
            HashSet<string> loaded = new HashSet<string>((session.Mods ?? new List<ObservedModInfo>()).Select(m => m.Key), StringComparer.OrdinalIgnoreCase);
            List<ObservedModInfo> kept = (history?.Mods ?? new List<ObservedModInfo>()).Where(m => !loaded.Contains(m.Key)).ToList();
            HashSet<string> keptKeys = new HashSet<string>(kept.Select(m => m.Key), StringComparer.OrdinalIgnoreCase);

            Dictionary<string, ObservedHookInfo> hooks = new Dictionary<string, ObservedHookInfo>(StringComparer.Ordinal);
            void Take(IEnumerable<ObservedHookInfo> from, Func<ObservedPatchInfo, bool> wanted)
            {
                foreach (ObservedHookInfo hook in from ?? Enumerable.Empty<ObservedHookInfo>())
                {
                    List<ObservedPatchInfo> patches = (hook.Patches ?? new List<ObservedPatchInfo>()).Where(wanted).ToList();
                    if (patches.Count == 0) continue;
                    if (!hooks.TryGetValue(hook.Target, out ObservedHookInfo into)) hooks[hook.Target] = into = new ObservedHookInfo { Target = hook.Target };
                    into.Patches.AddRange(patches);
                }
            }
            Take(history?.Hooks, p => p.Owner != null && keptKeys.Contains(p.Owner));
            Take(session.Hooks, p => true);

            return new ObservedHooksFile
            {
                Schema = session.Schema,
                CapturedAt = session.CapturedAt,
                GameVersion = session.GameVersion,
                Mods = kept.Concat(session.Mods ?? new List<ObservedModInfo>()).ToList(),
                Hooks = hooks.Values.OrderBy(h => h.Target, StringComparer.Ordinal).ToList(),
                Channels = session.Channels ?? new ObservedChannelsInfo()
            };
        }

        /// <summary>
        /// Folds the loader's file from the last session into the history and returns the history: what every mod seen so far patched, whether or not it
        /// was in the last session. Error says what could not be read or written; a history that cannot be read is left alone, never overwritten.
        /// </summary>
        public static (ObservedHooksFile File, string Error) UpdateObservedHistory(string sessionPath, string historyPath, Func<string, string> t)
        {
            (ObservedHooksFile history, string historyError) = Read<ObservedHooksFile>(historyPath, t);
            if (historyError != null) return (null, historyError);
            (ObservedHooksFile session, string sessionError) = ReadObserved(sessionPath, t);
            if (sessionError != null) return (history, sessionError);
            if (session == null || (history != null && history.CapturedAt == session.CapturedAt)) return (history, null);
            ObservedHooksFile merged = MergeObserved(history, session);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(historyPath));
                string temp = historyPath + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(merged, WriteOptions));
                if (File.Exists(historyPath)) File.Delete(historyPath);
                File.Move(temp, historyPath);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return (merged, NamedText.Fill(t("HookHistoryNotSaved"), ("File", historyPath), ("Problem", ex.Message)));
            }
            return (merged, null);
        }

        /// <summary>
        /// Whether the settings key that switches a declared hook on is on for the mod: false only when the mod has a settings file with that key
        /// (matched ignoring case) set to false. A mod without a settings schema, or without that key, has the hook on.
        /// </summary>
        public static bool SettingIsOn(ModInfo mod, string key)
        {
            ModSettingsSchema schema = ModSettingsSchemaReader.Read(mod.ContentRoot ?? mod.Folder);
            if (schema == null) return true;
            foreach (KeyValuePair<string, JsonNode> value in ModSettingsStore.GetOrCreateValues(mod, schema))
                if (string.Equals(value.Key, key, StringComparison.OrdinalIgnoreCase))
                    return !(value.Value is JsonValue v && v.TryGetValue(out bool on) && !on);
            return true;
        }

        private static (T Value, string Error) Read<T>(string path, Func<string, string> t) where T : class
        {
            if (!File.Exists(path)) return (null, null);
            try
            {
                T value = JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options);
                return value == null ? (null, NamedText.Fill(t("HookFileEmpty"), ("File", path))) : (value, null);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
            {
                return (null, NamedText.Fill(t("HookFileUnreadable"), ("File", path), ("Problem", ex.Message)));
            }
        }
    }
}
