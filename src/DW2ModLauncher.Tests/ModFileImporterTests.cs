using System;
using System.IO;
using DW2ModLauncher.Core.Services;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class ModFileImporterTests : IDisposable
    {
        private readonly string dir = Path.Combine(Path.GetTempPath(), "dw2-import-" + Guid.NewGuid().ToString("N"));
        private string Mod { get { return Path.Combine(dir, "mod"); } }
        private string Elsewhere { get { return Path.Combine(dir, "elsewhere"); } }

        public ModFileImporterTests()
        {
            Directory.CreateDirectory(Path.Combine(Mod, "docs"));
            Directory.CreateDirectory(Elsewhere);
        }

        public void Dispose() { Directory.Delete(dir, true); }

        [Fact]
        public void IsInside_AcceptsModFilesAndSubfolders_RejectsOutsideAndSiblingPrefix()
        {
            Assert.True(ModFileImporter.IsInside(Mod, Path.Combine(Mod, "a.txt")));
            Assert.True(ModFileImporter.IsInside(Mod, Path.Combine(Mod, "docs", "a.txt")));
            Assert.False(ModFileImporter.IsInside(Mod, Path.Combine(Elsewhere, "a.txt")));
            // "mod2" shares the "mod" prefix but is a different folder.
            Assert.False(ModFileImporter.IsInside(Mod, Path.Combine(dir, "mod2", "a.txt")));
            Assert.False(ModFileImporter.IsInside(Mod, Path.Combine(Mod, "..", "elsewhere", "a.txt")));
        }

        [Fact]
        public void RelativePath_UsesForwardSlashes()
        {
            Assert.Equal("docs/a.txt", ModFileImporter.RelativePath(Mod, Path.Combine(Mod, "docs", "a.txt")));
        }

        [Fact]
        public void CopyIntoMod_CopiesToTheModRoot()
        {
            string source = Path.Combine(Elsewhere, "poster.png");
            File.WriteAllText(source, "image");

            Assert.Equal("poster.png", ModFileImporter.CopyIntoMod(Mod, source));
            Assert.Equal("image", File.ReadAllText(Path.Combine(Mod, "poster.png")));
            Assert.True(File.Exists(source));
        }

        [Fact]
        public void CopyIntoMod_ReusesIdenticalFile_ButNeverOverwritesADifferentOne()
        {
            File.WriteAllText(Path.Combine(Mod, "poster.png"), "original");
            string same = Path.Combine(Elsewhere, "poster.png");
            File.WriteAllText(same, "original");
            Assert.Equal("poster.png", ModFileImporter.CopyIntoMod(Mod, same));

            File.WriteAllText(same, "different");
            Assert.Equal("poster (2).png", ModFileImporter.CopyIntoMod(Mod, same));
            Assert.Equal("original", File.ReadAllText(Path.Combine(Mod, "poster.png")));
            Assert.Equal("different", File.ReadAllText(Path.Combine(Mod, "poster (2).png")));

            // Same different file again: the earlier copy is identical, so it is reused rather than creating (3).
            Assert.Equal("poster (2).png", ModFileImporter.CopyIntoMod(Mod, same));
        }

        [Fact]
        public void FindIdentical_AndUniquePath_LookAtTheSameNamedFileInTheMod()
        {
            string source = Path.Combine(Elsewhere, "poster.png");
            File.WriteAllText(source, "image");
            Assert.Null(ModFileImporter.FindIdentical(Mod, source));
            Assert.Equal(Path.Combine(Mod, "poster.png"), ModFileImporter.UniquePath(Mod, source));

            File.WriteAllText(Path.Combine(Mod, "poster.png"), "image");
            Assert.Equal(Path.Combine(Mod, "poster.png"), ModFileImporter.FindIdentical(Mod, source));
            Assert.Equal(Path.Combine(Mod, "poster (2).png"), ModFileImporter.UniquePath(Mod, source));

            File.WriteAllText(Path.Combine(Mod, "poster.png"), "other");
            Assert.Null(ModFileImporter.FindIdentical(Mod, source));
        }
    }
}
