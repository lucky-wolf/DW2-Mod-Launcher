using System;
using System.Collections.Generic;
using DW2ModLauncher.Core.Models;
using DW2ModLauncher.Core.Services.Catalog;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class CatalogTests
    {
        private const string Sha = "404ca7420cc397a08d34ec6473fc53b31571624ee3e8def4861a683d61325657";

        private const string Uid = "6f1c2a3e-8b4d-4e5f-9a60-7b1d2c3e4f50";

        private static string Entry(string extra = "", string id = "Diplomatic-Corps", string kinds = "\"code\"", string sha = Sha, string size = "419556",
            string url = "https://github.com/lucky-wolf/Distant-Worlds-2-Mods/releases/download/Diplomatic-Corps-v1.0.15/Diplomatic-Corps-v1.0.15.zip", string uid = Uid)
        {
            return "{\"id\":\"" + id + "\",\"uid\":\"" + uid + "\",\"name\":\"Diplomatic Corps\",\"kinds\":[" + kinds + "],\"version\":\"1.0.15\",\"author\":\"Mordachai\","
                + "\"shortDescription\":\"Diplomacy you control.\",\"workshopId\":3829180757,\"minLauncherVersion\":\"1.0.30\","
                + "\"dependencies\":[{\"mod\":\"Settings Keeper\",\"workshopId\":3824847512,\"required\":false}],"
                + "\"size\":" + size + ",\"sha256\":\"" + sha + "\",\"downloadUrl\":\"" + url + "\","
                + "\"releaseUrl\":\"https://github.com/lucky-wolf/Distant-Worlds-2-Mods/releases/tag/Diplomatic-Corps-v1.0.15\","
                + "\"publishedAt\":\"2026-11-12T15:42:16Z\",\"downloads\":3" + extra + "}";
        }

        private static string Index(params string[] entries)
        {
            return "{\"schema\":1,\"generated\":\"2026-11-12T20:00:00Z\",\"mods\":[" + string.Join(",", entries) + "]}";
        }

        [Fact]
        public void Parse_ReadsEveryField()
        {
            CatalogIndex index = CatalogParser.Parse(Index(Entry()));

            CatalogEntry e = Assert.Single(index.Entries);
            Assert.Equal("Diplomatic-Corps", e.Id);
            Assert.Equal(Uid, e.Uid);
            Assert.Equal("Diplomatic Corps", e.Name);
            Assert.Equal(new[] { "code" }, e.Kinds);
            Assert.Equal("1.0.15", e.Version);
            Assert.Equal("Mordachai", e.Author);
            Assert.Equal("3829180757", e.WorkshopId);
            Assert.Equal("1.0.30", e.MinLauncherVersion);
            Assert.Equal(419556, e.Size);
            Assert.Equal(3, e.Downloads);
            Assert.Equal(new DateTime(2026, 11, 12, 15, 42, 16, DateTimeKind.Utc), e.PublishedUtc.ToUniversalTime());
            CatalogDependency dep = Assert.Single(e.Dependencies);
            Assert.Equal("Settings Keeper", dep.Mod);
            Assert.Equal("3824847512", dep.WorkshopId);
            Assert.False(dep.Required);
        }

        [Fact]
        public void Parse_NullDownloads_MeansTheHostPublishesNoCount()
        {
            CatalogIndex index = CatalogParser.Parse(Index(Entry(",\"downloads\":null").Replace("\"downloads\":3,", "")));

            Assert.Null(index.Entries[0].Downloads);
        }

        [Fact]
        public void Parse_UppercaseHash_IsLowered()
        {
            CatalogIndex index = CatalogParser.Parse(Index(Entry(sha: Sha.ToUpperInvariant())));

            Assert.Equal(Sha, index.Entries[0].Sha256);
        }

        [Theory]
        [InlineData("{\"schema\":2,\"generated\":\"2026-11-12T20:00:00Z\",\"mods\":[]}", "schema 2")]
        [InlineData("{\"schema\":1,\"generated\":\"2026-11-12T20:00:00Z\"}", "no \"mods\" list")]
        [InlineData("[]", "not a JSON object")]
        [InlineData("{not json", "not valid JSON")]
        public void Parse_BadWrapper_SaysWhy(string json, string expected)
        {
            FormatException error = Assert.Throws<FormatException>(() => CatalogParser.Parse(json));

            Assert.Contains(expected, error.Message);
        }

        [Fact]
        public void Parse_UnknownKind_NamesTheEntry()
        {
            FormatException error = Assert.Throws<FormatException>(() => CatalogParser.Parse(Index(Entry(kinds: "\"skin\""))));

            Assert.Contains("Diplomatic-Corps", error.Message);
            Assert.Contains("kind \"skin\"", error.Message);
        }

        [Fact]
        public void Parse_ModWithSeveralKinds_KeepsThemAll()
        {
            CatalogIndex index = CatalogParser.Parse(Index(Entry(kinds: "\"code\",\"xml\",\"translation\"")));

            Assert.Equal(new[] { "code", "xml", "translation" }, index.Entries[0].Kinds);
        }

        [Fact]
        public void Parse_Tags_AreWrittenOneWay()
        {
            CatalogIndex index = CatalogParser.Parse(Index(Entry(",\"tags\":[\" Music \",\"sound   effects\",\"MUSIC\",\"\"]")));

            Assert.Equal(new[] { "music", "sound effects" }, index.Entries[0].Tags);
        }

        [Fact]
        public void Parse_NoTags_MeansNone()
        {
            Assert.Empty(CatalogParser.Parse(Index(Entry())).Entries[0].Tags);
        }

        [Fact]
        public void Parse_TagThatIsNotText_NamesTheEntry()
        {
            FormatException error = Assert.Throws<FormatException>(() => CatalogParser.Parse(Index(Entry(",\"tags\":[3]"))));

            Assert.Contains("Diplomatic-Corps", error.Message);
        }

        [Fact]
        public void Parse_NoKinds_IsRejected()
        {
            FormatException error = Assert.Throws<FormatException>(() => CatalogParser.Parse(Index(Entry(kinds: ""))));

            Assert.Contains("Diplomatic-Corps", error.Message);
            Assert.Contains("kinds", error.Message);
        }

        [Fact]
        public void Parse_ShortHash_IsRejected()
        {
            FormatException error = Assert.Throws<FormatException>(() => CatalogParser.Parse(Index(Entry(sha: "abc123"))));

            Assert.Contains("sha256", error.Message);
        }

        [Fact]
        public void Parse_NonHttpsDownload_IsRejected()
        {
            FormatException error = Assert.Throws<FormatException>(() => CatalogParser.Parse(Index(Entry(url: "http://example.com/a.zip"))));

            Assert.Contains("https", error.Message);
        }

        [Fact]
        public void Parse_ZeroSize_IsRejected()
        {
            Assert.Throws<FormatException>(() => CatalogParser.Parse(Index(Entry(size: "0"))));
        }

        [Fact]
        public void Parse_MissingName_IsRejected()
        {
            string noName = Entry().Replace("\"name\":\"Diplomatic Corps\",", "");

            FormatException error = Assert.Throws<FormatException>(() => CatalogParser.Parse(Index(noName)));

            Assert.Contains("\"name\"", error.Message);
        }

        [Fact]
        public void Parse_DuplicateId_IsRejected()
        {
            FormatException error = Assert.Throws<FormatException>(() => CatalogParser.Parse(Index(Entry(), Entry())));

            Assert.Contains("twice", error.Message);
        }

        // ---- matching what is installed

        private static CatalogEntry Wanted(string workshopId = "3829180757", string version = "1.0.15")
        {
            return new CatalogEntry { Id = "Diplomatic-Corps", Uid = Uid, Name = "Diplomatic Corps", Version = version, WorkshopId = workshopId };
        }

        private static ModInfo Local(string id, string version, string workshopId = null, string uid = null)
        {
            return new ModInfo { Id = id, DisplayName = id, Version = version, WorkshopId = workshopId, Uid = uid, IsWorkshop = false };
        }

        [Fact]
        public void Find_NothingInstalled()
        {
            CatalogStatus status = CatalogStatusFinder.Find(Wanted(), new List<ModInfo> { Local("Other", "1.0.0") });

            Assert.Equal(CatalogInstallState.NotInstalled, status.State);
            Assert.Null(status.Installed);
        }

        [Fact]
        public void Find_LocalCopyWithTheSameWorkshopId_OlderMeansUpdate()
        {
            ModInfo mine = Local("Diplomatic Corps", "1.0.14", "3829180757");

            CatalogStatus status = CatalogStatusFinder.Find(Wanted(), new List<ModInfo> { mine });

            Assert.Equal(CatalogInstallState.UpdateAvailable, status.State);
            Assert.Same(mine, status.Installed);
        }

        [Fact]
        public void Find_LocalCopyAtOrAboveTheCatalogVersion_IsUpToDate()
        {
            Assert.Equal(CatalogInstallState.UpToDate, CatalogStatusFinder.Find(Wanted(), new List<ModInfo> { Local("Diplomatic Corps", "1.0.15", "3829180757") }).State);
            Assert.Equal(CatalogInstallState.UpToDate, CatalogStatusFinder.Find(Wanted(), new List<ModInfo> { Local("Diplomatic Corps", "1.1.0", "3829180757") }).State);
        }

        [Fact]
        public void Find_SteamSubscription_WinsOverALocalCopy()
        {
            ModInfo steam = new ModInfo { Id = "3829180757", DisplayName = "Diplomatic Corps", Version = "1.0.1", IsWorkshop = true };
            ModInfo mine = Local("Diplomatic Corps", "1.0.14", "3829180757");

            CatalogStatus status = CatalogStatusFinder.Find(Wanted(), new List<ModInfo> { mine, steam });

            Assert.Equal(CatalogInstallState.FromSteam, status.State);
            Assert.Same(steam, status.Installed);
        }

        [Fact]
        public void Find_WithAWorkshopId_DoesNotMatchByFolderName()
        {
            CatalogStatus status = CatalogStatusFinder.Find(Wanted(), new List<ModInfo> { Local("Diplomatic Corps", "1.0.1", "999") });

            Assert.Equal(CatalogInstallState.NotInstalled, status.State);
        }

        [Fact]
        public void Find_WithoutAWorkshopId_MatchesByFolderName()
        {
            ModInfo mine = Local("diplomatic corps", "1.0.1");

            CatalogStatus status = CatalogStatusFinder.Find(Wanted(workshopId: null), new List<ModInfo> { mine });

            Assert.Equal(CatalogInstallState.UpdateAvailable, status.State);
            Assert.Same(mine, status.Installed);
        }

        [Fact]
        public void Parse_MissingUid_IsRejected()
        {
            string noUid = Entry().Replace("\"uid\":\"" + Uid + "\",", "");

            FormatException error = Assert.Throws<FormatException>(() => CatalogParser.Parse(Index(noUid)));

            Assert.Contains("\"uid\"", error.Message);
            Assert.Contains("Diplomatic-Corps", error.Message);
        }

        [Fact]
        public void Parse_UidThatIsNotAUuid_IsRejected()
        {
            FormatException error = Assert.Throws<FormatException>(() => CatalogParser.Parse(Index(Entry(uid: "diplomatic-corps"))));

            Assert.Contains("not a UUID", error.Message);
        }

        [Fact]
        public void Parse_UidIsKeptInCanonicalForm()
        {
            Assert.Equal(Uid, CatalogParser.Parse(Index(Entry(uid: "{" + Uid.ToUpperInvariant() + "}"))).Entries[0].Uid);
        }

        [Fact]
        public void Parse_MissingAuthor_IsRejected()
        {
            string noAuthor = Entry().Replace("\"author\":\"Mordachai\",", "");

            FormatException error = Assert.Throws<FormatException>(() => CatalogParser.Parse(Index(noAuthor)));

            Assert.Contains("\"author\"", error.Message);
        }

        [Fact]
        public void Parse_TwoEntriesWithOneUid_IsRejected()
        {
            FormatException error = Assert.Throws<FormatException>(() => CatalogParser.Parse(Index(Entry(), Entry(id: "Renamed"))));

            Assert.Contains("uid", error.Message);
        }

        [Fact]
        public void Find_ByUid_IgnoresTheNameAndTheWorkshopId()
        {
            ModInfo renamed = Local("Whatever It Is Called Now", "1.0.14", uid: Uid);

            CatalogStatus status = CatalogStatusFinder.Find(Wanted(), new List<ModInfo> { renamed });

            Assert.Equal(CatalogInstallState.UpdateAvailable, status.State);
            Assert.Same(renamed, status.Installed);
        }

        [Fact]
        public void Find_ByUid_SteamCopyWins()
        {
            ModInfo steam = new ModInfo { Id = "1", DisplayName = "Anything", Version = "1.0.1", Uid = Uid, IsWorkshop = true };

            Assert.Equal(CatalogInstallState.FromSteam, CatalogStatusFinder.Find(Wanted(), new List<ModInfo> { Local("A", "1.0.1", uid: Uid), steam }).State);
        }

        [Fact]
        public void Find_AModWithAnotherUid_IsAnotherMod_EvenWithTheSameWorkshopIdOrName()
        {
            string other = "11111111-2222-4333-8444-555555555555";

            Assert.Equal(CatalogInstallState.NotInstalled, CatalogStatusFinder.Find(Wanted(), new List<ModInfo> { Local("Diplomatic Corps", "1.0.1", "3829180757", other) }).State);
            Assert.Equal(CatalogInstallState.NotInstalled, CatalogStatusFinder.Find(Wanted(workshopId: null), new List<ModInfo> { Local("Diplomatic Corps", "1.0.1", uid: other) }).State);
        }
    }
}
