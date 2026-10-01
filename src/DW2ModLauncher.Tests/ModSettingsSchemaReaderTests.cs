using System.IO;
using DW2ModLauncher.Core.Services;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class ModSettingsSchemaReaderTests
    {
        [Fact]
        public void Read_ReturnsNull_WhenFileMissing()
        {
            string dir = Path.Combine(Path.GetTempPath(), "dw2-schema-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                Assert.Null(ModSettingsSchemaReader.Read(dir));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void Read_ParsesFields_WhenValid()
        {
            string dir = Path.Combine(Path.GetTempPath(), "dw2-schema-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(Path.Combine(dir, "settings.schema.json"), @"{
                    ""fields"": [
                        { ""key"": ""Enabled"", ""type"": ""bool"", ""label"": ""Enabled"", ""default"": true },
                        { ""key"": ""Mode"", ""type"": ""enum"", ""options"": [""a"", ""b""], ""default"": ""a"" }
                    ]
                }");

                var schema = ModSettingsSchemaReader.Read(dir);

                Assert.NotNull(schema);
                Assert.Equal(2, schema.Fields.Count);
                Assert.Equal("Enabled", schema.Fields[0].Key);
                Assert.Equal("bool", schema.Fields[0].Type);
                Assert.Equal(2, schema.Fields[1].Options.Count);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void Read_ReturnsNull_WhenMalformed()
        {
            string dir = Path.Combine(Path.GetTempPath(), "dw2-schema-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(Path.Combine(dir, "settings.schema.json"), "{ not valid json");

                Assert.Null(ModSettingsSchemaReader.Read(dir));
            }
            finally { Directory.Delete(dir, true); }
        }
    }
}
