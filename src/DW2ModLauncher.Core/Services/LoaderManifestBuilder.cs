using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
                manifest.ModFolders.Add(LeafName(mod.Folder ?? modRoot));
                foreach (InjectionTarget target in InjectionScanner.TargetsFor(mod))
                    AddEntry(manifest, seen, mod, modRoot, target.Dll, target.EntryPoint);
                AddPatches(manifest, mod, modRoot, manifest.ModFolders.Count - 1);
            }
            manifest.Fonts.AddRange(FontsOf(orderedEnabledMods));
            manifest.TextFiles.AddRange(TextFilesOf(orderedEnabledMods));
            manifest.GalactopediaReplacesVanilla = (orderedEnabledMods ?? new List<ModInfo>()).Any(ReplacesGalactopedia);
            return manifest;
        }

        /// <summary>
        /// The mods' replacements for the text files the game reads straight from its data folder, bypassing the mod file lookup:
        /// Hints.txt, dialog/*.txt and Galactopedia/**/*.txt. (GameText.txt and SystemNames.txt go through the mod lookup and need nothing.)
        /// For the same file the last mod in load order wins, as for the game's own data files.
        /// </summary>
        public static List<LoaderManifestTextFile> TextFilesOf(IEnumerable<ModInfo> orderedEnabledMods)
        {
            Dictionary<string, LoaderManifestTextFile> found = new Dictionary<string, LoaderManifestTextFile>(StringComparer.OrdinalIgnoreCase);
            foreach (ModInfo mod in orderedEnabledMods ?? new List<ModInfo>())
            {
                string root = mod.ContentRoot ?? mod.Folder;
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;
                // A mod that replaces the Galactopedia drops the articles of the mods before it (and the game's own, see the manifest).
                if (ReplacesGalactopedia(mod))
                {
                    foreach (string key in found.Keys.Where(k => k.StartsWith("Galactopedia/", StringComparison.OrdinalIgnoreCase)).ToList()) found.Remove(key);
                }
                foreach (string file in Directory.GetFiles(root, "*.txt", SearchOption.AllDirectories))
                {
                    string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                    bool read = relative.Equals("Hints.txt", StringComparison.OrdinalIgnoreCase)
                        || relative.StartsWith("dialog/", StringComparison.OrdinalIgnoreCase)
                        || relative.StartsWith("Galactopedia/", StringComparison.OrdinalIgnoreCase);
                    if (!read) continue;
                    found[relative] = new LoaderManifestTextFile { Relative = relative, Path = GamePaths.ToGameVisiblePath(Path.GetFullPath(file)) };
                }
            }
            return found.Values.OrderBy(f => f.Relative, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>Whether the mod's dw2modlauncher.json says "galactopedia": "replace".</summary>
        public static bool ReplacesGalactopedia(ModInfo mod)
        {
            return string.Equals(LauncherMetaReader.Read(mod)?.galactopedia?.Trim(), "replace", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The mod's XML patch files: every *.xml below its "patches" folder, in ordinal path order. They live there, not in the
        /// mod root, because the game loads root *.xml files as data and would read a partial patch as a whole entity.
        /// </summary>
        public static List<string> PatchFilesOf(string modRoot)
        {
            string dir = string.IsNullOrWhiteSpace(modRoot) ? null : Path.Combine(modRoot, "patches");
            if (dir == null || !Directory.Exists(dir)) return new List<string>();
            List<string> files = new List<string>(Directory.GetFiles(dir, "*.xml", SearchOption.AllDirectories));
            files.Sort(StringComparer.OrdinalIgnoreCase);
            return files;
        }

        /// <summary>
        /// The font bundles declared by the mods (dw2modlauncher.json "font"), in the order given. Only plain names count: the value ends up
        /// on the game's command line and in a file name, so anything but letters, digits, '_' and '-' is ignored. A font whose
        /// Name.bundle is not in the mod folder is dropped too: the game would be started with a --font it cannot find (see FontIssues).
        /// </summary>
        public static List<LoaderManifestFont> FontsOf(IEnumerable<ModInfo> orderedEnabledMods)
        {
            List<LoaderManifestFont> fonts = new List<LoaderManifestFont>();
            foreach (ModInfo mod in orderedEnabledMods ?? new List<ModInfo>())
            {
                string name = LauncherMetaReader.Read(mod)?.font?.Trim();
                string root = mod.ContentRoot ?? mod.Folder;
                if (string.IsNullOrEmpty(name) || string.IsNullOrWhiteSpace(root) || !IsPlainName(name)) continue;
                if (!File.Exists(Path.Combine(root, name + ".bundle"))) continue;
                fonts.Add(new LoaderManifestFont
                {
                    DisplayName = mod.DisplayName ?? mod.Id,
                    Name = name,
                    Folder = GamePaths.ToGameVisiblePath(Path.GetFullPath(root))
                });
            }
            return fonts;
        }

        /// <summary>
        /// What is wrong with the declared fonts, for the pre-launch warnings: a declared font whose bundle file is missing from its
        /// mod ("missing"), and a font that loses to a later mod's different font, since the game takes only one ("overridden").
        /// Each tuple is (kind, mod display name, font name, name of the winning font or null).
        /// </summary>
        public static List<(string Kind, string Mod, string Font, string Winner)> FontIssues(IEnumerable<ModInfo> orderedEnabledMods)
        {
            List<(string Kind, string Mod, string Font, string Winner)> issues = new List<(string Kind, string Mod, string Font, string Winner)>();
            List<ModInfo> mods = (orderedEnabledMods ?? new List<ModInfo>()).ToList();
            foreach (ModInfo mod in mods)
            {
                string name = LauncherMetaReader.Read(mod)?.font?.Trim();
                string root = mod.ContentRoot ?? mod.Folder;
                if (string.IsNullOrEmpty(name) || string.IsNullOrWhiteSpace(root) || !IsPlainName(name)) continue;
                if (!File.Exists(Path.Combine(root, name + ".bundle"))) issues.Add(("missing", mod.DisplayName ?? mod.Id, name, null));
            }
            List<LoaderManifestFont> fonts = FontsOf(mods);
            LoaderManifestFont winner = fonts.LastOrDefault();
            foreach (LoaderManifestFont f in fonts)
            {
                if (winner != null && !string.Equals(f.Name, winner.Name, StringComparison.OrdinalIgnoreCase)) issues.Add(("overridden", f.DisplayName, f.Name, winner.Name));
            }
            return issues;
        }

        /// <summary>The font bundle to start the game with: the last declared in load order, or null.</summary>
        public static string ActiveFont(IEnumerable<ModInfo> orderedEnabledMods)
        {
            return FontsOf(orderedEnabledMods).LastOrDefault()?.Name;
        }

        private static bool IsPlainName(string name)
        {
            if (name.Length == 0 || !char.IsAsciiLetterOrDigit(name[0])) return false;
            foreach (char c in name)
            {
                if (!(c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '_' || c == '-')) return false;
            }
            return true;
        }

        private static string LeafName(string folder)
        {
            return Path.GetFileName((folder ?? string.Empty).TrimEnd('/', (char)92));
        }

        private static void AddPatches(LoaderManifest manifest, ModInfo mod, string modRoot, int order)
        {
            List<string> files = PatchFilesOf(modRoot);
            if (files.Count == 0) return;
            LoaderManifestPatchSet set = new LoaderManifestPatchSet { DisplayName = mod.DisplayName ?? mod.Id, Order = order };
            foreach (string file in files)
                set.Files.Add(GamePaths.ToGameVisiblePath(Path.GetFullPath(file)));
            manifest.Patches.Add(set);
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
