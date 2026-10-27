using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DW2ModLauncher.Core.Models;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>
    /// Translations of one mod that other mods supply (docs/Localization.md, "Translation packs"). A pack is an ordinary mod holding
    /// <c>translations/&lt;language code&gt;/&lt;target mod&gt;/&lt;file&gt;</c>: the folder under the code names the mod it translates, matched
    /// the way a dependency names a mod (id, display name, token or folder name, ignoring case), and the file is in the format of the sidecar
    /// the target would ship itself (<see cref="SettingsSchema"/>, <see cref="Status"/>). Everything is found by convention; no
    /// mod.json or dw2modlauncher.json field is involved. A pack may hold some files, and a file some keys.
    /// Only enabled mods are packs. Their files come back in load order, so a later one overrides an earlier one key by key, and all of
    /// them override the target's own sidecar. Where a language has a base language (<c>pt-BR</c> is <c>pt</c>), a pack's base files
    /// come before its exact ones. Pure logic; no UI.
    /// </summary>
    public static class TranslationPacks
    {
        /// <summary>The folder of a pack that holds its languages.</summary>
        public const string Folder = "translations";

        /// <summary>The kind of file that translates a mod's settings dialog (the format of <c>settings.schema.&lt;code&gt;.json</c>).</summary>
        public const string SettingsSchema = "settings.schema.json";

        /// <summary>The kind of file that translates a code mod's own in-game text (the format of <c>status.&lt;code&gt;.json</c>).</summary>
        public const string Status = "status.json";

        /// <summary>
        /// The full paths of the files of <paramref name="kind"/> that enabled packs supply for <paramref name="target"/> in
        /// <paramref name="languageCode"/>, in the order they apply (the last wins). Empty when there is nothing for that mod and language.
        /// </summary>
        public static List<string> FilesFor(IEnumerable<ModInfo> orderedEnabled, ModInfo target, string kind, string languageCode)
        {
            List<string> files = new List<string>();
            if (target == null || string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(languageCode)) return files;
            List<string> codes = CodesFor(languageCode.Trim());
            foreach (ModInfo pack in orderedEnabled ?? new List<ModInfo>())
            {
                string root = pack?.ContentRoot ?? pack?.Folder;
                string languages = string.IsNullOrWhiteSpace(root) ? null : Path.Combine(root, Folder);
                if (languages == null || !Directory.Exists(languages)) continue;
                foreach (string code in codes)
                {
                    string languageFolder = ChildNamed(languages, code);
                    if (languageFolder == null) continue;
                    foreach (string modFolder in Directory.GetDirectories(languageFolder).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                    {
                        if (ModDependencies.Find(new[] { target }, Path.GetFileName(modFolder)) != target) continue;
                        string file = ChildNamed(modFolder, kind);
                        if (file != null && File.Exists(file)) files.Add(file);
                    }
                }
            }
            return files;
        }

        /// <summary>The language folders to read for a code, base language first: <c>pt-BR</c> gives <c>pt</c>, <c>pt-BR</c>.</summary>
        public static List<string> CodesFor(string languageCode)
        {
            List<string> codes = new List<string>();
            int dash = languageCode.IndexOf('-');
            if (dash > 0) codes.Add(languageCode.Substring(0, dash));
            codes.Add(languageCode);
            return codes;
        }

        /// <summary>The entry of a folder named <paramref name="name"/>, ignoring case (a Linux file system does not), or null.</summary>
        private static string ChildNamed(string folder, string name)
        {
            return Directory.EnumerateFileSystemEntries(folder).FirstOrDefault(e => string.Equals(Path.GetFileName(e), name, StringComparison.OrdinalIgnoreCase));
        }
    }
}
