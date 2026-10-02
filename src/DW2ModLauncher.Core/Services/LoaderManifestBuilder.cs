using System;
using System.Collections.Generic;
using System.IO;
using DW2ModLauncher.Core.Models;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>
    /// Builds the manifest DW2ModLauncher.Loader.Entry.Init() reads at --low-level-inject time,
    /// from every enabled mod's declarative injection target (ModInfo.InjectionDll/
    /// InjectionEntryPoint, or launcher.json's injection block) - the same source data
    /// MainForm.Launch.CollectInjectionTargets used to compose directly into a
    /// --low-level-inject CLI string, before the loader existed.
    /// </summary>
    public static class LoaderManifestBuilder
    {
        public static LoaderManifest Build(List<ModInfo> orderedEnabledMods)
        {
            LoaderManifest manifest = new LoaderManifest();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ModInfo mod in orderedEnabledMods ?? new List<ModInfo>())
            {
                string modRoot = mod.ContentRoot ?? mod.Folder;
                AddEntry(manifest, seen, mod, modRoot, mod.InjectionDll, mod.InjectionEntryPoint);
                LauncherMeta meta = LauncherMetaReader.Read(mod);
                if (meta?.injection != null)
                    AddEntry(manifest, seen, mod, modRoot, meta.injection.dll, meta.injection.entryPoint);
            }
            return manifest;
        }

        private static void AddEntry(LoaderManifest manifest, HashSet<string> seen, ModInfo mod, string modRoot, string dllRelative, string entryPoint)
        {
            if (string.IsNullOrWhiteSpace(modRoot) || string.IsNullOrWhiteSpace(dllRelative) || string.IsNullOrWhiteSpace(entryPoint)) return;
            string full = Path.GetFullPath(Path.Combine(modRoot, dllRelative.Replace('/', Path.DirectorySeparatorChar)));
            if (!seen.Add(full + "!" + entryPoint)) return;

            string settingsJson = null;
            ModSettingsSchema schema = ModSettingsSchemaReader.Read(modRoot);
            if (schema != null)
                settingsJson = ModSettingsStore.GetOrCreateValues(mod, schema).ToJsonString();

            manifest.Entries.Add(new LoaderManifestEntry
            {
                DllPath = GamePaths.ToGameVisiblePath(full),
                EntryType = entryPoint,
                SettingsJson = settingsJson,
                DisplayName = mod.DisplayName ?? mod.Id
            });
        }
    }
}
