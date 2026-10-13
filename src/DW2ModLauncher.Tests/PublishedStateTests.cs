using System;
using System.IO;
using System.Linq;
using DW2ModLauncher.Core.Models;
using DW2ModLauncher.Core.Services;
using DW2ModLauncher.Core.Services.Publishing;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class PublishedStateTests : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "dw2-published-" + Guid.NewGuid().ToString("N"));
        private string Content { get { return Path.Combine(root, "mod"); } }
        private string Data { get { return Path.Combine(root, "data"); } }

        public PublishedStateTests()
        {
            Directory.CreateDirectory(Path.Combine(Content, "sub"));
            File.WriteAllText(Path.Combine(Content, "mod.json"), "{}");
            File.WriteAllText(Path.Combine(Content, "sub", "a.txt"), "alpha");
        }

        public void Dispose() { Directory.Delete(root, true); }

        [Fact]
        public void Compare_IsClean_RightAfterCapture()
        {
            Assert.True(PublishedState.Compare(Content, PublishedState.Capture(Content)).IsClean);
        }

        [Fact]
        public void Compare_ReportsAddedRemovedAndModified()
        {
            PublishedManifest manifest = PublishedState.Capture(Content);
            File.WriteAllText(Path.Combine(Content, "new.txt"), "n");
            File.Delete(Path.Combine(Content, "mod.json"));
            File.WriteAllText(Path.Combine(Content, "sub", "a.txt"), "ALPHA");
            File.SetLastWriteTimeUtc(Path.Combine(Content, "sub", "a.txt"), DateTime.UtcNow.AddHours(1));

            PublishedChanges changes = PublishedState.Compare(Content, manifest);

            Assert.Equal(new[] { "new.txt" }, changes.Added);
            Assert.Equal(new[] { "mod.json" }, changes.Removed);
            Assert.Equal(new[] { "sub/a.txt" }, changes.Modified);
            Assert.Equal(3, changes.Count);
        }

        [Fact]
        public void Compare_IgnoresTouchWithIdenticalContent()
        {
            PublishedManifest manifest = PublishedState.Capture(Content);
            File.SetLastWriteTimeUtc(Path.Combine(Content, "sub", "a.txt"), DateTime.UtcNow.AddHours(1));
            Assert.True(PublishedState.Compare(Content, manifest).IsClean);
        }

        [Fact]
        public void Compare_DetectsSameSizeEditWithNewTime()
        {
            PublishedManifest manifest = PublishedState.Capture(Content);
            string file = Path.Combine(Content, "sub", "a.txt");
            File.WriteAllText(file, "omega");
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddHours(1));
            Assert.Equal(new[] { "sub/a.txt" }, PublishedState.Compare(Content, manifest).Modified);
        }

        [Fact]
        public void Load_ReturnsNull_WithoutRecord_AndRoundTripsARecord()
        {
            Assert.Null(PublishedState.Load("123", Data));
            PublishedState.Record("123", Content, Data);
            PublishedManifest loaded = PublishedState.Load("123", Data);
            Assert.Equal(new[] { "mod.json", "sub/a.txt" }, loaded.Files.Keys.OrderBy(k => k, StringComparer.Ordinal));
            Assert.True(PublishedState.Compare(Content, loaded).IsClean);
        }

        [Fact]
        public void Load_Throws_OnCorruptRecord()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PublishedState.GetRecordPath("9", Data)));
            File.WriteAllText(PublishedState.GetRecordPath("9", Data), "not json");
            Assert.Throws<InvalidDataException>(() => PublishedState.Load("9", Data));
        }

        [Fact]
        public void Evaluate_ReportsEachStatus()
        {
            ModInfo local = new ModInfo { Folder = Content, WorkshopId = "77" };
            Assert.Equal(PublishStatus.Unpublished, PublishedState.Evaluate(new ModInfo { Folder = Content }, Data));
            Assert.Equal(PublishStatus.NotApplicable, PublishedState.Evaluate(new ModInfo { Folder = Content, IsWorkshop = true, Id = "77" }, Data));
            Assert.Equal(PublishStatus.Unknown, PublishedState.Evaluate(local, Data));

            PublishedState.Record("77", Content, Data);
            Assert.Equal(PublishStatus.Current, PublishedState.Evaluate(local, Data));

            File.WriteAllText(Path.Combine(Content, "new.txt"), "n");
            Assert.Equal(PublishStatus.Changed, PublishedState.Evaluate(local, Data));
            Assert.Equal(new[] { "+ new.txt" }, local.UnpublishedChanges);
        }
    }
}
