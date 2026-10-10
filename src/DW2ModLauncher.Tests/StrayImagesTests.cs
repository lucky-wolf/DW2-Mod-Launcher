using System;
using System.IO;
using DW2ModLauncher.Core.Services.Publishing;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class StrayImagesTests
    {
        [Fact]
        public void Find_ListsLooseImagesExceptThePreview_AndLeavesSubfoldersAndOtherFiles()
        {
            string root = Path.Combine(Path.GetTempPath(), "dw2-stray-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "Textures"));
            try
            {
                foreach (string f in new[] { "cover.jpg", "Cover (resized).PNG", "big.png", "notes.txt", "mod.json", "Textures/ship.png" })
                    File.WriteAllText(Path.Combine(root, f), "x");

                Assert.Equal(new[] { "big.png", "Cover (resized).PNG" }, StrayImages.Find(root, "cover.jpg"));
                Assert.Equal(new[] { "big.png", "Cover (resized).PNG", "cover.jpg" },StrayImages.Find(root, ""));
                Assert.Equal(new[] { "big.png", "Cover (resized).PNG" }, StrayImages.Find(root, "COVER.JPG"));
            }
            finally { Directory.Delete(root, true); }
        }

        [Fact]
        public void Find_PreviewNamingAMissingFile_ReportsNothing_SoTheOnlyImageIsNeverOfferedForDeletion()
        {
            string root = Path.Combine(Path.GetTempPath(), "dw2-stray-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                File.WriteAllText(Path.Combine(root, "Independence Day.png"), "x");
                Assert.Empty(StrayImages.Find(root, "Independence Day (Simple).png"));
            }
            finally { Directory.Delete(root, true); }
        }

        [Fact]
        public void Find_MissingFolder_IsEmpty()
        {
            Assert.Empty(StrayImages.Find(Path.Combine(Path.GetTempPath(), "dw2-nope-" + Guid.NewGuid().ToString("N")), "x.png"));
        }
    }
}
