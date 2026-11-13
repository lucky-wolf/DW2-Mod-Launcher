using System.Collections.Generic;
using System.Linq;
using DW2ModLauncher.Core.Models;
using DW2ModLauncher.Core.Services;
using DW2ModLauncher.Core.Services.Catalog;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class CatalogDependenciesTests
    {
        private const string MainUid = "6f1c2a3e-8b4d-4e5f-9a60-7b1d2c3e4f50";
        private const string KeeperUid = "0b9d3f10-1c2e-4a3b-8c4d-5e6f7a8b9c0d";
        private const string OtherUid = "11111111-2222-4333-8444-555555555555";

        private static CatalogEntry Listing(string name, string uid, string workshopId = null)
        {
            return new CatalogEntry { Id = name, Uid = uid, Name = name, Version = "1.0.0", WorkshopId = workshopId };
        }

        private static CatalogEntry Wanting(params CatalogDependency[] deps)
        {
            CatalogEntry entry = Listing("Diplomatic Corps", MainUid);
            entry.Dependencies.AddRange(deps);
            return entry;
        }

        private static ModInfo Installed(string id, string uid, bool steam = false)
        {
            return new ModInfo { Id = id, DisplayName = id, Version = "1.0.0", Uid = uid, IsWorkshop = steam };
        }

        [Fact]
        public void Missing_ReturnsADependencyNobodyInstalled_WithTheCatalogListing()
        {
            CatalogEntry keeper = Listing("Settings Keeper", KeeperUid);
            CatalogEntry entry = Wanting(new CatalogDependency { Mod = "Settings Keeper", Uid = KeeperUid, Required = true });

            MissingDependency missing = Assert.Single(CatalogDependencies.Missing(entry, new[] { entry, keeper }, new List<ModInfo>()));

            Assert.Same(keeper, missing.Entry);
            Assert.True(missing.Required);
        }

        [Fact]
        public void Missing_IsEmpty_WhenAnInstalledModHasTheUid_WhateverItIsCalled()
        {
            CatalogEntry keeper = Listing("Settings Keeper", KeeperUid);
            CatalogEntry entry = Wanting(new CatalogDependency { Mod = "Settings Keeper", Uid = KeeperUid, Required = true });

            Assert.Empty(CatalogDependencies.Missing(entry, new[] { entry, keeper }, new[] { Installed("Renamed Keeper", KeeperUid) }));
        }

        [Fact]
        public void Missing_AModWithAnotherUid_IsADifferentMod_EvenWithTheSameName()
        {
            CatalogEntry keeper = Listing("Settings Keeper", KeeperUid);
            CatalogEntry entry = Wanting(new CatalogDependency { Mod = "Settings Keeper", Uid = KeeperUid, Required = true });

            Assert.Single(CatalogDependencies.Missing(entry, new[] { entry, keeper }, new[] { Installed("Settings Keeper", OtherUid) }));
        }

        [Fact]
        public void Missing_SubscribedOnSteam_CountsAsInstalled()
        {
            CatalogEntry keeper = Listing("Settings Keeper", KeeperUid);
            CatalogEntry entry = Wanting(new CatalogDependency { Mod = "Settings Keeper", Uid = KeeperUid, Required = true });

            Assert.Empty(CatalogDependencies.Missing(entry, new[] { entry, keeper }, new[] { Installed("3824847512", KeeperUid, steam: true) }));
        }

        [Fact]
        public void Missing_NotInTheCatalog_HasNoListing_AndIsFoundByWorkshopId()
        {
            CatalogEntry entry = Wanting(new CatalogDependency { Mod = "Steam Only", WorkshopId = "999", Required = true });

            MissingDependency missing = Assert.Single(CatalogDependencies.Missing(entry, new[] { entry }, new List<ModInfo>()));
            Assert.Null(missing.Entry);

            ModInfo have = Installed("Steam Only", null);
            have.WorkshopId = "999";
            Assert.Empty(CatalogDependencies.Missing(entry, new[] { entry }, new[] { have }));
        }

        [Fact]
        public void Missing_KeepsOptionalOnesApart()
        {
            CatalogEntry entry = Wanting(new CatalogDependency { Mod = "Extras", Uid = KeeperUid, Required = false });

            Assert.False(Assert.Single(CatalogDependencies.Missing(entry, new[] { entry }, new List<ModInfo>())).Required);
        }

        [Fact]
        public void Parse_ReadsADependencysUid_AndRejectsOneThatIsNotAUuid()
        {
            string json = "{\"schema\":1,\"generated\":\"2026-11-12T20:00:00Z\",\"mods\":[{\"id\":\"A\",\"uid\":\"" + MainUid + "\",\"name\":\"A\",\"kinds\":[\"xml\"],\"version\":\"1.0.0\",\"author\":\"x\","
                + "\"dependencies\":[{\"mod\":\"B\",\"uid\":\"" + KeeperUid.ToUpperInvariant() + "\",\"required\":true}],"
                + "\"size\":1,\"sha256\":\"" + new string('a', 64) + "\",\"downloadUrl\":\"https://example.com/a.zip\",\"publishedAt\":\"2026-11-12T15:42:16Z\"}]}";

            Assert.Equal(KeeperUid, CatalogParser.Parse(json).Entries.Single().Dependencies.Single().Uid);
            System.FormatException ex = Assert.Throws<System.FormatException>(() => CatalogParser.Parse(json.Replace(KeeperUid.ToUpperInvariant(), "nope")));
            Assert.Contains("uid", ex.Message);
        }

        [Fact]
        public void Find_ByUid_WinsOverNameAndWorkshopId()
        {
            ModInfo right = Installed("Whatever", KeeperUid);
            ModInfo namesake = Installed("Settings Keeper", OtherUid);
            LauncherDependency dep = new LauncherDependency { mod = "Settings Keeper", uid = KeeperUid.ToUpperInvariant() };

            Assert.Same(right, ModDependencies.Find(new[] { namesake, right }, dep));
            Assert.Null(ModDependencies.Find(new[] { namesake }, dep));
        }

        [Fact]
        public void Find_ByUid_StillMatchesAModThatPredatesIdentitiesByName()
        {
            ModInfo old = Installed("Settings Keeper", null);

            Assert.Same(old, ModDependencies.Find(new[] { old }, new LauncherDependency { mod = "Settings Keeper", uid = KeeperUid }));
        }
    }
}
