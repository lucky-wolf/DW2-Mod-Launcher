using System;
using System.IO;
using System.Linq;
using DW2ModLauncher.Core.Models;
using DW2ModLauncher.Core.Services.Publishing;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class PublishedStateGitHubTests : IDisposable
    {
        private const string Uid = "11111111-2222-3333-4444-555555555555";
        private readonly string root = Path.Combine(Path.GetTempPath(), "dw2-github-state-" + Guid.NewGuid().ToString("N"));
        private string Content { get { return Path.Combine(root, "mod"); } }
        private string Data { get { return Path.Combine(root, "data"); } }

        public PublishedStateGitHubTests()
        {
            Directory.CreateDirectory(Content);
            File.WriteAllText(Path.Combine(Content, "mod.json"), "{}");
            File.WriteAllText(Path.Combine(Content, "a.txt"), "alpha");
        }

        public void Dispose() { Directory.Delete(root, true); }

        private ModInfo Mod(string repo = "me/mods", string uid = Uid, bool workshop = false)
        {
            return new ModInfo { Id = "m", DisplayName = "m", Folder = Content, ContentRoot = Content, ReleaseRepo = repo, Uid = uid, IsWorkshop = workshop };
        }

        [Fact]
        public void NoRepositoryNoUidOrAWorkshopCopy_IsNotApplicable()
        {
            Assert.Equal(PublishStatus.NotApplicable, PublishedState.EvaluateGitHub(Mod(repo: null), Data));
            Assert.Equal(PublishStatus.NotApplicable, PublishedState.EvaluateGitHub(Mod(uid: null), Data));
            Assert.Equal(PublishStatus.NotApplicable, PublishedState.EvaluateGitHub(Mod(workshop: true), Data));
        }

        [Fact]
        public void ARepositoryWithoutARecord_IsUnknown_ThenCurrent_ThenChanged()
        {
            ModInfo mod = Mod();
            Assert.Equal(PublishStatus.Unknown, PublishedState.EvaluateGitHub(mod, Data));
            Assert.Null(mod.GitHubPublishedUtc);

            PublishedState.RecordGitHub(Uid, Content, "1.2.3", Data);
            Assert.Equal(PublishStatus.Current, PublishedState.EvaluateGitHub(mod, Data));
            Assert.NotNull(mod.GitHubPublishedUtc);
            Assert.Empty(mod.GitHubChanges);

            File.WriteAllText(Path.Combine(Content, "a.txt"), "beta");
            File.WriteAllText(Path.Combine(Content, "b.txt"), "new");
            Assert.Equal(PublishStatus.Changed, PublishedState.EvaluateGitHub(mod, Data));
            Assert.Equal(new[] { "≠ a.txt", "+ b.txt" }, mod.GitHubChanges);
        }

        [Fact]
        public void TheGitHubRecordIsKeptApartFromTheSteamRecord()
        {
            ModInfo mod = Mod();
            mod.WorkshopId = "999";
            PublishedState.Record("999", Content, Data);
            Assert.Equal(PublishStatus.Current, PublishedState.Evaluate(mod, Data));
            Assert.Equal(PublishStatus.Unknown, PublishedState.EvaluateGitHub(mod, Data));

            PublishedState.RecordGitHub(Uid, Content, null, Data);
            File.WriteAllText(Path.Combine(Content, "a.txt"), "beta");
            PublishedState.Record("999", Content, Data);
            Assert.Equal(PublishStatus.Current, PublishedState.Evaluate(mod, Data));
            Assert.Equal(PublishStatus.Changed, PublishedState.EvaluateGitHub(mod, Data));
            Assert.Equal(new[] { "999.json" }, Directory.GetFiles(Path.Combine(Data, "PublishedState")).Select(Path.GetFileName).ToArray());
        }

        [Fact]
        public void MarkUpToDate_ActsOnEveryCheckedTarget_AndCreatesAMissingGitHubRecord()
        {
            ModInfo mod = Mod();
            mod.WorkshopId = "999";
            PublishedState.Record("999", Content, Data);
            File.WriteAllText(Path.Combine(Content, "a.txt"), "beta");
            PublishedState.Evaluate(mod, Data);
            PublishedState.EvaluateGitHub(mod, Data);
            Assert.Equal(PublishStatus.Changed, mod.PublishStatus);
            Assert.Equal(PublishStatus.Unknown, mod.GitHubStatus);

            Assert.Equal(PublishChannels.Steam, MarkUpToDate.Eligible(mod, steam: true, github: false));
            Assert.Equal(PublishChannels.GitHub, MarkUpToDate.Eligible(mod, steam: false, github: true));
            Assert.Equal(PublishChannels.Steam | PublishChannels.GitHub, MarkUpToDate.Eligible(mod, steam: true, github: true));
            Assert.Equal(PublishChannels.None, MarkUpToDate.Eligible(mod, steam: false, github: false));
            Assert.Equal(1, MarkUpToDate.ChangedFileCount(mod, PublishChannels.Steam | PublishChannels.GitHub));

            Assert.Empty(MarkUpToDate.Apply(mod, PublishChannels.Steam | PublishChannels.GitHub, Content, Data));
            PublishedState.Evaluate(mod, Data);
            PublishedState.EvaluateGitHub(mod, Data);
            Assert.Equal(PublishStatus.Current, mod.PublishStatus);
            Assert.Equal(PublishStatus.Current, mod.GitHubStatus);
            Assert.Equal(PublishChannels.None, MarkUpToDate.Eligible(mod, steam: true, github: true));
        }

        [Fact]
        public void MarkUpToDate_OnlyTouchesTheCheckedTarget_AndKeepsTheReleaseTime()
        {
            ModInfo mod = Mod();
            mod.WorkshopId = "999";
            PublishedState.Record("999", Content, Data);
            PublishedState.RecordGitHub(Uid, Content, "1.0.0", Data);
            DateTime released = PublishedState.LoadGitHub(Uid, Data).PublishedUtc;
            File.WriteAllText(Path.Combine(Content, "a.txt"), "beta");

            Assert.Empty(MarkUpToDate.Apply(mod, PublishChannels.GitHub, Content, Data));
            PublishedState.Evaluate(mod, Data);
            PublishedState.EvaluateGitHub(mod, Data);
            Assert.Equal(PublishStatus.Changed, mod.PublishStatus);
            Assert.Equal(PublishStatus.Current, mod.GitHubStatus);
            Assert.Equal(released, PublishedState.LoadGitHub(Uid, Data).PublishedUtc);
            Assert.Equal("1.0.0", PublishedState.LoadGitHub(Uid, Data).Version);
        }

        [Fact]
        public void MarkUpToDate_WithPendingEdits_CountsEveryTrackedTarget()
        {
            ModInfo mod = Mod();
            mod.WorkshopId = "999";
            PublishedState.Record("999", Content, Data);
            PublishedState.RecordGitHub(Uid, Content, null, Data);
            PublishedState.Evaluate(mod, Data);
            PublishedState.EvaluateGitHub(mod, Data);
            Assert.Equal(PublishChannels.None, MarkUpToDate.Eligible(mod, true, true));
            Assert.Equal(PublishChannels.Steam | PublishChannels.GitHub, MarkUpToDate.Eligible(mod, true, true, pendingEdits: true));
        }

        [Fact]
        public void ARecordNeedsARealUid()
        {
            Assert.Throws<ArgumentException>(() => PublishedState.RecordGitHub("not-a-uid", Content, null, Data));
        }
    }
}
