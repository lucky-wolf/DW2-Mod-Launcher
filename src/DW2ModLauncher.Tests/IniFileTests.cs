using System.Collections.Generic;
using System.IO;
using DW2ModLauncher.Core.Services;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class IniFileTests
    {
        [Fact]
        public void Read_ParsesKeyValuePairs_AndSkipsCommentsAndBlankLines()
        {
            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, "# comment\r\nEnabled=true\r\n\r\n; another comment\r\nModel=gpt\r\n");

                Dictionary<string, string> values = IniFile.Read(path);

                Assert.Equal("true", values["Enabled"]);
                Assert.Equal("gpt", values["Model"]);
                Assert.Equal(2, values.Count);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void GetBool_TreatsTrueOneYes_AsTrue()
        {
            var d = new Dictionary<string, string> { ["A"] = "true", ["B"] = "1", ["C"] = "yes", ["D"] = "false" };
            Assert.True(IniFile.GetBool(d, "A", false));
            Assert.True(IniFile.GetBool(d, "B", false));
            Assert.True(IniFile.GetBool(d, "C", false));
            Assert.False(IniFile.GetBool(d, "D", true));
            Assert.True(IniFile.GetBool(d, "Missing", true));
        }

        [Fact]
        public void Write_UpdatesExistingKeys_AndAppendsNewOnes()
        {
            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, "Enabled=false\r\nModel=old\r\n");

                IniFile.Write(path, new Dictionary<string, string> { ["Enabled"] = "true", ["NewKey"] = "value" });

                Dictionary<string, string> result = IniFile.Read(path);
                Assert.Equal("true", result["Enabled"]);
                Assert.Equal("old", result["Model"]);
                Assert.Equal("value", result["NewKey"]);
            }
            finally { File.Delete(path); }
        }
    }
}
