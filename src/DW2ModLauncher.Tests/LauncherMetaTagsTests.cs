using System.IO;
using System.Linq;
using DW2ModLauncher.Core.Services;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class LauncherMetaTagsTests
    {
        private static string TempFolder()
        {
            string folder = Path.Combine(Path.GetTempPath(), "dw2-tags-" + Path.GetRandomFileName());
            Directory.CreateDirectory(folder);
            return folder;
        }

        [Fact]
        public void SetTags_WritesThemNormalized_AndKeepsTheRestOfTheFile()
        {
            string folder = TempFolder();
            try
            {
                File.WriteAllText(Path.Combine(folder, "dw2modlauncher.json"), "{\"author\":\"Someone\"}");

                LauncherMetaWriter.SetTags(folder, new[] { " Music ", "Sound  Effects", "music" });

                string text = File.ReadAllText(Path.Combine(folder, "dw2modlauncher.json"));
                Assert.Contains("\"author\": \"Someone\"", text);
                Assert.Contains("\"music\"", text);
                Assert.Contains("\"sound effects\"", text);
                Assert.Equal(1, text.Split("\"music\"").Length - 1);
            }
            finally { Directory.Delete(folder, true); }
        }

        [Fact]
        public void SetTags_None_RemovesTheKey_AndCreatesNoFile()
        {
            string folder = TempFolder();
            try
            {
                LauncherMetaWriter.SetTags(folder, Enumerable.Empty<string>());
                Assert.False(File.Exists(Path.Combine(folder, "dw2modlauncher.json")));

                File.WriteAllText(Path.Combine(folder, "dw2modlauncher.json"), "{\"tags\":[\"art\"],\"author\":\"Someone\"}");
                LauncherMetaWriter.SetTags(folder, new string[0]);

                string text = File.ReadAllText(Path.Combine(folder, "dw2modlauncher.json"));
                Assert.DoesNotContain("tags", text);
                Assert.Contains("Someone", text);
            }
            finally { Directory.Delete(folder, true); }
        }
    }
}
