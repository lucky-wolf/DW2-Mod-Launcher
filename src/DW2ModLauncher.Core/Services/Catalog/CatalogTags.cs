using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace DW2ModLauncher.Core.Services.Catalog
{
    /// <summary>
    /// The rule for a mod's topic tags (music, art, icons ...): free-form, but written one way so "Music", " music " and "MUSIC" are one tag.
    /// A tag is lower case with the white space in it collapsed to single spaces; an empty tag is dropped and a repeat is listed once.
    /// The release packager applies this rule when it writes the manifest.
    /// </summary>
    public static class CatalogTags
    {
        /// <summary>
        /// A starting vocabulary the tag picker always offers, so authors converge on the same words before the catalog has any. Not a
        /// restriction: any tag can be added. Already in the form <see cref="Normalize(string)"/> gives.
        /// </summary>
        public static readonly string[] Suggested =
        {
            "ai", "art", "balance", "diplomacy", "economy", "events", "galaxy", "icons", "music", "planets", "quality of life",
            "races", "research", "ships", "sound effects", "ui", "weapons"
        };

        public static string Normalize(string tag)
        {
            return Regex.Replace((tag ?? "").Trim(), @"\s+", " ").ToLowerInvariant();
        }

        public static List<string> Normalize(IEnumerable<string> tags)
        {
            return (tags ?? Enumerable.Empty<string>()).Select(Normalize).Where(t => t.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        }
    }
}
