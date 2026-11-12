using System;
using System.Collections.Generic;
using System.Linq;
using DW2ModLauncher.Core.Services.Catalog;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class CatalogBrowseTests
    {
        private static CatalogEntry E(string name, string kinds = "code", long? downloads = 1, int day = 1, string author = "Someone", string description = null)
        {
            CatalogEntry entry = new CatalogEntry
            {
                Id = name.Replace(' ', '-'),
                Name = name,
                Author = author,
                ShortDescription = description,
                Downloads = downloads,
                PublishedUtc = new DateTime(2026, 11, day, 0, 0, 0, DateTimeKind.Utc)
            };
            entry.Kinds.AddRange(kinds.Split(','));
            return entry;
        }

        private static string[] Names(IEnumerable<CatalogEntry> entries) { return entries.Select(e => e.Name).ToArray(); }

        [Fact]
        public void MostDownloads_PutsNoCountLast_NotAsZero()
        {
            var entries = new[] { E("XL", "xml", null), E("A", downloads: 2), E("B", downloads: 0), E("C", downloads: 9) };

            Assert.Equal(new[] { "C", "A", "B", "XL" }, Names(CatalogBrowse.Apply(entries, "", null, CatalogSort.MostDownloads)));
        }

        [Fact]
        public void MostSubscribers_UsesSteamsCount_AndPutsUnknownLast()
        {
            var entries = new[] { E("A", downloads: 50), E("B", downloads: 1), E("C", downloads: null) };
            var steam = new System.Collections.Generic.Dictionary<string, long> { { "C", 900 }, { "B", 20 } };

            var sorted = CatalogBrowse.Apply(entries, "", null, CatalogSort.MostSubscribers, e => steam.TryGetValue(e.Id, out long n) ? n : (long?)null);

            Assert.Equal(new[] { "C", "B", "A" }, Names(sorted));
        }

        [Fact]
        public void RecentlyReleased_NewestFirst()
        {
            var entries = new[] { E("Old", day: 1), E("New", day: 9), E("Mid", day: 5) };

            Assert.Equal(new[] { "New", "Mid", "Old" }, Names(CatalogBrowse.Apply(entries, null, null, CatalogSort.RecentlyReleased)));
        }

        [Fact]
        public void Name_IgnoresCase()
        {
            var entries = new[] { E("beta"), E("Alpha"), E("Gamma") };

            Assert.Equal(new[] { "Alpha", "beta", "Gamma" }, Names(CatalogBrowse.Apply(entries, "", null, CatalogSort.Name)));
        }

        [Fact]
        public void Search_EveryWordMustMatchNameAuthorOrDescription()
        {
            var entries = new[]
            {
                E("Troop Logistics", author: "Mordachai", description: "Moves troops"),
                E("Music Box", author: "Mordachai", description: "Plays music"),
                E("Other", author: "Someone", description: "Troops too")
            };

            Assert.Equal(new[] { "Troop Logistics" }, Names(CatalogBrowse.Apply(entries, "TROOP mordachai", null, CatalogSort.Name)));
            Assert.Equal(new[] { "Music Box", "Troop Logistics" }, Names(CatalogBrowse.Apply(entries, "mordachai", null, CatalogSort.Name)));
            Assert.Empty(CatalogBrowse.Apply(entries, "nothing like this", null, CatalogSort.Name));
        }

        [Fact]
        public void Tag_NarrowsTheList_AndSearchFindsTags()
        {
            CatalogEntry a = E("A"); a.Tags.AddRange(new[] { "music", "sound" });
            CatalogEntry b = E("B"); b.Tags.Add("art");
            CatalogEntry c = E("C");
            var entries = new[] { a, b, c };

            Assert.Equal(new[] { "A" }, Names(CatalogBrowse.Apply(entries, "", null, CatalogSort.Name, tag: "music")));
            Assert.Equal(new[] { "B" }, Names(CatalogBrowse.Apply(entries, "art", null, CatalogSort.Name)));
            Assert.Equal(new[] { "art", "music", "sound" }, CatalogBrowse.AllTags(entries));
        }

        [Fact]
        public void Tags_NormalizeCaseAndSpacing()
        {
            Assert.Equal(new[] { "sound effects", "art" }, CatalogTags.Normalize(new[] { "  Sound   Effects ", "ART", "art", " " }));
        }

        [Fact]
        public void Kind_NarrowsTheList()
        {
            var entries = new[] { E("A", "code"), E("B", "translation"), E("C", "xml,translation"), E("D", "code,xml") };

            Assert.Equal(new[] { "B", "C" }, Names(CatalogBrowse.Apply(entries, "", "translation", CatalogSort.Name)));
            // a mod of several kinds is listed under each of them
            Assert.Equal(new[] { "C", "D" }, Names(CatalogBrowse.Apply(entries, "", "xml", CatalogSort.Name)));
        }
    }
}
