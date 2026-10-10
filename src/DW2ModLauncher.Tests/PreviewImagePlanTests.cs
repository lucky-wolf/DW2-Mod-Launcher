using System;
using System.IO;
using System.Linq;
using DW2ModLauncher.Core.Services.Publishing;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class PreviewImagePlanTests : IDisposable
    {
        private readonly string dir = Path.Combine(Path.GetTempPath(), "dw2-test-" + Guid.NewGuid().ToString("N"));

        public PreviewImagePlanTests() { Directory.CreateDirectory(dir); }
        public void Dispose() { Directory.Delete(dir, true); }

        [Fact]
        public void NeedsResize_OnlyAboveOneMiB()
        {
            Assert.False(PreviewImagePlan.NeedsResize(1024 * 1024));
            Assert.True(PreviewImagePlan.NeedsResize(1024 * 1024 + 1));
        }

        [Fact]
        public void Sizes_StartAtTheOriginal_ShrinkBothSidesTogether_AndStopAtTheFloor()
        {
            var sizes = PreviewImagePlan.Sizes(2000, 1000);

            Assert.Equal(new System.Collections.Generic.KeyValuePair<int, int>(2000, 1000), sizes[0]);
            Assert.All(sizes, s => Assert.InRange(s.Key / (double)s.Value, 1.99, 2.01));
            Assert.True(sizes.Zip(sizes.Skip(1), (a, b) => a.Key > b.Key).All(x => x));
            Assert.True(sizes.Last().Key >= PreviewImagePlan.MinLongEdge);
            Assert.True(sizes.Count > 5);
        }

        [Fact]
        public void Sizes_HandlesTinyAndInvalidImages()
        {
            Assert.Single(PreviewImagePlan.Sizes(100, 50));
            Assert.Empty(PreviewImagePlan.Sizes(0, 50));
        }

        [Theory]
        [InlineData("cover", ".jpg", "cover.jpg")]
        [InlineData("cover.jpg", ".jpg", "cover.jpg")]
        [InlineData("Cover.PNG", ".jpg", "Cover.jpg")]
        [InlineData("cover.jpeg", ".png", "cover.png")]
        [InlineData("  cover v2 ", ".jpg", "cover v2.jpg")]
        [InlineData("cover.v2", ".jpg", "cover.v2.jpg")]
        [InlineData("", ".jpg", null)]
        [InlineData(".jpg", ".jpg", null)]
        [InlineData("..", ".jpg", null)]
        [InlineData("sub/cover", ".jpg", null)]
        [InlineData("a\\b", ".jpg", null)]
        public void FileNameFor_ForcesTheEncodedExtension_AndRejectsNonNames(string typed, string extension, string expected)
        {
            Assert.Equal(expected, PreviewImagePlan.FileNameFor(typed, extension));
        }
    }
}
