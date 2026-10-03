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
                    Description = "New description",
                    PreviewImage = "poster.jpg",
                    Version = "2.0.0",
                    Bundles = new System.Collections.Generic.List<string> { "A.bundle" }
                };

                ModPublishMetadataEditor.Write(path, metadata);

                string text = File.ReadAllText(path);
                Assert.Contains("\"displayName\": \"New Name\"", text);
                Assert.Contains("\"description\": \"New description\"", text);
                Assert.Contains("\"previewImage\": \"poster.jpg\"", text);
                Assert.Contains("\"version\": \"2.0.0\"", text);
                Assert.Contains("\"A.bundle\"", text);
                Assert.Contains("\"workshopId\": 42", text);
                Assert.Contains("\"disableDefaultMusic\": true", text);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Write_BlankDescription_RemovesTheKeyInsteadOfWritingAnEmptyOne()
        {
            string path = MakeModJson("{ \"displayName\": \"XL\", \"shortDescription\": \"short\", \"descriptionFile\": \"description.txt\" }");
            try
            {
                ModPublishMetadata metadata = ModPublishMetadataEditor.Read(path);
                metadata.Description = "  ";
                ModPublishMetadataEditor.Write(path, metadata);
                string text = File.ReadAllText(path);
                Assert.DoesNotContain("\"description\"", text);
                Assert.Contains("\"descriptionFile\": \"description.txt\"", text);
                Assert.Contains("\"shortDescription\": \"short\"", text);

                metadata.Description = "Now set";
                ModPublishMetadataEditor.Write(path, metadata);
                Assert.Contains("\"description\": \"Now set\"", File.ReadAllText(path));
                metadata.Description = "";
                ModPublishMetadataEditor.Write(path, metadata);
                Assert.DoesNotContain("\"description\":", File.ReadAllText(path));
            }
            finally { File.Delete(path); }
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
