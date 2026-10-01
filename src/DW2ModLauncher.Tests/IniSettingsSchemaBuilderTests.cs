using System;
using System.IO;
using System.Text.Json.Nodes;
using DW2ModLauncher.Core.Models;
using DW2ModLauncher.Core.Services;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class IniSettingsSchemaBuilderTests
    {
        private static string MakeIni(string content)
        {
            string path = Path.Combine(Path.GetTempPath(), "dw2-ini-" + Guid.NewGuid().ToString("N") + ".ini");
            File.WriteAllText(path, content);
            return path;
        }

        [Fact]
        public void BuildSchema_InfersBoolType_AndReadsValue()
        {
            string ini = MakeIni("Enabled=true\r\n");
            try
            {
                ModSettingsSchema schema = IniSettingsSchemaBuilder.BuildSchema(ini, out JsonObject values);

                Assert.Single(schema.Fields);
                Assert.Equal("bool", schema.Fields[0].Type);
                Assert.True(values["Enabled"].GetValue<bool>());
            }
            finally { File.Delete(ini); }
        }

        [Fact]
        public void BuildSchema_InfersLanguageEnum()
        {
            string ini = MakeIni("Language=ja\r\n");
            try
            {
                ModSettingsSchema schema = IniSettingsSchemaBuilder.BuildSchema(ini, out JsonObject values);

                Assert.Equal("enum", schema.Fields[0].Type);
                Assert.Contains("ja", schema.Fields[0].Options);
                Assert.Contains("en", schema.Fields[0].Options);
                Assert.Equal("ja", values["Language"].GetValue<string>());
            }
            finally { File.Delete(ini); }
        }

        [Fact]
        public void BuildSchema_UsesPrecedingComment_AsDescription()
        {
            string ini = MakeIni("# Controls the volume\r\nVolume=0.5\r\n");
            try
            {
                ModSettingsSchema schema = IniSettingsSchemaBuilder.BuildSchema(ini, out _);

                Assert.Equal("Controls the volume", schema.Fields[0].Description);
                Assert.Equal("string", schema.Fields[0].Type);
            }
            finally { File.Delete(ini); }
        }

        [Fact]
        public void BuildSchema_SkipsCommentsAndBlankLines_WithoutAKey()
        {
            string ini = MakeIni("# a stray comment\r\n\r\nNotAKeyValueLine\r\nName=Value\r\n");
            try
            {
                ModSettingsSchema schema = IniSettingsSchemaBuilder.BuildSchema(ini, out JsonObject values);

                Assert.Single(schema.Fields);
                Assert.Equal("Name", schema.Fields[0].Key);
                Assert.Null(schema.Fields[0].Description);
                Assert.Equal("Value", values["Name"].GetValue<string>());
            }
            finally { File.Delete(ini); }
        }
    }
}
