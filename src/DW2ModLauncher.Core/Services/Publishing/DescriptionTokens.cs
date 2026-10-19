using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace DW2ModLauncher.Core.Services.Publishing
{
    /// <summary>
    /// Placeholders in a description file that are filled in as the description is sent to Steam: {version} (mod.json's version) and
    /// {date} (the publish date, yyyy-MM-dd). The file itself is never rewritten. A {word} that is not one of these is an error, not text.
    /// </summary>
    public static class DescriptionTokens
    {
        private static readonly Regex Token = new Regex(@"\{([A-Za-z_][A-Za-z0-9_]*)\}", RegexOptions.Compiled);

        /// <summary>Whether the text has any placeholder, so what Steam gets changes with the version and date even when the file does not.</summary>
        public static bool HasTokens(string text) { return !string.IsNullOrEmpty(text) && Token.IsMatch(text); }

        public static string Expand(string text, string version, DateTime date)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return Token.Replace(text, m =>
            {
                switch (m.Groups[1].Value.ToLowerInvariant())
                {
                    case "version": return version ?? "";
                    case "date": return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    default: throw new FormatException("The description has the placeholder " + m.Value + ", which is not known. Use {version} or {date}.");
                }
            });
        }
    }
}
