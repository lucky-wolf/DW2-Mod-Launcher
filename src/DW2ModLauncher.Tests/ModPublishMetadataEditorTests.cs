using System;
using System.IO;
using DW2ModLauncher.Core.Models;
using DW2ModLauncher.Core.Services.Publishing;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class ModPublishMetadataEditorTests
    {
        private static string MakeModJson(string content)
        {
            string path = Path.Combine(Path.GetTempPath(), "dw2-publishmeta-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path, content);
            return path;
        }

        [Fact]
        public void Read_ParsesAllFields()
        {
            string path = MakeModJson(@"{
                ""displayName"": ""My Mod"",
                ""description"": ""Does things."",
                ""previewImage"": ""preview.jpg"",
                ""version"": ""1.2.3"",
                ""bundles"": [ ""MyMod.bundle"", ""MyMod2.bundle"" ]
            }");
            try
            {
                ModPublishMetadata metadata = ModPublishMetadataEditor.Read(path);

                Assert.Equal("My Mod", metadata.DisplayName);
                Assert.Equal("Does things.", metadata.Description);
                Assert.Equal("preview.jpg", metadata.PreviewImage);
                Assert.Equal("1.2.3", metadata.Version);
                Assert.Equal(new[] { "MyMod.bundle", "MyMod2.bundle" }, metadata.Bundles);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Write_UpdatesFields_PreservingUnrelatedOnes()
        {
            string path = MakeModJson(@"{
                ""displayName"": ""Old Name"",
                ""version"": ""1.0.0"",
                ""bundles"": [],
                ""workshopId"": 42,
                ""disableDefaultMusic"": true
            }");
            try
            {
                ModPublishMetadata metadata = new ModPublishMetadata
                {
                    DisplayName = "New Name",
                    PreviewImage = "poster.jpg",
                    Version = "2.0.0",
                    Bundles = new System.Collections.Generic.List<string> { "A.bundle" }
                };

                ModPublishMetadataEditor.Write(path, metadata);

                string text = File.ReadAllText(path);
                Assert.Contains("\"displayName\": \"New Name\"", text);
                Assert.Contains("\"previewImage\": \"poster.jpg\"", text);
                Assert.Contains("\"version\": \"2.0.0\"", text);
                Assert.Contains("\"A.bundle\"", text);
                Assert.Contains("\"workshopId\": 42", text);
                Assert.Contains("\"disableDefaultMusic\": true", text);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Write_DropsInlineDescription_KeepsCustomDescriptionFile_AndDropsTheDefaultOne()
        {
            string path = MakeModJson("{ \"displayName\": \"XL\", \"shortDescription\": \"short\", \"description\": \"old\", \"descriptionFile\": \"docs/about.md\", \"workshopId\": 7 }");
            try
            {
                ModPublishMetadata metadata = ModPublishMetadataEditor.Read(path);
                Assert.Equal("old", metadata.Description);
                ModPublishMetadataEditor.Write(path, metadata);
                string text = File.ReadAllText(path);
                Assert.DoesNotContain("\"description\"", text);
                Assert.Contains("\"descriptionFile\": \"docs/about.md\"", text);
                Assert.Contains("\"shortDescription\": \"short\"", text);
                Assert.Contains("\"workshopId\": 7", text);

                metadata.DescriptionFile = "description.bbcode";
                metadata.ShortDescription = "  ";
                ModPublishMetadataEditor.Write(path, metadata);
                text = File.ReadAllText(path);
                Assert.DoesNotContain("descriptionFile", text);
                Assert.DoesNotContain("shortDescription", text);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void ModDescriptionFile_NameFor_UsesTheModsOwnFile_ButNeverOneOutsideTheFolder()
        {
            string dir = Path.Combine(Path.GetTempPath(), "dw2-namefor-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                Assert.Equal("description.bbcode", ModDescriptionFile.NameFor(dir, ""));
                Assert.Equal("docs/about.md", ModDescriptionFile.NameFor(dir, " docs/about.md "));
                Assert.Equal("description.bbcode", ModDescriptionFile.NameFor(dir, "../outside.txt"));

                // A mod that only has the earlier default (description.txt) and names no file keeps using it...
                File.WriteAllText(Path.Combine(dir, "description.txt"), "old");
                Assert.Equal("description.txt", ModDescriptionFile.NameFor(dir, ""));
                Assert.Equal("description.txt", ModDescriptionFile.NameFor(dir, "../outside.txt"));
                // ...until a description.bbcode exists, which wins.
                File.WriteAllText(Path.Combine(dir, "description.bbcode"), "new");
                Assert.Equal("description.bbcode", ModDescriptionFile.NameFor(dir, ""));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ModDescriptionFile_Load_FileWins_ThenLegacyInlineDescription()
        {
            string dir = Path.Combine(Path.GetTempPath(), "dw2-resolvedesc-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                ModPublishMetadata metadata = new ModPublishMetadata { Description = "Typed text", DescriptionFile = "about.md" };
                Assert.Equal("Typed text", ModDescriptionFile.Load(dir, metadata));

                metadata.Description = "  ";
                Assert.Equal("", ModDescriptionFile.Load(dir, metadata));
                Assert.Null(ModPublishMetadataEditor.ResolveSteamDescription(dir, metadata));

                // Once the (custom-named) file exists it wins over the inline text.
                File.WriteAllText(Path.Combine(dir, "about.md"), "From the file");
                metadata.Description = "Typed text";
                Assert.Equal("From the file", ModDescriptionFile.Load(dir, metadata));
                Assert.Equal("From the file", ModPublishMetadataEditor.ResolveSteamDescription(dir, metadata));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ModDescriptionFile_Save_WritesLfUtf8WithoutBom_AndReadsBack()
        {
            string dir = Path.Combine(Path.GetTempPath(), "dw2-savedesc-" + Guid.NewGuid().ToString("N"));
            try
            {
                ModDescriptionFile.Save(dir, "docs/about.md", "line one\r\nline two é");
                byte[] bytes = File.ReadAllBytes(ModDescriptionFile.PathFor(dir, "docs/about.md"));
                Assert.NotEqual(0xEF, bytes[0]);
                Assert.DoesNotContain((byte)'\r', bytes);
                Assert.Equal("line one\nline two é", ModDescriptionFile.ReadFile(dir, "docs/about.md"));
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }

        [Fact]
        public void ModDescriptionFile_SameText_IgnoresLineEndingsAndOuterWhitespace()
        {
            Assert.True(ModDescriptionFile.SameText("a\r\nb\r\n", "  a\nb"));
            Assert.True(ModDescriptionFile.SameText(null, ""));
            Assert.False(ModDescriptionFile.SameText("a b", "a  b"));
        }

        [Fact]
        public void Read_ReturnsEmptyMetadata_WhenFileMissing()
        {
            ModPublishMetadata metadata = ModPublishMetadataEditor.Read(Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid().ToString("N") + ".json"));

            Assert.Null(metadata.DisplayName);
            Assert.Empty(metadata.Bundles);
        }
    }
}
