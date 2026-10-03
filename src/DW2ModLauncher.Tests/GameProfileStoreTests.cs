using System;
using System.Collections.Generic;
using System.IO;
using DW2ModLauncher.Core.Services;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class GameProfileStoreTests : IDisposable
    {
        private readonly string dir = Path.Combine(Path.GetTempPath(), "dw2-test-" + Guid.NewGuid().ToString("N"));

        public GameProfileStoreTests() { Directory.CreateDirectory(dir); }
        public void Dispose() { Directory.Delete(dir, true); }

        [Theory]
        [InlineData("XL (Local)", "XL_20_(Local)")]
        [InlineData("XL Only (Steam)", "XL_20_Only_20_(Steam)")]
        [InlineData("XL", "XL")]
        public void Escape_MatchesTheGamesObservedNames(string name, string escaped)
        {
            Assert.Equal(escaped, GameProfileStore.Escape(name));
            Assert.Equal(name, GameProfileStore.Unescape(escaped));
        }

        [Fact]
        public void ListsExistingGameFiles_AndExistsFindsThem()
        {
            File.WriteAllText(Path.Combine(dir, "mods.XL_20_(Local).json"), "{\"order\":[\"mods/XL\"]}");
            File.WriteAllText(Path.Combine(dir, "mods.None.json"), "{\"order\":[]}");
            File.WriteAllText(Path.Combine(dir, "mods.json"), "{\"order\":[]}");
            GameProfileStore store = new GameProfileStore(dir);

            Assert.Equal(new List<string> { "None", "XL (Local)" }, store.ListNames());
            Assert.True(store.Exists("XL (Local)"));
            Assert.True(store.Exists("xl (local)"));
            Assert.False(store.Exists("XL"));
            Assert.Equal(new List<string> { "mods/XL" }, store.Read("XL (Local)").Order);
        }

        [Fact]
        public void ExistingFileWithDifferentEscaping_IsFoundAndOverwritten_NotDuplicated()
        {
            // the game left a '.' unescaped; we would have written "_2E_"
            File.WriteAllText(Path.Combine(dir, "mods.v1.2.json"), "{\"order\":[]}");
            GameProfileStore store = new GameProfileStore(dir);

            Assert.True(store.Exists("v1.2"));
            store.Write("v1.2", new[] { "mods/A" });
            Assert.Single(store.ListNames());
            Assert.Equal(new List<string> { "mods/A" }, store.Read("v1.2").Order);
        }

        [Fact]
        public void WriteCurrentAndRead_RoundTrip_AndMissingGivesEmpty()
        {
            GameProfileStore store = new GameProfileStore(dir);
            Assert.Equal("", store.ReadCurrent());
            store.WriteCurrent("XL (Local)");
            Assert.Equal("XL (Local)", store.ReadCurrent());
            store.WriteCurrent("");
            Assert.Equal("", store.ReadCurrent());
        }

        [Fact]
        public void Names_AreCaseInsensitive_AndCanonicalNameKeepsTheExistingSpelling()
        {
            GameProfileStore store = new GameProfileStore(dir);
            store.Write("XL (Local)", new[] { "mods/A" });

            Assert.True(store.Exists("xl (LOCAL)"));
            Assert.Equal("XL (Local)", store.CanonicalName("xl (local)"));
            Assert.Equal("Brand New", store.CanonicalName("Brand New"));

            store.Write("xl (local)", new[] { "mods/B" });
            Assert.Single(store.ListNames());
            Assert.Equal(new List<string> { "mods/B" }, store.Read("XL (Local)").Order);
        }

        [Fact]
        public void Rename_MovesTheOrder_AndOverwritesAnExistingTarget()
        {
            GameProfileStore store = new GameProfileStore(dir);
            store.Write("A", new[] { "mods/A" });
            store.Write("B", new[] { "mods/B" });

            store.Rename("A", "C");
            Assert.Equal(new List<string> { "B", "C" }, store.ListNames());
            Assert.Equal(new List<string> { "mods/A" }, store.Read("C").Order);

            store.Rename("C", "B");
            Assert.Equal(new List<string> { "B" }, store.ListNames());
            Assert.Equal(new List<string> { "mods/A" }, store.Read("B").Order);
        }

        [Fact]
        public void Rename_ChangingOnlyTheCase_KeepsTheProfile()
        {
            GameProfileStore store = new GameProfileStore(dir);
            store.Write("xl local", new[] { "mods/A" });

            store.Rename("xl local", "XL Local");

            Assert.Equal(new List<string> { "XL Local" }, store.ListNames());
            Assert.Equal(new List<string> { "mods/A" }, store.Read("XL Local").Order);
        }

        [Fact]
        public void DeleteRemovesTheProfile()
        {
            GameProfileStore store = new GameProfileStore(dir);
            store.Write("A B", new[] { "mods/A" });
            Assert.True(store.Exists("A B"));
            store.Delete("A B");
            Assert.False(store.Exists("A B"));
        }
    }
}
