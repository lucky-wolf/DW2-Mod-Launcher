using System;
using System.IO;
using System.Linq;
using DW2ModLauncher.Core.Services;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class MissingModReferencesTests : IDisposable
    {
        private readonly string dir = Path.Combine(Path.GetTempPath(), "dw2-missing-" + Guid.NewGuid().ToString("N"));
        private string Mods { get { return Path.Combine(dir, "mods"); } }
        private string Workshop { get { return Path.Combine(dir, "workshop"); } }
        private string ModsJson { get { return Path.Combine(Mods, "mods.json"); } }

        public MissingModReferencesTests()
        {
            Directory.CreateDirectory(Path.Combine(Mods, "Real"));
            Directory.CreateDirectory(Path.Combine(Workshop, "123"));
        }

        public void Dispose() { try { Directory.Delete(dir, true); } catch (IOException) { } }

        private MissingModReferences.Result Prune(ModOrderState order)
        {
            return MissingModReferences.Prune(new GameProfileStore(Mods), order, ModsJson, Mods, Workshop, dir);
        }

        [Fact]
        public void RemovesEntriesWhoseFolderIsGone_FromModsJsonAndEveryProfile()
        {
            string[] listed = { "mods/Real", "mods/Gone", "steam/123", "steam/999", "something-else" };
            ModOrderStore.Write(ModsJson, listed);
            string profile = Path.Combine(Mods, "mods." + GameProfileStore.Escape("Alt") + ".json");
            ModOrderStore.Write(profile, new[] { "mods/Gone", "mods/Real", "mods/AlsoGone" });
            ModOrderState order = ModOrderStore.Read(ModsJson);

            MissingModReferences.Result result = Prune(order);

            Assert.Equal(new[] { "mods/AlsoGone", "mods/Gone", "steam/999" }, result.Removed);
            Assert.Equal(new[] { "mods/Real", "steam/123", "something-else" }, ModOrderStore.Read(ModsJson).Order);
            Assert.Equal(new[] { "mods/Real" }, ModOrderStore.Read(profile).Order);
            Assert.Equal(ModOrderStore.Read(ModsJson).Order, order.Order);
            Assert.Empty(result.Problems);
        }

        [Fact]
        public void KeepsEverythingWhenNothingIsMissing()
        {
            ModOrderStore.Write(ModsJson, new[] { "mods/Real", "steam/123" });
            string before = File.ReadAllText(ModsJson);

            MissingModReferences.Result result = Prune(ModOrderStore.Read(ModsJson));

            Assert.Empty(result.Removed);
            Assert.Equal(before, File.ReadAllText(ModsJson));
        }

        [Fact]
        public void AFolderThatCannotBeFoundProvesNothing()
        {
            ModOrderStore.Write(ModsJson, new[] { "mods/Gone", "steam/999" });
            Directory.Delete(Workshop, true);

            MissingModReferences.Result result = Prune(ModOrderStore.Read(ModsJson));

            Assert.Equal(new[] { "mods/Gone" }, result.Removed);
            Assert.Equal(new[] { "steam/999" }, ModOrderStore.Read(ModsJson).Order);
        }

        [Fact]
        public void KeepsAPreviousCopyOfTheFileItRewrites()
        {
            ModOrderStore.Write(ModsJson, new[] { "mods/Real", "mods/Gone" });

            Prune(ModOrderStore.Read(ModsJson));

            Assert.Single(Directory.GetFiles(Mods, "mods.json.*.launcher_backup"));
        }

        [Fact]
        public void LeavesAnUnreadableModsJsonAlone()
        {
            File.WriteAllText(ModsJson, "not json");
            string before = File.ReadAllText(ModsJson);

            MissingModReferences.Result result = Prune(ModOrderStore.Read(ModsJson));

            Assert.Empty(result.Removed);
            Assert.Equal(before, File.ReadAllText(ModsJson));
            Assert.Equal(before.Length, File.ReadAllText(ModsJson).Length);
            Assert.DoesNotContain(Directory.GetFiles(Mods), f => f.EndsWith(".launcher_backup"));
        }
    }
}
