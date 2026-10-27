using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>
    /// Launcher languages that installed mods supply (docs/Localization.md, "Translation packs"): <c>translations/&lt;code&gt;/launcher.json</c> in
    /// any installed mod, enabled or not, so a player can pick the language before enabling anything. The file has the format of a
    /// <c>Languages/&lt;code&gt;.json</c> pack (key to text, optional <c>_displayName</c>). Pure logic apart from reading the files; no UI.
    /// </summary>
    public static class InstalledLanguagePacks
    {
        /// <summary>The file of a pack that holds a language for the launcher itself.</summary>
        public const string FileName = "launcher.json";

        /// <summary>One language file of one installed mod.</summary>
        public sealed class Pack
        {
            public string Code;
            public string File;
        }

        /// <summary>One file that could not be used, and why.</summary>
        public sealed class Problem
        {
            public string File;
            public string Message;
        }

        /// <summary>
        /// The language files of every mod folder under the two mod roots (either may be blank), in the order they apply: by mod folder name,
        /// then language code. The load order is not known yet when the launcher starts, so this order stands in for it; a later file wins per key.
        /// A Workshop item's content may sit one or two folders down, where its mod.json is.
        /// </summary>
        public static List<Pack> Find(string managedModsRoot, string workshopRoot)
        {
            List<Pack> packs = new List<Pack>();
            foreach (var root in new[] { (Path: managedModsRoot, Workshop: false), (Path: workshopRoot, Workshop: true) })
            {
                if (string.IsNullOrWhiteSpace(root.Path) || !Directory.Exists(root.Path)) continue;
                foreach (string dir in Directory.GetDirectories(root.Path).OrderBy(d => System.IO.Path.GetFileName(d), StringComparer.OrdinalIgnoreCase))
                {
                    string modJson = ModScanner.FindModJson(dir, root.Workshop);
                    string contentRoot = string.IsNullOrWhiteSpace(modJson) ? dir : System.IO.Path.GetDirectoryName(modJson);
                    string translations = System.IO.Path.Combine(contentRoot, TranslationPacks.Folder);
                    if (!Directory.Exists(translations)) continue;
                    foreach (string languageFolder in Directory.GetDirectories(translations).OrderBy(d => System.IO.Path.GetFileName(d), StringComparer.OrdinalIgnoreCase))
                    {
                        string file = Directory.EnumerateFiles(languageFolder).FirstOrDefault(f => string.Equals(System.IO.Path.GetFileName(f), FileName, StringComparison.OrdinalIgnoreCase));
                        if (file != null) packs.Add(new Pack { Code = System.IO.Path.GetFileName(languageFolder), File = file });
                    }
                }
            }
            return packs;
        }

        /// <summary>
        /// Adds the packs' keys to <paramref name="languages"/> (code to key to text), a code the launcher does not have yet becoming a new language
        /// named by the file's <c>_displayName</c> (else its code). A key a language already has is replaced and listed in <paramref name="replaced"/>
        /// as "code: key (file)". A file that cannot be read or is not a JSON object of key to text comes back as a problem and adds nothing.
        /// </summary>
        public static List<Problem> Merge(Dictionary<string, Dictionary<string, string>> languages, Dictionary<string, string> displayNames, IEnumerable<Pack> packs, List<string> replaced)
        {
            List<Problem> problems = new List<Problem>();
            foreach (Pack pack in packs)
            {
                Dictionary<string, string> texts;
                try
                {
                    texts = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(pack.File, Encoding.UTF8));
                    if (texts == null) throw new JsonException("the file holds no JSON object");
                }
                catch (Exception ex) when (ex is JsonException || ex is IOException || ex is UnauthorizedAccessException)
                {
                    problems.Add(new Problem { File = pack.File, Message = ex.Message });
                    continue;
                }
                texts.TryGetValue("_displayName", out string displayName);
                texts.Remove("_displayName");
                if (!languages.TryGetValue(pack.Code, out Dictionary<string, string> language))
                {
                    language = new Dictionary<string, string>(StringComparer.Ordinal);
                    languages[pack.Code] = language;
                    displayNames[pack.Code] = string.IsNullOrWhiteSpace(displayName) ? pack.Code : displayName;
                }
                foreach (KeyValuePair<string, string> text in texts)
                {
                    if (language.ContainsKey(text.Key)) replaced?.Add(pack.Code + ": " + text.Key + " (" + pack.File + ")");
                    language[text.Key] = text.Value;
                }
            }
            return problems;
        }
    }
}
