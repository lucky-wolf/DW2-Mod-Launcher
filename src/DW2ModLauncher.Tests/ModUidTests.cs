using System.IO;
using System.Text.Json.Nodes;
using DW2ModLauncher.Core.Services;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class ModUidTests
    {
        private const string Uid = "6f1c2a3e-8b4d-4e5f-9a60-7b1d2c3e4f50";

        private static string TempFolder()
        {
            string folder = Path.Combine(Path.GetTempPath(), "dw2ml-uid-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            return folder;
        }

        private static string Read(string folder, string field)
        {
            return JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "dw2modlauncher.json")))[field]?.ToString();
        }

        [Theory]
        [InlineData(Uid, Uid)]
        [InlineData("6F1C2A3E-8B4D-4E5F-9A60-7B1D2C3E4F50", Uid)]
        [InlineData("{6f1c2a3e-8b4d-4e5f-9a60-7b1d2c3e4f50}", Uid)]
        [InlineData("  " + Uid + " ", Uid)]
        [InlineData("", null)]
        [InlineData(null, null)]
        [InlineData("my-mod", null)]
        [InlineData("00000000-0000-0000-0000-000000000000", null)]
        public void Normalize_GivesTheCanonicalFormOrNull(string text, string expected)
        {
            Assert.Equal(expected, ModUid.Normalize(text));
        }

        [Fact]
        public void StampUid_WritesOnceAndKeepsIt_AndKeepsOtherFields()
        {
            string folder = TempFolder();
            try
            {
                File.WriteAllText(Path.Combine(folder, "dw2modlauncher.json"), "{ \"author\": \"Someone\" }");

                string first = LauncherMetaWriter.StampUid(folder);
                string second = LauncherMetaWriter.StampUid(folder);

                Assert.Equal(first, second);
                Assert.Equal(first, ModUid.Normalize(first));
                Assert.Equal(first, Read(folder, "uid"));
                Assert.Equal("Someone", Read(folder, "author"));
            }
            finally { Directory.Delete(folder, true); }
        }

        [Fact]
        public void StampUid_Replace_IssuesAFreshOne()
        {
            string folder = TempFolder();
            try
            {
                File.WriteAllText(Path.Combine(folder, "dw2modlauncher.json"), "{ \"uid\": \"" + Uid + "\" }");

                string fresh = LauncherMetaWriter.StampUid(folder, true);

                Assert.NotEqual(Uid, fresh);
                Assert.Equal(fresh, Read(folder, "uid"));
            }
            finally { Directory.Delete(folder, true); }
        }

        [Fact]
        public void StampUid_AnIdentityThatIsNotAUuid_IsReported_NotReplaced()
        {
            string folder = TempFolder();
            try
            {
                File.WriteAllText(Path.Combine(folder, "dw2modlauncher.json"), "{ \"uid\": \"my-mod\" }");

                Assert.Throws<InvalidDataException>(() => LauncherMetaWriter.StampUid(folder));
                Assert.Equal("my-mod", Read(folder, "uid"));
            }
            finally { Directory.Delete(folder, true); }
        }
    }
}
