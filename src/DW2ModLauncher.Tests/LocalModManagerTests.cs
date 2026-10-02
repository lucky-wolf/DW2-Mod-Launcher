using System;
using System.IO;
using DW2ModLauncher.Core.Models;
using DW2ModLauncher.Core.Services;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class LocalModManagerTests
    {
        private static string MakeRoot()
        {
            string root = Path.Combine(Path.GetTempPath(), "dw2-localmod-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

        [Theory]
        [InlineData("My Mod", "My_Mod")]
        [InlineData("  My   Cool  Mod  ", "My_Cool_Mod")]
        [InlineData("Part 2: The Return?", "Part_2_The_Return")]
        [InlineData("a/b\\c", "a_b_c")]
        [InlineData("Ends with dot.", "Ends_with_dot")]
        [InlineData("GalCivMusic", "GalCivMusic")]
        [InlineData("", "NewMod")]
        [InlineData("   ", "NewMod")]
        [InlineData("???", "NewMod")]
        [InlineData("CON", "_CON")]
        [InlineData("nul.txt", "_nul.txt")]
        public void FolderNameFor_Sanitizes(string input, string expected)
        {
            Assert.Equal(expected, LocalModManager.FolderNameFor(input));
        }

        [Fact]
        public void Create_WritesModJsonWithOriginalNameInSanitizedFolder()
        {
            string root = MakeRoot();
            try
            {
                string folder = LocalModManager.Create(root, "My Mod: Part 2");

                Assert.Equal(Path.Combine(root, "My_Mod_Part_2"), folder);
                string text = File.ReadAllText(Path.Combine(folder, "mod.json"));
                Assert.Contains("\"displayName\": \"My Mod: Part 2\"", text);
                Assert.Contains("\"version\": \"1.0.0\"", text);
            }
            finally { Directory.Delete(root, true); }
        }

        [Fact]
        public void Create_CreatedModIsFoundByTheScanner()
        {
            string root = MakeRoot();
            try
            {
                LocalModManager.Create(root, "Scan Me");

                ModInfo mod = Assert.Single(ModScanner.ScanMods(root, false, key => key));
                Assert.Equal("Scan Me", mod.DisplayName);
                Assert.Equal("mods/Scan_Me", mod.ActiveToken);
            }
            finally { Directory.Delete(root, true); }
        }

        [Fact]
        public void Create_Throws_WhenFolderAlreadyExists_AndLeavesItAlone()
        {
            string root = MakeRoot();
            try
            {
                string existing = Path.Combine(root, "Taken");
                Directory.CreateDirectory(existing);
                File.WriteAllText(Path.Combine(existing, "keep.txt"), "x");

                Assert.Throws<IOException>(() => LocalModManager.Create(root, "Taken"));
                Assert.True(File.Exists(Path.Combine(existing, "keep.txt")));
                Assert.False(File.Exists(Path.Combine(existing, "mod.json")));
            }
            finally { Directory.Delete(root, true); }
        }

        [Fact]
        public void Create_Throws_ForBlankName()
        {
            string root = MakeRoot();
            try { Assert.Throws<ArgumentException>(() => LocalModManager.Create(root, "  ")); }
            finally { Directory.Delete(root, true); }
        }

        [Fact]
        public void Delete_RemovesLocalModFolder()
        {
            string root = MakeRoot();
            try
            {
                string folder = LocalModManager.Create(root, "Doomed");
                File.WriteAllText(Path.Combine(folder, "extra.txt"), "x");
                ModInfo mod = new ModInfo { Folder = folder, IsWorkshop = false };

                Assert.True(LocalModManager.CanDelete(mod, root));
                LocalModManager.Delete(mod, root);

                Assert.False(Directory.Exists(folder));
                Assert.True(Directory.Exists(root));
            }
            finally { Directory.Delete(root, true); }
        }

        [Fact]
        public void Delete_Refuses_WorkshopMod()
        {
            string root = MakeRoot();
            try
            {
                string folder = Path.Combine(root, "123456");
                Directory.CreateDirectory(folder);
                ModInfo mod = new ModInfo { Folder = folder, IsWorkshop = true };

                Assert.False(LocalModManager.CanDelete(mod, root));
                Assert.Throws<InvalidOperationException>(() => LocalModManager.Delete(mod, root));
                Assert.True(Directory.Exists(folder));
            }
            finally { Directory.Delete(root, true); }
        }

        [Fact]
        public void Delete_Refuses_FolderOutsideManagedRoot_AndTheRootItself()
        {
            string root = MakeRoot();
            string other = MakeRoot();
            try
            {
                Assert.False(LocalModManager.CanDelete(new ModInfo { Folder = other }, root));
                Assert.False(LocalModManager.CanDelete(new ModInfo { Folder = root }, root));
                // A nested folder is not a Mod folder of the root either.
                string nested = Path.Combine(root, "Mod", "Inner");
                Directory.CreateDirectory(nested);
                Assert.False(LocalModManager.CanDelete(new ModInfo { Folder = nested }, root));
                Assert.False(LocalModManager.CanDelete(new ModInfo { Folder = null }, root));
                Assert.False(LocalModManager.CanDelete(null, root));
            }
            finally
            {
                Directory.Delete(root, true);
                Directory.Delete(other, true);
            }
        }
    }
}
