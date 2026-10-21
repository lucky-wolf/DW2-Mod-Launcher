using System;
using System.Collections.Generic;
using System.IO;
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
                return value == null ? (null, string.Format(t("HookFileEmpty"), path)) : (value, null);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
            {
                return (null, string.Format(t("HookFileUnreadable"), path, ex.Message));
            }
        }
    }
}
