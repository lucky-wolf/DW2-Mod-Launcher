using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using DW2ModLauncher.Core.Models;
using DW2ModLauncher.Core.Services;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class ModChannelsTests : IDisposable
    {
        private const string Channel = "coherent-research.profiles";
        private readonly string dir = Path.Combine(Path.GetTempPath(), "dw2-channels-" + Guid.NewGuid().ToString("N"));

        public ModChannelsTests() { Directory.CreateDirectory(dir); }
        public void Dispose() { try { Directory.Delete(dir, true); } catch (IOException) { } }

        private ModInfo MakeMod(string id, params (string rel, string content)[] files)
        {
            string folder = Path.Combine(dir, id);
            Directory.CreateDirectory(folder);
            foreach ((string rel, string content) in files)
            {
                string full = Path.Combine(folder, rel.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(full));
                File.WriteAllText(full, content);
            }
            return new ModInfo { Id = id, DisplayName = id, Folder = folder, ContentRoot = folder, ActiveToken = "mods/" + id };
        }

        private static ModInfo Provides(ModInfo mod, string channel, params (string slot, string path)[] files)
        {
            mod.Provides.Add(new LauncherProvision { channel = channel, files = files.ToDictionary(f => f.slot, f => f.path) });
            return mod;
        }

        private static ModInfo Receives(ModInfo mod, params string[] channels)
        {
            mod.Receives.AddRange(channels);
            return mod;
        }

        [Fact]
        public void OneProvider_ItsFileArrivesUnderChannelAndSlot_WithoutComments()
        {
            ModInfo xl = Provides(MakeMod("xl", ("p/x.json", "{ // comment\n \"version\": 1, \"A\": { \"b\": 2 }, }")), Channel, ("x.json", "p/x.json"));
            ModInfo cr = Receives(MakeMod("cr"), Channel);

            Dictionary<string, string> files = ModChannels.FilesFor(cr, new[] { xl, cr });

            JsonNode got = JsonNode.Parse(files[Channel + "/x.json"]);
            Assert.Equal(1, (int)got["version"]);
            Assert.Equal(2, (int)got["A"]["b"]);
            Assert.Empty(ModChannels.Problems(new[] { xl, cr }));
        }

        [Fact]
        public void LaterProviderAugmentsAndOverrides_ByMergePatch()
        {
            ModInfo xl = Provides(MakeMod("xl", ("x.json", "{\"version\":1,\"Races\":{\"Teekan\":{\"gamma\":1,\"rho\":2},\"Mortalen\":{\"gamma\":3}},\"list\":[1,2]}")), Channel, ("x.json", "x.json"));
            ModInfo overhaul = Provides(MakeMod("overhaul", ("x.json", "{\"version\":1,\"Races\":{\"Teekan\":{\"gamma\":9,\"rho\":null},\"NewRace\":{\"gamma\":5}},\"list\":[7]}")), Channel, ("x.json", "x.json"));
            ModInfo cr = Receives(MakeMod("cr"), Channel);

            JsonNode got = JsonNode.Parse(ModChannels.FilesFor(cr, new[] { xl, overhaul, cr })[Channel + "/x.json"]);

            Assert.Equal(9, (int)got["Races"]["Teekan"]["gamma"]);
            Assert.Null(got["Races"]["Teekan"]["rho"]);
            Assert.Equal(3, (int)got["Races"]["Mortalen"]["gamma"]);
            Assert.Equal(5, (int)got["Races"]["NewRace"]["gamma"]);
            Assert.Equal(new[] { 7 }, got["list"].AsArray().Select(n => (int)n));
        }

        [Fact]
        public void TheOrderGivenDecidesWhoWins()
        {
            ModInfo a = Provides(MakeMod("a", ("x.json", "{\"v\":\"a\"}")), Channel, ("x.json", "x.json"));
            ModInfo b = Provides(MakeMod("b", ("x.json", "{\"v\":\"b\"}")), Channel, ("x.json", "x.json"));
            ModInfo cr = Receives(MakeMod("cr"), Channel);

            Assert.Equal("b", (string)JsonNode.Parse(ModChannels.FilesFor(cr, new[] { a, b, cr })[Channel + "/x.json"])["v"]);
            Assert.Equal("a", (string)JsonNode.Parse(ModChannels.FilesFor(cr, new[] { b, a, cr })[Channel + "/x.json"])["v"]);
        }

        [Fact]
        public void NonJsonSlot_IsReplacedAsAWhole()
        {
            ModInfo a = Provides(MakeMod("a", ("t.txt", "first")), Channel, ("notes.txt", "t.txt"));
            ModInfo b = Provides(MakeMod("b", ("t.txt", "second")), Channel, ("notes.txt", "t.txt"));
            ModInfo cr = Receives(MakeMod("cr"), Channel);

            Assert.Equal("second", ModChannels.FilesFor(cr, new[] { a, b, cr })[Channel + "/notes.txt"]);
        }

        [Fact]
        public void OnlyTheChannelsADllReceives_AndNothingWithoutAProvider()
        {
            ModInfo xl = Provides(MakeMod("xl", ("x.json", "{}")), "other.channel", ("x.json", "x.json"));
            ModInfo cr = Receives(MakeMod("cr"), Channel);

            Assert.Empty(ModChannels.FilesFor(cr, new[] { xl, cr }));
            Assert.Empty(ModChannels.FilesFor(MakeMod("plain"), new[] { xl }));
        }

        [Fact]
        public void ChannelNamesMatchIgnoringCase()
        {
            ModInfo xl = Provides(MakeMod("xl", ("x.json", "{}")), "Coherent-Research.Profiles", ("x.json", "x.json"));
            ModInfo cr = Receives(MakeMod("cr"), Channel);

            Assert.Single(ModChannels.FilesFor(cr, new[] { xl, cr }));
        }

        [Fact]
        public void BadFiles_AreReportedAndLeftOut_TheRestStillMerge()
        {
            ModInfo bad = Provides(MakeMod("bad", ("ok.json", "{\"a\":1}"), ("big.txt", new string('x', ModChannels.MaxFileBytes + 1)), ("broken.json", "{ nope")),
                Channel, ("ok.json", "ok.json"), ("outside.json", "../secret.json"), ("missing.json", "missing.json"), ("big.txt", "big.txt"),
                ("broken.json", "broken.json"), ("rooted.json", Path.Combine(dir, "bad", "ok.json")), ("none.json", ""));
            File.WriteAllText(Path.Combine(dir, "secret.json"), "{}");
            ModInfo cr = Receives(MakeMod("cr"), Channel);

            Dictionary<string, string> files = ModChannels.FilesFor(cr, new[] { bad, cr });
            List<string> problems = ModChannels.Problems(new[] { bad, cr });

            Assert.Equal(new[] { Channel + "/ok.json" }, files.Keys);
            Assert.Equal(6, problems.Count);
        }

        [Fact]
        public void BytesThatAreNotUtf8_AreReported()
        {
            ModInfo xl = Provides(MakeMod("xl"), Channel, ("b.dat", "b.dat"));
            File.WriteAllBytes(Path.Combine(xl.Folder, "b.dat"), new byte[] { 0xFF, 0xFE, 0x00, 0xC3, 0x28 });

            Assert.Single(ModChannels.Problems(new[] { xl }));
        }

        [Fact]
        public void ProvidersDisagreeingOnVersion_IsReported()
        {
            ModInfo a = Provides(MakeMod("a", ("x.json", "{\"version\":1}")), Channel, ("x.json", "x.json"));
            ModInfo b = Provides(MakeMod("b", ("x.json", "{\"version\":2}")), Channel, ("x.json", "x.json"));

            List<string> problems = ModChannels.Problems(new[] { a, b });

            Assert.Single(problems);
            Assert.Contains("version", problems[0]);
        }

        [Fact]
        public void ProvisionWithNoChannelOrNoFiles_IsReported()
        {
            ModInfo a = MakeMod("a");
            a.Provides.Add(new LauncherProvision { channel = " ", files = new Dictionary<string, string> { ["x"] = "x" } });
            a.Provides.Add(new LauncherProvision { channel = Channel });

            Assert.Equal(2, ModChannels.Problems(new[] { a }).Count);
        }

        [Fact]
        public void Describe_NamesTheProvidersInOrder()
        {
            ModInfo a = Provides(MakeMod("a", ("x.json", "{}")), Channel, ("x.json", "x.json"));
            ModInfo b = Provides(MakeMod("b", ("x.json", "{}")), Channel, ("x.json", "x.json"));

            Assert.Equal(new[] { Channel + "/x.json: a, b" }, ModChannels.Describe(new[] { a, b }));
        }

        [Fact]
        public void Manifest_CarriesTheMergedFilesApartFromTheSettings()
        {
            ModInfo xl = Provides(MakeMod("xl", ("x.json", "{\"a\":1}")), Channel, ("x.json", "x.json"));
            ModInfo cr = MakeMod("cr");
            File.WriteAllText(Path.Combine(cr.Folder, "dw2modlauncher.json"),
                "{ \"injection\": { \"dll\": \"cr.dll\", \"entryPoint\": \"Cr.Entry.Init\" }, \"Receives\": [ \"" + Channel + "\" ] }");
            cr.Receives = LauncherMetaReader.Read(cr).receives;

            LoaderManifest manifest = LoaderManifestBuilder.Build(new List<ModInfo> { xl, cr });

            LoaderManifestEntry entry = Assert.Single(manifest.Entries);
            Assert.Equal(1, (int)JsonNode.Parse(entry.Files[Channel + "/x.json"])["a"]);
            Assert.Null(entry.SettingsJson);
        }

        [Fact]
        public void LauncherMeta_ReadsProvidesAndReceivesIgnoringCase()
        {
            ModInfo xl = MakeMod("xl");
            File.WriteAllText(Path.Combine(xl.Folder, "dw2modlauncher.json"),
                "{ \"Provides\": [ { \"Channel\": \"" + Channel + "\", \"Files\": { \"x.json\": \"p/x.json\" } } ], \"Receives\": [ \"a\", \"b\" ] }");

            LauncherMeta meta = LauncherMetaReader.Read(xl);

            Assert.Equal(Channel, Assert.Single(meta.provides).channel);
            Assert.Equal("p/x.json", meta.provides[0].files["x.json"]);
            Assert.Equal(new[] { "a", "b" }, meta.receives);
        }
    }
}
