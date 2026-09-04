using System.Collections.Generic;
using DW2ModLauncher.Core.Services;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class LooseJsonTests
    {
        [Fact]
        public void Parse_ReadsNestedObjectsArraysAndScalars()
        {
            const string json = @"{
                ""displayName"": ""My Mod"",
                ""version"": ""1.2"",
                ""required"": [""Base"", ""Extra""],
                ""launcher"": { ""launchArguments"": ""--foo"" }
            }";

            var root = (Dictionary<string, object>)LooseJson.Parse(json);

            Assert.Equal("My Mod", LooseJson.GetString(root, new[] { "displayName" }, ""));
            Assert.Equal("--foo", LooseJson.GetString(LooseJson.GetDictionary(root, "launcher"), new[] { "launchArguments" }, ""));
            List<string> required = LooseJson.GetStringList(root, new[] { "required" });
            Assert.Equal(new[] { "Base", "Extra" }, required);
        }

        [Fact]
        public void GetString_IsCaseInsensitiveOnKeyName_AndTriesFallbackKeysInOrder()
        {
            var root = (Dictionary<string, object>)LooseJson.Parse(@"{ ""Name"": ""Fallback Hit"" }");

            Assert.Equal("Fallback Hit", LooseJson.GetString(root, new[] { "displayName", "name", "title" }, "default"));
        }

        [Fact]
        public void GetString_ReturnsFallback_WhenKeyMissing()
        {
            var root = (Dictionary<string, object>)LooseJson.Parse(@"{ ""other"": ""x"" }");

            Assert.Equal("default", LooseJson.GetString(root, new[] { "displayName" }, "default"));
        }
    }
}
