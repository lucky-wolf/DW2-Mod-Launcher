using System.Linq;
using System.Text.Json;
using DW2ModLauncher.XmlPatching;
using Xunit;

namespace DW2ModLauncher.Tests.XmlPatching
{
    /// <summary>keymap.json is a published contract (a release asset other tools read): its shape must not change by accident.</summary>
    public class KeyMapTests
    {
        [Fact]
        public void ToJson_Splits_Entities_From_Items_And_Loses_Nothing()
        {
            using JsonDocument doc = JsonDocument.Parse(KeyMap.Default.ToJson());
            JsonElement root = doc.RootElement;

            Assert.Equal(1, root.GetProperty("format").GetInt32());
            JsonElement entities = root.GetProperty("entities");
            JsonElement items = root.GetProperty("items");

            Assert.Equal("RaceId", entities.GetProperty("Race").GetString());
            Assert.Equal("Type", items.GetProperty("Bonus").GetString());
            Assert.False(entities.TryGetProperty("Bonus", out _));
            Assert.False(items.TryGetProperty("Race", out _));

            // Every key the patcher knows is published, exactly once, with the same value.
            foreach (JsonProperty p in entities.EnumerateObject().Concat(items.EnumerateObject()))
            {
                Assert.True(KeyMap.Default.TryGetKey(p.Name, out string key), p.Name);
                Assert.Equal(key, p.Value.GetString());
            }
            Assert.True(entities.EnumerateObject().Count() >= 20 && items.EnumerateObject().Count() >= 15);
        }

        [Fact]
        public void ToJson_Is_Sorted_And_Stable()
        {
            string json = KeyMap.Default.ToJson();
            Assert.Equal(json, KeyMap.Default.ToJson());
            string[] names = JsonDocument.Parse(json).RootElement.GetProperty("entities").EnumerateObject().Select(p => p.Name).ToArray();
            Assert.Equal(names.OrderBy(n => n, System.StringComparer.Ordinal), names);
        }
    }
}
