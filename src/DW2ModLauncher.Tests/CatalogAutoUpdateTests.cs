using System.Collections.Generic;
using System.IO;
using DW2ModLauncher.Core.Models;
using DW2ModLauncher.Core.Services.Catalog;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class CatalogAutoUpdateTests
    {
        private const string Uid = "6F1C2A3E-8B4D-4E5F-9A60-7B1D2C3E4F50";

        private static CatalogIndex Index(string version, string minLauncher = null)
        {
            CatalogIndex index = new CatalogIndex();
            index.Entries.Add(new CatalogEntry { Id = "a", Uid = Uid, Name = "A", Version = version, Author = "someone", MinLauncherVersion = minLauncher });
            return index;
        }

        private static ModInfo Installed(string root, string author, string version = "1.0.0")
        {
            string folder = Path.Combine(root, "A");
            return new ModInfo { Id = "A", Folder = folder, Uid = Uid, Author = author, Version = version, IsWorkshop = false };
        }

        [Fact]
        public void NewerReleaseOfSomeoneElsesModIsPending()
        {
            string root = Path.GetFullPath("mods-test");
            List<CatalogPendingUpdate> found = CatalogAutoUpdate.Find(Index("1.1.0"), new[] { Installed(root, "someone") }, root, "me", "9.0.0");
            Assert.Single(found);
        }

        [Fact]
        public void PlayersOwnModIsNeverUpdated()
        {
            string root = Path.GetFullPath("mods-test");
            Assert.Empty(CatalogAutoUpdate.Find(Index("1.1.0"), new[] { Installed(root, "me") }, root, "ME", "9.0.0"));
            Assert.Empty(CatalogAutoUpdate.Find(Index("1.1.0"), new[] { Installed(root, "") }, root, "me", "9.0.0"));
        }

        [Fact]
        public void CurrentModAndTooOldLauncherGiveNothing()
        {
            string root = Path.GetFullPath("mods-test");
            Assert.Empty(CatalogAutoUpdate.Find(Index("1.0.0"), new[] { Installed(root, "someone") }, root, "me", "9.0.0"));
            Assert.Empty(CatalogAutoUpdate.Find(Index("1.1.0", "99.0.0"), new[] { Installed(root, "someone") }, root, "me", "9.0.0"));
        }

        [Fact]
        public void OnlyOthersLocalModsAreCandidates()
        {
            string root = Path.GetFullPath("mods-test");
            Assert.True(CatalogAutoUpdate.HasCandidates(new[] { Installed(root, "someone") }, "me"));
            Assert.False(CatalogAutoUpdate.HasCandidates(new[] { Installed(root, "me") }, "me"));
        }
    }
}
