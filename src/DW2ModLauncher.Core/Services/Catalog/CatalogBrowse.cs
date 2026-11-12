using System;
using System.Collections.Generic;
using System.Linq;

namespace DW2ModLauncher.Core.Services.Catalog
{
    public enum CatalogSort
    {
        /// <summary>Most Steam subscribers first; entries with no count (null, not zero) come last.</summary>
        MostSubscribers,
        /// <summary>Most downloads of all releases, ever, first; entries with no count (null, not zero) come last.</summary>
        MostDownloads,
        /// <summary>Most recently released first.</summary>
        RecentlyReleased,
        Name
    }

    /// <summary>What the Browse window shows: the catalog's entries narrowed by a search text and a kind, in the chosen order.</summary>
    public static class CatalogBrowse
    {
        /// <param name="text">Words that must each appear in the name, author or description (any case); blank keeps everything.</param>
        /// <param name="kind">One of the catalog's kinds (a mod with several matches any of them), or null for all.</param>
        /// <param name="tag">One of the mods' topic tags (already normalized), or null for all.</param>
        /// <param name="subscribers">Steam's subscriber count for an entry, or null for "none known"; without it every entry has none.</param>
        public static List<CatalogEntry> Apply(IEnumerable<CatalogEntry> entries, string text, string kind, CatalogSort sort, Func<CatalogEntry, long?> subscribers = null, string tag = null)
        {
            subscribers = subscribers ?? (e => null);
            string[] words = (text ?? "").Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            IEnumerable<CatalogEntry> found = entries.Where(e =>
                (kind == null || e.Kinds.Contains(kind)) &&
                (tag == null || e.Tags.Contains(tag)) &&
                words.All(w => Contains(e.Name, w) || Contains(e.Author, w) || Contains(e.ShortDescription, w) || e.Tags.Any(t => Contains(t, w))));
            switch (sort)
            {
                case CatalogSort.MostSubscribers:
                    return found.OrderBy(e => subscribers(e) == null).ThenByDescending(e => subscribers(e) ?? 0).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
                case CatalogSort.MostDownloads:
                    return found.OrderBy(e => e.Downloads == null).ThenByDescending(e => e.Downloads ?? 0).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
                case CatalogSort.RecentlyReleased:
                    return found.OrderByDescending(e => e.PublishedUtc).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
                default:
                    return found.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
            }
        }

        /// <summary>Every tag used in the entries, once each, in alphabetical order: what the tag filter offers.</summary>
        public static List<string> AllTags(IEnumerable<CatalogEntry> entries)
        {
            return entries.SelectMany(e => e.Tags).Distinct(StringComparer.Ordinal).OrderBy(t => t, StringComparer.Ordinal).ToList();
        }

        private static bool Contains(string haystack, string word)
        {
            return haystack != null && haystack.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
