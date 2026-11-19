using System.IO;
using System.Linq;
using DW2ModLauncher.Core.Models;
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
        public void Read_AttachesGroupDescriptions_FromRootGroupsObject()
        {
            string dir = Path.Combine(Path.GetTempPath(), "dw2-schema-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(Path.Combine(dir, "settings.schema.json"), @"{
                    ""Speed"": [ { ""key"": ""A"", ""type"": ""bool"" } ],
                    ""Other"": [ { ""key"": ""B"", ""type"": ""bool"" } ],
                    ""_groups"": { ""speed"": ""About speed."", ""Missing"": ""Nobody home."" }
                }");

                var schema = ModSettingsSchemaReader.Read(dir);

                Assert.Equal(new[] { "Speed", "Other" }, schema.Groups.Select(g => g.Name));
                Assert.Equal("About speed.", schema.Groups[0].Description);
                Assert.Null(schema.Groups[1].Description);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void Read_Throws_WhenMalformed()
        {
            string dir = Path.Combine(Path.GetTempPath(), "dw2-schema-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(Path.Combine(dir, "settings.schema.json"), "{ not valid json");

                Assert.Throws<InvalidDataException>(() => ModSettingsSchemaReader.Read(dir));
            }
            finally { Directory.Delete(dir, true); }
        }

        private static ModSettingsSchema ReadSchema(string json)
        {
            string dir = Path.Combine(Path.GetTempPath(), "dw2-schema-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(Path.Combine(dir, "settings.schema.json"), json);
                return ModSettingsSchemaReader.Read(dir);
            }
            finally { Directory.Delete(dir, true); }
        }

        private const string EnableFields = @"""Main"": [
            { ""key"": ""Master"", ""type"": ""bool"", ""default"": true },
            { ""key"": ""Sub"", ""type"": ""bool"", ""default"": true },
            { ""key"": ""A"", ""type"": ""int"", ""default"": 1 },
            { ""key"": ""B"", ""type"": ""int"", ""default"": 1 }
        ]";

        [Fact]
        public void Read_ParsesLayeredEnables()
        {
            var schema = ReadSchema("{ " + EnableFields + @", ""_enables"": { ""master"": [""Sub"", ""A""], ""Sub"": [""A"", ""B""] } }");

            Assert.Equal(new[] { "Master" }, schema.EnabledBy["Sub"]);
            Assert.Equal(new[] { "Master", "Sub" }, schema.EnabledBy["A"]);
            Assert.Equal(new[] { "Sub" }, schema.EnabledBy["B"]);
            Assert.False(schema.EnabledBy.ContainsKey("Master"));
        }

        [Theory]
        [InlineData(@"""_enables"": { ""Nope"": [""A""] }", "not a setting")]
        [InlineData(@"""_enables"": { ""Master"": [""Nope""] }", "not a setting")]
        [InlineData(@"""_enables"": { ""A"": [""B""] }", "only a bool")]
        [InlineData(@"""_enables"": { ""Master"": [""Master""] }", "lists itself")]
        [InlineData(@"""_enables"": { ""Master"": [""Sub""], ""Sub"": [""Master""] }", "loops")]
        [InlineData(@"""_enables"": { ""Master"": ""A"" }", "array")]
        public void Read_RejectsBadEnables(string enables, string expected)
        {
            var ex = Assert.Throws<InvalidDataException>(() => ReadSchema("{ " + EnableFields + ", " + enables + " }"));
            Assert.Contains(expected, ex.Message);
        }

        private const string EnumEnableFields = @"""Main"": [
            { ""key"": ""Mode"", ""type"": ""enum"", ""options"": [""Galaxy"", ""Overlays"", ""Both""], ""default"": ""Galaxy"" },
            { ""key"": ""Master"", ""type"": ""bool"", ""default"": true },
            { ""key"": ""A"", ""type"": ""int"", ""default"": 1 },
            { ""key"": ""B"", ""type"": ""int"", ""default"": 1 },
            { ""key"": ""C"", ""type"": ""int"", ""default"": 1 }
        ]";

        [Fact]
        public void Read_ParsesEnumEnables_ByOption()
        {
            var schema = ReadSchema("{ " + EnumEnableFields + @", ""_enables"": { ""mode"": { ""galaxy"": [""A"", ""B""], ""Overlays"": [""B"", ""C""] }, ""Master"": [""A""] } }");

            Assert.Equal(new[] { "Mode", "Master" }, schema.EnabledBy["A"]);
            Assert.Equal(new[] { "Mode" }, schema.EnabledBy["B"]);
            Assert.Equal(new[] { "Galaxy" }, schema.EnabledWhen["A"]["Mode"]);
            Assert.Equal(new[] { "Galaxy", "Overlays" }, schema.EnabledWhen["B"]["Mode"]);
            Assert.Equal(new[] { "Overlays" }, schema.EnabledWhen["C"]["MODE"]);
            Assert.False(schema.EnabledWhen.ContainsKey("Master"));
        }

        [Theory]
        [InlineData(@"""_enables"": { ""Mode"": [""A""] }", "object of")]
        [InlineData(@"""_enables"": { ""Master"": { ""Galaxy"": [""A""] } }", "must list the settings it enables as an array")]
        [InlineData(@"""_enables"": { ""Mode"": { ""Nope"": [""A""] } }", "which the setting does not have")]
        [InlineData(@"""_enables"": { ""Mode"": { ""Galaxy"": ""A"" } }", "array of keys")]
        [InlineData(@"""_enables"": { ""Mode"": { ""Galaxy"": [""Nope""] } }", "not a setting")]
        [InlineData(@"""_enables"": { ""Mode"": { ""Galaxy"": [""Mode""] } }", "lists itself")]
        [InlineData(@"""_enables"": { ""Mode"": { ""Galaxy"": [""Master""] }, ""Master"": [""Mode""] }", "loops")]
        public void Read_RejectsBadEnumEnables(string enables, string expected)
        {
            var ex = Assert.Throws<InvalidDataException>(() => ReadSchema("{ " + EnumEnableFields + ", " + enables + " }"));
            Assert.Contains(expected, ex.Message);
        }

        private const string PreviewFields = @"""Main"": [
            { ""key"": ""Flag"", ""type"": ""bool"", ""default"": true },
            { ""key"": ""Mode"", ""type"": ""enum"", ""options"": [""Sparse"", ""Dense""], ""default"": ""Sparse"" },
            { ""key"": ""Count"", ""type"": ""int"", ""default"": 1 }
        ]";

        [Fact]
        public void Read_ParsesPreviews_SingleImageAndPerOption()
        {
            var schema = ReadSchema("{ " + PreviewFields + @", ""_previews"": { ""flag"": ""flag.png"", ""Mode"": { ""dense"": ""dense.WEBP"" } } }");

            Assert.Equal("flag.png", schema.Fields.Single(f => f.Key == "Flag").Preview);
            ModSettingsField mode = schema.Fields.Single(f => f.Key == "Mode");
            Assert.Null(mode.Preview);
            Assert.Equal("dense.WEBP", mode.OptionPreviews["Dense"]);
            Assert.Null(schema.Fields.Single(f => f.Key == "Count").Preview);
        }

        [Theory]
        [InlineData(@"""_previews"": { ""Nope"": ""p/a.png"" }", "not a setting")]
        [InlineData(@"""_previews"": { ""Flag"": { ""On"": ""a.png"" } }", "not an enum")]
        [InlineData(@"""_previews"": { ""Mode"": { ""Huge"": ""a.png"" } }", "does not have")]
        [InlineData(@"""_previews"": { ""Mode"": { ""Dense"": 3 } }", "image file name")]
        [InlineData(@"""_previews"": { ""Flag"": 3 }", "image file name")]
        [InlineData(@"""_previews"": { ""Flag"": """" }", "empty")]
        [InlineData(@"""_previews"": { ""Flag"": ""../a.png"" }", "just an image file name")]
        [InlineData(@"""_previews"": { ""Flag"": ""p\\a.png"" }", "just an image file name")]
        [InlineData(@"""_previews"": { ""Flag"": ""_previews/a.png"" }", "just an image file name")]
        [InlineData(@"""_previews"": { ""Flag"": ""C:a.png"" }", "just an image file name")]
        [InlineData(@"""_previews"": { ""Flag"": ""a.gif"" }", "PNG, JPG or WebP")]
        [InlineData(@"""_previews"": [ ]", "object")]
        public void Read_RejectsBadPreviews(string previews, string expected)
        {
            var ex = Assert.Throws<InvalidDataException>(() => ReadSchema("{ " + PreviewFields + ", " + previews + " }"));
            Assert.Contains(expected, ex.Message);
        }

        private const string LadderGroup = @"""Rules"": [
            { ""key"": ""A"", ""type"": ""enum"", ""options"": [""Never"", ""High"", ""Low""], ""default"": ""Low"" },
            { ""key"": ""B"", ""type"": ""enum"", ""options"": [""Never"", ""High"", ""Low""], ""default"": ""High"" }
        ]";

        [Fact]
        public void Read_ParsesLadderLayout()
        {
            var schema = ReadSchema("{ " + LadderGroup + @", ""_layouts"": { ""Rules"": { ""kind"": ""ladder"", ""none"": ""Never"", ""short"": { ""High"": ""H"" } } } }");

            var layout = schema.Groups[0].Layout;
            Assert.NotNull(layout);
            Assert.Equal("Never", layout.None);
            Assert.Equal("H", layout.Short["high"]);
        }

        [Theory]
        [InlineData(@"""_layouts"": { ""Missing"": { ""kind"": ""ladder"", ""none"": ""Never"" } }", "Missing")]
        [InlineData(@"""_layouts"": { ""Rules"": { ""kind"": ""grid"", ""none"": ""Never"" } }", "grid")]
        [InlineData(@"""_layouts"": { ""Rules"": { ""kind"": ""ladder"", ""none"": ""Nope"" } }", "Nope")]
        [InlineData(@"""_layouts"": { ""Rules"": { ""kind"": ""ladder"", ""none"": ""Never"", ""short"": { ""Nope"": ""N"" } } }", "not a column")]
        public void Read_RejectsBadLadderLayout(string layouts, string expected)
        {
            var ex = Assert.Throws<InvalidDataException>(() => ReadSchema("{ " + LadderGroup + ", " + layouts + " }"));
            Assert.Contains(expected, ex.Message);
        }

        [Fact]
        public void Read_RejectsLadderWithMismatchedOptions()
        {
            string json = @"{ ""Rules"": [
                { ""key"": ""A"", ""type"": ""enum"", ""options"": [""Never"", ""High""] },
                { ""key"": ""B"", ""type"": ""enum"", ""options"": [""Never"", ""Low""] } ],
                ""_layouts"": { ""Rules"": { ""kind"": ""ladder"", ""none"": ""Never"" } } }";

            var ex = Assert.Throws<InvalidDataException>(() => ReadSchema(json));
            Assert.Contains("not in the order", ex.Message);
        }

        [Fact]
        public void Read_AllowsLadderRowWithFewerOptions()
        {
            string json = @"{ ""Rules"": [
                { ""key"": ""A"", ""type"": ""enum"", ""options"": [""Never"", ""High"", ""Low""] },
                { ""key"": ""B"", ""type"": ""enum"", ""options"": [""High"", ""Low""] } ],
                ""_layouts"": { ""Rules"": { ""kind"": ""ladder"", ""none"": ""Never"" } } }";

            Assert.NotNull(ReadSchema(json).Groups[0].Layout);
        }

        [Fact]
        public void Read_RejectsLadderRowWithReorderedOptions()
        {
            string json = @"{ ""Rules"": [
                { ""key"": ""A"", ""type"": ""enum"", ""options"": [""Never"", ""High"", ""Low""] },
                { ""key"": ""B"", ""type"": ""enum"", ""options"": [""Low"", ""High""] } ],
                ""_layouts"": { ""Rules"": { ""kind"": ""ladder"", ""none"": ""Never"" } } }";

            Assert.Contains("not in the order", Assert.Throws<InvalidDataException>(() => ReadSchema(json)).Message);
        }

        [Fact]
        public void Read_AcceptsNoneLast_AndRejectsNoneInTheMiddle()
        {
            string last = @"{ ""Rules"": [ { ""key"": ""A"", ""type"": ""enum"", ""options"": [""High"", ""Low"", ""Never""] } ],
                ""_layouts"": { ""Rules"": { ""kind"": ""ladder"", ""none"": ""Never"" } } }";
            string middle = last.Replace(@"""High"", ""Low"", ""Never""", @"""High"", ""Never"", ""Low""");

            Assert.NotNull(ReadSchema(last).Groups[0].Layout);
            Assert.Contains("first or the last", Assert.Throws<InvalidDataException>(() => ReadSchema(middle)).Message);
        }

        [Fact]
        public void Read_RejectsLadderWithNonEnumField()
        {
            string json = @"{ ""Rules"": [ { ""key"": ""A"", ""type"": ""bool"" } ],
                ""_layouts"": { ""Rules"": { ""kind"": ""ladder"", ""none"": ""Never"" } } }";

            var ex = Assert.Throws<InvalidDataException>(() => ReadSchema(json));
            Assert.Contains("not an enum", ex.Message);
        }
    }
}
