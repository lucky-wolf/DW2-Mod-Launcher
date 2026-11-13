using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using DW2ModLauncher.Core.Models;
using DW2ModLauncher.Core.Services.Catalog;
using DW2ModLauncher.Core.Services.Publishing;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class ReleaseTests : IDisposable
    {
        private const string Uid = "6f1c2a3e-8b4d-4e5f-9a60-7b1d2c3e4f50";
        private readonly string root = Path.Combine(Path.GetTempPath(), "dw2-release-" + Guid.NewGuid().ToString("N"));

        public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

        private string MakeMod(string folderName = "Diplomatic Corps", bool withDll = true)
        {
            string mod = Path.Combine(root, "mods", folderName);
            Directory.CreateDirectory(Path.Combine(mod, "data"));
            File.WriteAllText(Path.Combine(mod, "mod.json"), "{ \"displayName\": \"" + folderName + "\", \"version\": \"1.0.15\" }");
            File.WriteAllText(Path.Combine(mod, "data", "x.xml"), "<a/>");
            if (withDll) File.WriteAllText(Path.Combine(mod, folderName + ".dll"), "dll");
            File.WriteAllText(Path.Combine(mod, "preview.png"), "png");
            File.WriteAllText(Path.Combine(mod, "changelog.txt"), "## Unreleased\n\n## 1.0.15 (2026-11-12)\n- Did a thing\n- Did another\n\n## 1.0.14 (2026-11-01)\n- Old\n");
            return mod;
        }

        private static LauncherMeta Meta()
        {
            return new LauncherMeta { uid = Uid, author = "Mordachai", tags = new List<string> { "Diplomacy  AI" }, minLauncherVersion = "1.0.30" };
        }

        private static ModPublishMetadata Metadata()
        {
            return new ModPublishMetadata { DisplayName = "Diplomatic Corps", Version = "1.0.15", ShortDescription = "Diplomacy you control." };
        }

        private ReleasePackage Package(string mod, LauncherMeta meta = null, ModPublishMetadata metadata = null)
        {
            return ReleasePackager.Package(mod, Path.Combine(root, "out"), metadata ?? Metadata(), meta ?? Meta(), "3829180757", new DateTime(2026, 11, 12, 15, 42, 16, DateTimeKind.Utc));
        }

        [Fact]
        public void TagFor_UsesTheFolderNameAndVersion()
        {
            Assert.Equal("Diplomatic-Corps-v1.0.15", ReleasePackager.TagFor("Diplomatic Corps", "1.0.15"));
            Assert.Equal("XL-v1.2", ReleasePackager.TagFor("XL ", " 1.2 "));
            Assert.Throws<InvalidDataException>(() => ReleasePackager.TagFor("Mod", "1 0"));
        }

        [Fact]
        public void Package_WritesTheFilesTheCatalogReads_AndTheManifestParses()
        {
            ReleasePackage package = Package(MakeMod());

            Assert.Equal("Diplomatic-Corps-v1.0.15", package.Tag);
            Assert.Equal(4, package.Files.Count);
            Assert.All(package.Files, f => Assert.True(File.Exists(f)));
            Assert.EndsWith(".preview.png", package.PreviewPath);
            Assert.Equal("- Did a thing\n- Did another\n", File.ReadAllText(package.NotesPath));

            // The catalog's own parser accepts what the packager wrote (with the fields a catalog index adds).
            JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(package.ManifestPath));
            Assert.Equal(Uid, manifest.RootElement.GetProperty("uid").GetString());
            Assert.Equal(new[] { "code", "xml" }, manifest.RootElement.GetProperty("kinds").EnumerateArray().Select(k => k.GetString()));
            Assert.Equal("diplomacy ai", manifest.RootElement.GetProperty("tags")[0].GetString());
            Assert.Equal(3829180757, manifest.RootElement.GetProperty("workshopId").GetInt64());
            Assert.Equal("2026-11-12T15:42:16Z", manifest.RootElement.GetProperty("publishedUtc").GetString());
            Assert.Equal(package.Size, manifest.RootElement.GetProperty("size").GetInt64());
            string sha = File.ReadAllText(package.Sha256Path).Split(' ')[0];
            Assert.Equal(sha, manifest.RootElement.GetProperty("sha256").GetString());
        }

        [Fact]
        public void Package_ZipHoldsOneRootFolder_WithTheModsFiles()
        {
            ReleasePackage package = Package(MakeMod());

            using (ZipArchive zip = ZipFile.OpenRead(package.ZipPath))
            {
                Assert.Equal(new[] { "Diplomatic Corps/Diplomatic Corps.dll", "Diplomatic Corps/changelog.txt", "Diplomatic Corps/data/x.xml", "Diplomatic Corps/mod.json", "Diplomatic Corps/preview.png" },
                    zip.Entries.Select(e => e.FullName).OrderBy(n => n, StringComparer.Ordinal));
            }
        }

        [Fact]
        public void Package_AModWithoutAUidOrAuthor_IsRefusedByName()
        {
            string mod = MakeMod();
            LauncherMeta noUid = Meta(); noUid.uid = null;
            LauncherMeta noAuthor = Meta(); noAuthor.author = " ";

            Assert.Contains("no uid", Assert.Throws<InvalidDataException>(() => Package(mod, noUid)).Message);
            Assert.Contains("no author", Assert.Throws<InvalidDataException>(() => Package(mod, noAuthor)).Message);
        }

        [Fact]
        public void IdentityProblem_ADependencyNeedsAModName()
        {
            LauncherMeta meta = Meta();
            meta.dependencies = new List<LauncherDependency> { new LauncherDependency { workshopId = 5 } };

            Assert.Contains("no \"mod\" name", ReleasePackager.IdentityProblem(meta));
        }

        [Fact]
        public void Package_DependenciesKeepTheirUidAndRequiredFlag()
        {
            LauncherMeta meta = Meta();
            meta.dependencies = new List<LauncherDependency> { new LauncherDependency { mod = "Settings Keeper", uid = "0B9D3F10-1C2E-4A3B-8C4D-5E6F7A8B9C0D", required = true } };
            ReleasePackage package = Package(MakeMod(), meta);

            CatalogEntry entry = CatalogParser.Parse(IndexFor(File.ReadAllText(package.ManifestPath))).Entries.Single();
            CatalogDependency dep = Assert.Single(entry.Dependencies);
            Assert.Equal("0b9d3f10-1c2e-4a3b-8c4d-5e6f7a8b9c0d", dep.Uid);
            Assert.True(dep.Required);
        }

        // The catalog's index.json entry is the manifest plus the release URLs the indexer adds.
        private static string IndexFor(string manifest)
        {
            JsonElement m = JsonDocument.Parse(manifest).RootElement;
            Dictionary<string, object> entry = new Dictionary<string, object>();
            foreach (JsonProperty p in m.EnumerateObject()) entry[p.Name] = p.Value;
            entry["downloadUrl"] = "https://github.com/o/r/releases/download/t/t.zip";
            entry["publishedAt"] = m.GetProperty("publishedUtc").GetString();
            return JsonSerializer.Serialize(new Dictionary<string, object> { ["schema"] = 1, ["generated"] = "2026-11-12T20:00:00Z", ["mods"] = new[] { entry } });
        }

        [Fact]
        public void Package_ManifestIsAcceptedByTheCatalogParser()
        {
            ReleasePackage package = Package(MakeMod());

            CatalogEntry entry = CatalogParser.Parse(IndexFor(File.ReadAllText(package.ManifestPath))).Entries.Single();
            Assert.Equal(Uid, entry.Uid);
            Assert.Equal("Diplomatic-Corps", entry.Id);
        }

        [Fact]
        public void Package_ANamedPreviewThatIsMissing_MeansNoPreview_AndSaysSo()
        {
            ModPublishMetadata metadata = Metadata();
            metadata.PreviewImage = "Gone.png";
            ReleasePackage package = Package(MakeMod(), metadata: metadata);

            Assert.Null(package.PreviewPath);
            Assert.Contains("Gone.png", Assert.Single(package.Warnings));
        }

        [Fact]
        public void Package_AModWithNothingToClassify_IsRefused()
        {
            string mod = Path.Combine(root, "mods", "Bare");
            Directory.CreateDirectory(mod);
            File.WriteAllText(Path.Combine(mod, "mod.json"), "{}");

            Assert.Contains("cannot be classified", Assert.Throws<InvalidDataException>(() => Package(mod)).Message);
        }

        [Fact]
        public void Differences_FindsAChangedAddedAndRemovedFile()
        {
            string mod = MakeMod();
            ReleasePackage package = Package(mod);
            Assert.Empty(ReleasePackager.Differences(package.ZipPath, mod));

            File.WriteAllText(Path.Combine(mod, "data", "x.xml"), "<b/>");
            File.WriteAllText(Path.Combine(mod, "new.txt"), "n");
            File.Delete(Path.Combine(mod, "preview.png"));

            Assert.Equal(new[] { "+ new.txt", "- preview.png", "≠ data/x.xml" }, ReleasePackager.Differences(package.ZipPath, mod).OrderBy(x => x, StringComparer.Ordinal));
        }

        private class FakeRunner : IProcessRunner
        {
            public List<string[]> Calls { get; } = new List<string[]>();
            public Func<string[], ProcessResult> Answer = args => new ProcessResult { ExitCode = 0, Output = "", Error = "" };
            public bool NotInstalled;

            public ProcessResult Run(string program, IReadOnlyList<string> arguments)
            {
                if (NotInstalled) throw new Win32Exception(2);
                Assert.Equal("gh", program);
                Calls.Add(arguments.ToArray());
                return Answer(arguments.ToArray());
            }
        }

        [Fact]
        public void Host_WithoutGh_SaysHowToInstallIt()
        {
            GitHubReleaseHost host = new GitHubReleaseHost(new FakeRunner { NotInstalled = true });

            Assert.Contains("cli.github.com", Assert.Throws<ReleaseHostException>(() => host.CheckReady()).Message);
        }

        [Fact]
        public void Host_NotLoggedIn_ThrowsWithGhsMessage()
        {
            GitHubReleaseHost host = new GitHubReleaseHost(new FakeRunner { Answer = a => new ProcessResult { ExitCode = 1, Error = "You are not logged in" } });

            Assert.Contains("You are not logged in", Assert.Throws<ReleaseHostException>(() => host.CheckReady()).Message);
        }

        [Fact]
        public void Host_ReleaseExists_TellsNotFoundFromRealFailure()
        {
            FakeRunner runner = new FakeRunner();
            GitHubReleaseHost host = new GitHubReleaseHost(runner);
            Assert.True(host.ReleaseExists("o/r", "t"));

            runner.Answer = a => new ProcessResult { ExitCode = 1, Error = "release not found" };
            Assert.False(host.ReleaseExists("o/r", "t"));

            runner.Answer = a => new ProcessResult { ExitCode = 1, Error = "HTTP 404: Not Found (repository)" };
            Assert.Contains("404", Assert.Throws<ReleaseHostException>(() => host.ReleaseExists("o/r", "t")).Message);
        }

        [Fact]
        public void Host_Create_PassesFilesTitleNotesAndNotLatest()
        {
            FakeRunner runner = new FakeRunner();
            ReleasePackage package = Package(MakeMod());

            new GitHubReleaseHost(runner).Create("lucky-wolf/Distant-Worlds-2-Mods", package);

            string[] call = Assert.Single(runner.Calls);
            Assert.Equal(new[] { "release", "create", "Diplomatic-Corps-v1.0.15" }, call.Take(3));
            Assert.All(package.Files, f => Assert.Contains(f, call));
            Assert.Contains("--latest=false", call);
            Assert.Equal("Diplomatic Corps 1.0.15", call[Array.IndexOf(call, "--title") + 1]);
            Assert.Equal(package.NotesPath, call[Array.IndexOf(call, "--notes-file") + 1]);
            Assert.Equal("lucky-wolf/Distant-Worlds-2-Mods", call[Array.IndexOf(call, "--repo") + 1]);
        }

        [Fact]
        public void Host_RefusesARepoThatIsNotOwnerSlashName()
        {
            GitHubReleaseHost host = new GitHubReleaseHost(new FakeRunner());

            Assert.False(GitHubReleaseHost.IsRepo("https://github.com/o/r"));
            Assert.Throws<ReleaseHostException>(() => host.ReleaseExists("not a repo", "t"));
        }

        // A fake gh for a whole publish: "release view" says whether the tag exists, "release download" provides a zip of the folder as it was.
        private ReleaseResult Publish(string mod, bool exists, string releasedZip, Func<string, IList<string>, bool> confirm, out FakeRunner runner, bool topic = true)
        {
            FakeRunner fake = new FakeRunner();
            fake.Answer = a =>
            {
                string key = a[0] + " " + a[1];
                if (key == "release view") return exists ? new ProcessResult() : new ProcessResult { ExitCode = 1, Error = "release not found" };
                if (key == "repo view") return new ProcessResult { Output = topic ? "{\"repositoryTopics\":[{\"name\":\"dw2-mod\"}]}" : "{\"repositoryTopics\":[]}" };
                if (key == "release download")
                {
                    string dir = a[Array.IndexOf(a, "--dir") + 1];
                    Directory.CreateDirectory(dir);
                    File.Copy(releasedZip, Path.Combine(dir, a[Array.IndexOf(a, "--pattern") + 1]), true);
                }
                return new ProcessResult();
            };
            runner = fake;
            return ReleasePublisher.Publish(new GitHubReleaseHost(fake), "o/r", mod, Path.Combine(root, "work"), Metadata(), Meta(), null, DateTime.UtcNow, confirm);
        }

        private static string[] Verbs(FakeRunner runner) { return runner.Calls.Select(c => c[0] + " " + c[1]).ToArray(); }

        [Fact]
        public void Publish_NewTag_CreatesTheRelease()
        {
            string mod = MakeMod();

            ReleaseResult result = Publish(mod, false, null, (t, d) => throw new InvalidOperationException("no question expected"), out FakeRunner runner);

            Assert.Equal(ReleaseOutcome.Created, result.Outcome);
            Assert.Empty(result.Warnings);
            Assert.Equal(new[] { "auth status", "repo view", "release view", "release create" }, Verbs(runner));
        }

        [Fact]
        public void Publish_SameFilesAlreadyReleased_DoesNothing()
        {
            string mod = MakeMod();
            ReleasePackage before = Package(mod);

            ReleaseResult result = Publish(mod, true, before.ZipPath, (t, d) => throw new InvalidOperationException("no question expected"), out FakeRunner runner);

            Assert.Equal(ReleaseOutcome.AlreadyCurrent, result.Outcome);
            Assert.DoesNotContain("release create", Verbs(runner));
            Assert.DoesNotContain("release delete", Verbs(runner));
        }

        [Fact]
        public void Publish_ChangedFilesAlreadyReleased_AsksThenReplaces()
        {
            string mod = MakeMod();
            ReleasePackage before = Package(mod);
            File.WriteAllText(Path.Combine(mod, "data", "x.xml"), "<changed/>");
            IList<string> asked = null;

            ReleaseResult result = Publish(mod, true, before.ZipPath, (t, d) => { asked = d; return true; }, out FakeRunner runner);

            Assert.Equal(ReleaseOutcome.Replaced, result.Outcome);
            Assert.Equal(new[] { "≠ data/x.xml" }, asked);
            Assert.Equal(new[] { "release delete", "release create" }, Verbs(runner).Skip(Verbs(runner).Length - 2));
        }

        [Fact]
        public void Publish_ChangedFilesAlreadyReleased_Declined_LeavesTheReleaseAlone()
        {
            string mod = MakeMod();
            ReleasePackage before = Package(mod);
            File.WriteAllText(Path.Combine(mod, "data", "x.xml"), "<changed/>");

            ReleaseResult result = Publish(mod, true, before.ZipPath, (t, d) => false, out FakeRunner runner);

            Assert.Equal(ReleaseOutcome.Declined, result.Outcome);
            Assert.DoesNotContain("release delete", Verbs(runner));
            Assert.DoesNotContain("release create", Verbs(runner));
        }

        [Fact]
        public void Publish_RepoWithoutTheCatalogTopic_WarnsButReleases()
        {
            ReleaseResult result = Publish(MakeMod(), false, null, (t, d) => false, out FakeRunner runner, topic: false);

            Assert.Equal(ReleaseOutcome.Created, result.Outcome);
            Assert.Contains("dw2-mod", Assert.Single(result.Warnings));
        }

        [Fact]
        public void Host_HasCatalogTopic_ReadsTheTopics()
        {
            FakeRunner runner = new FakeRunner { Answer = a => new ProcessResult { Output = "{\"repositoryTopics\":[{\"name\":\"mods\"},{\"name\":\"dw2-mod\"}]}" } };
            Assert.True(new GitHubReleaseHost(runner).HasCatalogTopic("o/r"));

            runner.Answer = a => new ProcessResult { Output = "{\"repositoryTopics\":null}" };
            Assert.False(new GitHubReleaseHost(runner).HasCatalogTopic("o/r"));
        }
    }
}
