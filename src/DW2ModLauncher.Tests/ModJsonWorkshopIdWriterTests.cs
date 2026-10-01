using System;
using System.IO;
using DW2ModLauncher.Core.Services;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class ModJsonWorkshopIdWriterTests
    {
        private static string MakeModJson(string content)
        {
            string path = Path.Combine(Path.GetTempPath(), "dw2-modjson-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path, content);
            return path;
        }

        [Fact]
        public void Write_AddsWorkshopId_PreservingOtherFields()
        {
            string path = MakeModJson(@"{ ""displayName"": ""My Mod"", ""version"": ""1.0"" }");
            try
            {
                ModJsonWorkshopIdWriter.Write(path, 123456789);

                string text = File.ReadAllText(path);
                Assert.Contains("\"workshopId\": 123456789", text);
                Assert.Contains("\"displayName\": \"My Mod\"", text);
                Assert.Contains("\"version\": \"1.0\"", text);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Write_OverwritesExistingWorkshopId()
        {
            string path = MakeModJson(@"{ ""displayName"": ""My Mod"", ""workshopId"": 111 }");
            try
            {
                ModJsonWorkshopIdWriter.Write(path, 222);

                string text = File.ReadAllText(path);
                Assert.Contains("\"workshopId\": 222", text);
                Assert.DoesNotContain("111", text);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Write_Throws_WhenModJsonIsNotAnObject()
        {
            string path = MakeModJson("[1, 2, 3]");
            try
            {
                Assert.Throws<InvalidDataException>(() => ModJsonWorkshopIdWriter.Write(path, 1));
            }
            finally { File.Delete(path); }
        }
    }
}
