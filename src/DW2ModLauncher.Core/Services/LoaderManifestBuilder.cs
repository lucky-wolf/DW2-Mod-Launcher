using System;
using System.Collections.Generic;
using System.IO;
using DW2ModLauncher.Core.Models;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>
    /// Builds the manifest DW2ModLauncher.Loader.Entry.Init() reads at --low-level-inject time.
    /// A mod's injection DLLs are inferred (InjectionScanner: a public static "Entry" class with Init()/
    /// InitWithOptions(string)); a dw2modlauncher.json "injection" block in the mod folder overrides that.
    /// mod.json is DW2's own file and is not consulted.
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
                foreach (InjectionTarget target in InjectionScanner.TargetsFor(mod))
                    AddEntry(manifest, seen, mod, modRoot, target.Dll, target.EntryPoint);
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
                HostDllPath = full,
                EntryType = entryPoint,
                SettingsJson = settingsJson,
                DisplayName = mod.DisplayName ?? mod.Id
            });
        }
    }
}
