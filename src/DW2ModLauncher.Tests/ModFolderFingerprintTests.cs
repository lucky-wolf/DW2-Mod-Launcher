using System.IO;
using DW2ModLauncher.Core.Services;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class ModFolderFingerprintTests
    {
        private static string TempRoot()
        {
            string root = Path.Combine(Path.GetTempPath(), "fp-" + Path.GetRandomFileName());
            Directory.CreateDirectory(Path.Combine(root, "ModA"));
            File.WriteAllText(Path.Combine(root, "ModA", "changelog.txt"), "one");
            return root;
        }

        [Fact]
        public void SameContentsGiveSameFingerprint()
        {
            string root = TempRoot();
            try { Assert.Equal(ModFolderFingerprint.Compute(new[] { root }), ModFolderFingerprint.Compute(new[] { root })); }
            finally { Directory.Delete(root, true); }
        }

        [Fact]
        public void EditingNestedFileChangesFingerprint()
        {
            string root = TempRoot();
            try
            {
                string before = ModFolderFingerprint.Compute(new[] { root });
                File.WriteAllText(Path.Combine(root, "ModA", "changelog.txt"), "two, longer");
                Assert.NotEqual(before, ModFolderFingerprint.Compute(new[] { root }));
            }
            finally { Directory.Delete(root, true); }
        }

        [Fact]
        public void NewModFolderChangesFingerprint()
        {
            string root = TempRoot();
            try
            {
                string before = ModFolderFingerprint.Compute(new[] { root });
                Directory.CreateDirectory(Path.Combine(root, "ModB"));
                Assert.NotEqual(before, ModFolderFingerprint.Compute(new[] { root }));
            }
            finally { Directory.Delete(root, true); }
        }

        [Fact]
        public void GitFolderAndRootFilesAreIgnored()
        {
            string root = TempRoot();
            try
            {
                string before = ModFolderFingerprint.Compute(new[] { root });
                Directory.CreateDirectory(Path.Combine(root, "ModA", ".git"));
                File.WriteAllText(Path.Combine(root, "ModA", ".git", "index"), "x");
                File.WriteAllText(Path.Combine(root, "mods.json"), "{}");
                Assert.Equal(before, ModFolderFingerprint.Compute(new[] { root }));
            }
            finally { Directory.Delete(root, true); }
        }
    }
}
