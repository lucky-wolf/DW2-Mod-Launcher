using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace DW2ModLauncher.Core.Services
{
    // Language packs live as plain JSON files (key -> translated text) in the
    // Languages folder next to the executable. "en.json" is the canonical,
    // always-complete pack that every key must exist in. Any other file
    // (ja.json, or a community-contributed fr.json, etc.) only needs to
    // contain the keys someone has translated so far; anything it is
    // missing is exposed as the raw key name rather than silently
    // falling back to English, so incomplete translations are obvious
    // instead of invisible.
    public static class Localization
    {
        private static readonly object Gate = new object();
        private static Dictionary<string, Dictionary<string, string>> languages;
        private static Dictionary<string, string> displayNames;
        // the packs shipped in the Languages folder, as loaded; installed mods' packs are merged over copies of these
        private static Dictionary<string, Dictionary<string, string>> shipped;
        private static Dictionary<string, string> shippedNames;

        public static string LanguagesDirectory
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Languages"); }
        }

        public static IEnumerable<string> AvailableLanguageCodes()
        {
            EnsureLoaded();
            return languages.Keys.OrderBy(code => code == "en" ? "" : code, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static string DisplayNameFor(string code)
        {
            EnsureLoaded();
            string name;
            return displayNames.TryGetValue(code ?? "", out name) ? name : code;
        }

        public static string Get(string languageCode, string key)
        {
            EnsureLoaded();
            Dictionary<string, string> pack;
            string value;
            if (!string.IsNullOrEmpty(languageCode) && languages.TryGetValue(languageCode, out pack) && pack.TryGetValue(key, out value))
                return value;
            return key;
        }

        /// <summary>
        /// The launcher language for a person whose system speaks <paramref name="culture"/>: the pack named exactly like it (pt-BR), else
        /// the pack for its language (ja for ja-JP), else English.
        /// </summary>
        public static string ForCulture(CultureInfo culture)
        {
            EnsureLoaded();
            if (culture == null) return "en";
            string match = languages.Keys.FirstOrDefault(c => string.Equals(c, culture.Name, StringComparison.OrdinalIgnoreCase))
                ?? languages.Keys.FirstOrDefault(c => string.Equals(c, culture.TwoLetterISOLanguageName, StringComparison.OrdinalIgnoreCase));
            return match ?? "en";
        }

        /// <summary>The language pack whose code or display name is the given text (case-insensitive), else null.</summary>
        public static string CodeFor(string text)
        {
            EnsureLoaded();
            string typed = (text ?? "").Trim();
            if (typed.Length == 0) return null;
            return languages.Keys.FirstOrDefault(c => string.Equals(c, typed, StringComparison.OrdinalIgnoreCase)
                || string.Equals(displayNames.TryGetValue(c, out string name) ? name : null, typed, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Every key of the canonical English pack that starts with <paramref name="prefix"/>, with its text in the given language.
        /// A key the language's pack lacks comes back as the raw key name, like <see cref="Get"/>.
        /// </summary>
        public static Dictionary<string, string> Prefixed(string languageCode, string prefix)
        {
            EnsureLoaded();
            Dictionary<string, string> result = new Dictionary<string, string>();
            foreach (string key in languages["en"].Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)))
                result[key] = Get(languageCode, key);
            return result;
        }

        /// <summary>
        /// Adds the launcher languages that installed mods supply (<see cref="InstalledLanguagePacks"/>) to the packs shipped with the launcher, replacing
        /// what an earlier call added. Call it before the language is chosen, so a saved language that only a mod supplies is still there. A key a mod's file
        /// replaces is logged; a file that cannot be used is returned for the caller to show, and adds nothing.
        /// </summary>
        public static List<InstalledLanguagePacks.Problem> UseInstalledPacks(string managedModsRoot, string workshopRoot)
        {
            EnsureLoaded();
            lock (Gate)
            {
                Dictionary<string, Dictionary<string, string>> merged = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
                foreach (KeyValuePair<string, Dictionary<string, string>> pack in shipped)
                    merged[pack.Key] = new Dictionary<string, string>(pack.Value, StringComparer.Ordinal);
                Dictionary<string, string> names = new Dictionary<string, string>(shippedNames, StringComparer.OrdinalIgnoreCase);
                List<string> replaced = new List<string>();
                List<InstalledLanguagePacks.Problem> problems = InstalledLanguagePacks.Merge(merged, names, InstalledLanguagePacks.Find(managedModsRoot, workshopRoot), replaced);
                foreach (string line in replaced) Diagnostics.Logger.Log("Installed language packs", "A mod's launcher.json replaces " + line);
                foreach (InstalledLanguagePacks.Problem problem in problems) Diagnostics.Logger.Log("Installed language packs", "Not used: " + problem.File + ": " + problem.Message);
                languages = merged;
                displayNames = names;
                return problems;
            }
        }

        private static void EnsureLoaded()
        {
            if (languages != null) return;
            lock (Gate)
            {
                if (languages != null) return;
                Dictionary<string, Dictionary<string, string>> loaded = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
                Dictionary<string, string> names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                string dir = LanguagesDirectory;
                if (Directory.Exists(dir))
                {
                    foreach (string file in Directory.GetFiles(dir, "*.json"))
                    {
                        string code = Path.GetFileNameWithoutExtension(file);
                        if (string.IsNullOrWhiteSpace(code)) continue;
                        try
                        {
                            Dictionary<string, string> pack = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file, Encoding.UTF8));
                            if (pack == null) continue;
                            string displayName;
                            pack.TryGetValue("_displayName", out displayName);
                            pack.Remove("_displayName");
                            loaded[code] = pack;
                            names[code] = string.IsNullOrWhiteSpace(displayName) ? code : displayName;
                        }
                        catch (Exception ex) when (ex is JsonException || ex is IOException || ex is UnauthorizedAccessException)
                        {
                            Diagnostics.Logger.LogException("Load language pack: " + file, ex);
                        }
                    }
                }
                if (!loaded.ContainsKey("en")) loaded["en"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                languages = loaded;
                displayNames = names;
                shipped = loaded.ToDictionary(p => p.Key, p => new Dictionary<string, string>(p.Value, StringComparer.Ordinal), StringComparer.OrdinalIgnoreCase);
                shippedNames = new Dictionary<string, string>(names, StringComparer.OrdinalIgnoreCase);
            }
        }
    }
}
