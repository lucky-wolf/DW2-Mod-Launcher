using System;
using System.Collections.Generic;
using System.Linq;
using DW2ModLauncher.Core.Models;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>
    /// Which language the in-game widget and Mods menu speak. The game has no language setting of its own: a game is in French
    /// because a translation mod replaces its text, so by default the widget follows the enabled translation mod
    /// (dw2modlauncher.json "language"). The launcher setting WidgetLanguageFromMods = false makes it follow the launcher's own language instead.
    /// </summary>
    public static class WidgetLanguage
    {
        /// <summary>Prefix of the language pack keys the loader uses.</summary>
        public const string KeyPrefix = "Widget";

        /// <summary>
        /// The language pack code to use. From the mods: the last enabled mod in load order that declares a language; English when none does.
        /// <paramref name="unknown"/> is set to the declared code when there is no pack for it (the widget stays English then), else null.
        /// </summary>
        public static string Choose(IEnumerable<ModInfo> orderedEnabledMods, LauncherSettings settings, out string unknown)
        {
            unknown = null;
            if (!settings.WidgetLanguageFromMods) return settings.Language;
            string declared = null;
            foreach (ModInfo mod in orderedEnabledMods ?? Enumerable.Empty<ModInfo>())
            {
                string code = LauncherMetaReader.Read(mod)?.language?.Trim();
                if (!string.IsNullOrEmpty(code)) declared = code;
            }
            if (declared == null) return "en";
            string pack = Localization.AvailableLanguageCodes().FirstOrDefault(c => string.Equals(c, declared, StringComparison.OrdinalIgnoreCase));
            if (pack == null)
            {
                unknown = declared;
                return "en";
            }
            return pack;
        }

        /// <summary>The words to put in the loader manifest for the chosen language; empty for English, which the loader has built in.</summary>
        public static Dictionary<string, string> Strings(string languageCode)
        {
            return string.Equals(languageCode, "en", StringComparison.OrdinalIgnoreCase)
                ? new Dictionary<string, string>()
                : Localization.Prefixed(languageCode, KeyPrefix);
        }
    }
}
