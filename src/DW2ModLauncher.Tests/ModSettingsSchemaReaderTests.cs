using System.IO;
using System.Linq;
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
        public void VisibleFields_HidesHiddenFields_ForWorkshopMods()
        {
            var schema = new DW2ModLauncher.Core.Models.ModSettingsSchema();
            var group = new DW2ModLauncher.Core.Models.ModSettingsGroup { Name = "fields" };
            group.Fields.Add(new DW2ModLauncher.Core.Models.ModSettingsField { Key = "Speed" });
            group.Fields.Add(new DW2ModLauncher.Core.Models.ModSettingsField { Key = "LogSamples", Hidden = true });
            schema.Groups.Add(group);

            var workshop = new DW2ModLauncher.Core.Models.ModInfo { IsWorkshop = true };
            var local = new DW2ModLauncher.Core.Models.ModInfo { IsWorkshop = false };

            Assert.Equal(new[] { "Speed" }, schema.VisibleFields(workshop).Select(f => f.Key));
            Assert.Equal(new[] { "Speed", "LogSamples" }, schema.VisibleFields(local).Select(f => f.Key));
        }

        [Fact]
        public void Read_ParsesArbitraryGroups_InFileOrder_AcceptsLocalOnlyAlias_AndIgnoresNonArrays()
        {
            string dir = Path.Combine(Path.GetTempPath(), "dw2-schema-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(Path.Combine(dir, "settings.schema.json"), @"{
                    ""$schema"": ""whatever"",
                    ""Speed"": [ { ""key"": ""A"", ""type"": ""int"" }, { ""key"": ""B"", ""type"": ""int"", ""hidden"": true } ],
                    ""fields"": [ { ""key"": ""C"", ""type"": ""bool"" }, { ""key"": ""A"", ""type"": ""bool"" } ],
                    ""Dev"": [ { ""key"": ""D"", ""type"": ""bool"", ""localOnly"": true } ]
                }");

                var schema = ModSettingsSchemaReader.Read(dir);

                Assert.Equal(new[] { "Speed", "fields", "Dev" }, schema.Groups.Select(g => g.Name));
                Assert.Equal(new[] { "A", "B", "C", "D" }, schema.Fields.Select(f => f.Key));
                Assert.True(schema.Groups[0].HasHeading);
                Assert.False(schema.Groups[1].HasHeading);
                Assert.True(schema.Fields[1].Hidden);
                Assert.True(schema.Fields[3].Hidden);

                var workshop = new DW2ModLauncher.Core.Models.ModInfo { IsWorkshop = true };
                Assert.Equal(new[] { "Speed", "fields" }, schema.VisibleGroups(workshop).Select(g => g.Name));
                Assert.Equal(new[] { "A", "C" }, schema.VisibleFields(workshop).Select(f => f.Key));
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
