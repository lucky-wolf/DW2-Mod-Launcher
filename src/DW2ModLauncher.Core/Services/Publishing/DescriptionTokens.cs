using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace DW2ModLauncher.Core.Services.Publishing
{
    /// <summary>
    /// Placeholders in a description file that are filled in as the description is sent to Steam: {version} (mod.json's version),
    /// {date} (the publish date, yyyy-MM-dd) and {changes} (the latest changelog.txt entries as a BBCode bullet list, see
    /// <see cref="ModChangelog.LatestChanges"/>). The file itself is never rewritten. A {word} that is not one of these is an error, not text.
    /// </summary>
    public static class DescriptionTokens
    {
        private static readonly Regex Token = new Regex(@"\{([A-Za-z_][A-Za-z0-9_]*)\}", RegexOptions.Compiled);

        /// <summary>Whether the text has any placeholder, so what Steam gets changes with the version and date even when the file does not.</summary>
        public static bool HasTokens(string text) { return !string.IsNullOrEmpty(text) && Token.IsMatch(text); }

        /// <summary>The changelog entries as a BBCode list.</summary>
        private static string ChangesList(IReadOnlyList<string> changes)
        {
            return "[list]\n" + string.Join("\n", changes.Select(c => "[*]" + c)) + "\n[/list]";
        }

        /// <param name="changes">The entries {changes} lists; a description that uses {changes} with none is an error.</param>
        public static string Expand(string text, string version, DateTime date, IReadOnlyList<string> changes = null)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return Token.Replace(text, m =>
            {
                switch (m.Groups[1].Value.ToLowerInvariant())
                {
                    case "version": return version ?? "";
                    case "date": return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    case "changes":
                        if (changes == null || changes.Count == 0)
                            throw new FormatException("The description has {changes}, but changelog.txt has no entries to list (add lines under Unreleased).");
                        return ChangesList(changes);
                    default: throw new FormatException("The description has the placeholder " + m.Value + ", which is not known. Use {version}, {date} or {changes}.");
                }
            });
        }
    }
}
