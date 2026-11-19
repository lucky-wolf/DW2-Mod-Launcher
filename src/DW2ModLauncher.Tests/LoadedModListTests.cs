extern alias loader;
using System;
using System.Collections.Generic;
using loader::DW2ModLauncher.Loader;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class LoadedModListTests
    {
        private static LoadedModList Sample() => new LoadedModList(new List<LoaderManifestInstalledMod>
        {
            new LoaderManifestInstalledMod { Name = "Advanced Construction", Uid = "0b7c6f0e-1d2a-4c3b-8e9f-a1b2c3d4e5f6", WorkshopId = "3012345678", HasCode = true },
            new LoaderManifestInstalledMod { Name = "Data Only" }
        });

        [Fact]
        public void FindsByNameIgnoringCase()
        {
            LoadedModList list = Sample();
            Assert.True(list.IsLoaded("advanced construction"));
            Assert.True(list.IsLoaded("Data Only"));
            Assert.False(list.IsLoaded("Other Mod"));
        }

        [Fact]
        public void FindsByUidInAnySpelling()
        {
            LoadedModList list = Sample();
            Assert.True(list.IsLoadedByUid("0B7C6F0E-1D2A-4C3B-8E9F-A1B2C3D4E5F6"));
            Assert.True(list.IsLoadedByUid("0b7c6f0e1d2a4c3b8e9fa1b2c3d4e5f6"));
            Assert.False(list.IsLoadedByUid("11111111-1111-1111-1111-111111111111"));
        }

        [Fact]
        public void FindsByWorkshopId()
        {
            LoadedModList list = Sample();
            Assert.True(list.IsLoadedByWorkshopId("3012345678"));
            Assert.False(list.IsLoadedByWorkshopId("42"));
        }

        [Fact]
        public void ModWithoutUidOrWorkshopIdNeverMatchesThem()
        {
            LoadedModList list = Sample();
            Assert.Single(list.Uids());
            Assert.Equal(new[] { "Advanced Construction", "Data Only" }, list.Names());
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("  ")]
        public void BlankNameIsAnError(string name)
        {
            Assert.Throws<ArgumentException>(() => Sample().IsLoaded(name));
        }

        [Theory]
        [InlineData("not-a-uuid")]
        [InlineData("")]
        public void BadUidIsAnErrorNotNo(string uid)
        {
            Assert.Throws<ArgumentException>(() => Sample().IsLoadedByUid(uid));
        }

        [Theory]
        [InlineData("steam/123")]
        [InlineData("")]
        [InlineData("12a")]
        public void BadWorkshopIdIsAnErrorNotNo(string id)
        {
            Assert.Throws<ArgumentException>(() => Sample().IsLoadedByWorkshopId(id));
        }
    }
}
