using System;
using System.IO;
using DW2ModLauncher.Core.Models;
using DW2ModLauncher.Core.Services;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class ModScannerPreviewTests
    {
        private static ModInfo Scan(string modJson, out string root)
        {
            root = Path.Combine(Path.GetTempPath(), "dw2-scanpreview-" + Guid.NewGuid().ToString("N"));
            string mod = Path.Combine(root, "My Mod");
            Directory.CreateDirectory(mod);
            File.WriteAllText(Path.Combine(mod, "mod.json"), modJson);
            File.WriteAllText(Path.Combine(mod, "Real Image.png"), "x");
            return Assert.Single(ModScanner.ScanMods(root, false, key => key));
        }

        [Fact]
        public void PreviewNamingAMissingFile_HasNoPreview_NotSomeOtherImage()
        {
            ModInfo mod = Scan("{ \"displayName\": \"M\", \"previewImage\": \"Gone.png\" }", out string root);
            try { Assert.True(string.IsNullOrEmpty(mod.PreviewImage)); }
            finally { Directory.Delete(root, true); }
        }

        [Fact]
        public void ModNamingNoPreview_StillGetsTheByConventionImage()
        {
            ModInfo mod = Scan("{ \"displayName\": \"M\" }", out string root);
            try { Assert.EndsWith("Real Image.png", mod.PreviewImage); }
            finally { Directory.Delete(root, true); }
        }

        [Fact]
        public void PreviewNamingAnExistingFile_IsUsed()
        {
            ModInfo mod = Scan("{ \"displayName\": \"M\", \"previewImage\": \"Real Image.png\" }", out string root);
            try { Assert.EndsWith("Real Image.png", mod.PreviewImage); }
            finally { Directory.Delete(root, true); }
        }
    }
}
