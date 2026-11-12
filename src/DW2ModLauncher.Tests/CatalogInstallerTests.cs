using System;
using System.IO;
using System.IO.Compression;
using DW2ModLauncher.Core.Services.Catalog;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class CatalogInstallerTests : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "dw2-installer-" + Guid.NewGuid().ToString("N"));

        public CatalogInstallerTests() { Directory.CreateDirectory(root); }

        public void Dispose() { Directory.Delete(root, true); }

        private string Zip(params string[] names)
        {
            string path = Path.Combine(root, Guid.NewGuid().ToString("N") + ".zip");
            using (ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create))
                foreach (string name in names)
                    using (StreamWriter w = new StreamWriter(zip.CreateEntry(name).Open())) w.Write("x " + name);
            return path;
        }

        private string ZipWithLauncherJson(string json)
        {
            string path = Path.Combine(root, Guid.NewGuid().ToString("N") + ".zip");
            using (ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                using (StreamWriter w = new StreamWriter(zip.CreateEntry("mod.json").Open())) w.Write("{}");
                using (StreamWriter w = new StreamWriter(zip.CreateEntry("dw2modlauncher.json").Open())) w.Write(json);
            }
            return path;
        }

        private static CatalogEntry Listed() { return new CatalogEntry { Uid = "6f1c2a3e-8b4d-4e5f-9a60-7b1d2c3e4f50", Author = "Mordachai" }; }

        [Fact]
        public void Install_WithEntry_AcceptsAMatchingOrAnUnstatedIdentity()
        {
            CatalogInstaller.Install(ZipWithLauncherJson("{ \"uid\": \"6F1C2A3E-8B4D-4E5F-9A60-7B1D2C3E4F50\", \"author\": \"mordachai\" }"), "A", Mods, null, Stage, Listed());
            CatalogInstaller.Install(ZipWithLauncherJson("{ \"tags\": [\"music\"] }"), "B", Mods, null, Stage, Listed());

            Assert.True(File.Exists(Path.Combine(Mods, "A", "mod.json")));
            Assert.True(File.Exists(Path.Combine(Mods, "B", "mod.json")));
        }

        [Fact]
        public void Install_WithEntry_RefusesAnotherUid_AndLeavesNothing()
        {
            CatalogInstallException error = Assert.Throws<CatalogInstallException>(() =>
                CatalogInstaller.Install(ZipWithLauncherJson("{ \"uid\": \"11111111-2222-4333-8444-555555555555\" }"), "A", Mods, null, Stage, Listed()));

            Assert.Contains("identity", error.Message);
            Assert.False(Directory.Exists(Path.Combine(Mods, "A")));
            Assert.False(Directory.Exists(Stage));
        }

        [Fact]
        public void Install_WithEntry_RefusesAnotherAuthor_AndKeepsTheInstalledCopyOnUpdate()
        {
            string installed = Path.Combine(Mods, "A");
            Directory.CreateDirectory(installed);
            File.WriteAllText(Path.Combine(installed, "old.txt"), "keep");

            CatalogInstallException error = Assert.Throws<CatalogInstallException>(() =>
                CatalogInstaller.Install(ZipWithLauncherJson("{ \"author\": \"Someone Else\" }"), "A", Mods, installed, Stage, Listed()));

            Assert.Contains("author", error.Message);
            Assert.Equal("keep", File.ReadAllText(Path.Combine(installed, "old.txt")));
        }

        [Fact]
        public void Install_WithEntry_AnUnreadableLauncherFileIsReported()
        {
            Assert.Throws<CatalogInstallException>(() => CatalogInstaller.Install(ZipWithLauncherJson("{ not json"), "A", Mods, null, Stage, Listed()));
        }

        [Fact]
        public void Install_LeavesNoWorkFolderBehind_AndTheScannerIgnoresOne()
        {
            CatalogInstaller.Install(Zip("mod.json", "a.xml"), "My Mod", Mods, null, Stage);

            Assert.Equal(new[] { Path.Combine(Mods, "My Mod") }, Directory.GetDirectories(Mods));

            string work = Path.Combine(Mods, CatalogInstaller.WorkPrefix + "install-test");
            Directory.CreateDirectory(work);
            File.WriteAllText(Path.Combine(work, "mod.json"), "{ \"name\": \"Half\" }");
            Assert.All(DW2ModLauncher.Core.Services.ModScanner.ScanMods(Mods, false, key => key), m => Assert.DoesNotContain("install-test", m.Folder));
        }

        [Fact]
        public void Install_ABadZipLeavesNoWorkFolder()
        {
            Assert.Throws<CatalogInstallException>(() => CatalogInstaller.Install(Zip("patches/a.xml"), "My Mod", Mods, null, Stage));

            Assert.Empty(Directory.GetDirectories(Mods));
        }

        private string Mods { get { string m = Path.Combine(root, "mods"); Directory.CreateDirectory(m); return m; } }

        private string Stage { get { return Path.Combine(root, "stage"); } }

        [Fact]
        public void Install_ZipWithoutRootFolder()
        {
            string folder = CatalogInstaller.Install(Zip("mod.json", "patches/a.xml"), "My Mod", Mods, null, Stage);

            Assert.Equal(Path.Combine(Mods, "My Mod"), folder);
            Assert.True(File.Exists(Path.Combine(folder, "mod.json")));
            Assert.True(File.Exists(Path.Combine(folder, "patches", "a.xml")));
            Assert.False(Directory.Exists(Stage));
        }

        [Fact]
        public void Install_RootFolderOfAnyNameIsRemoved()
        {
            string folder = CatalogInstaller.Install(Zip("My Mod v1.2/mod.json", "My Mod v1.2/patches/a.xml"), "My Mod", Mods, null, Stage);

            Assert.True(File.Exists(Path.Combine(Mods, "My Mod", "mod.json")));
            Assert.True(File.Exists(Path.Combine(folder, "patches", "a.xml")));
            Assert.False(Directory.Exists(Path.Combine(Mods, "My Mod v1.2")));
        }

        [Fact]
        public void Install_ModWithOnlyAPatchesFolderIsNotMistakenForARoot()
        {
            // no mod.json at the top and none inside "patches": not a mod
            Assert.Throws<CatalogInstallException>(() => CatalogInstaller.Install(Zip("patches/a.xml", "patches/b.xml"), "My Mod", Mods, null, Stage));
            Assert.False(Directory.Exists(Path.Combine(Mods, "My Mod")));
        }

        [Fact]
        public void Install_RefusesEntriesOutsideTheFolder()
        {
            Assert.Throws<CatalogInstallException>(() => CatalogInstaller.Install(Zip("mod.json", "../evil.txt"), "My Mod", Mods, null, Stage));
            Assert.Throws<CatalogInstallException>(() => CatalogInstaller.Install(Zip("mod.json", "/evil.txt"), "My Mod", Mods, null, Stage));
            Assert.Throws<CatalogInstallException>(() => CatalogInstaller.Install(Zip("mod.json", "a/../../evil.txt"), "My Mod", Mods, null, Stage));
            Assert.Throws<CatalogInstallException>(() => CatalogInstaller.Install(Zip("mod.json", "C:evil.txt"), "My Mod", Mods, null, Stage));
            Assert.False(Directory.Exists(Path.Combine(Mods, "My Mod")));
            Assert.False(File.Exists(Path.Combine(root, "evil.txt")));
        }

        [Fact]
        public void Install_LeavesAnUnrelatedFolderOfTheSameNameAlone()
        {
            string existing = Path.Combine(Mods, "My Mod");
            Directory.CreateDirectory(existing);
            File.WriteAllText(Path.Combine(existing, "mine.txt"), "keep");

            Assert.Throws<CatalogInstallException>(() => CatalogInstaller.Install(Zip("mod.json"), "My Mod", Mods, null, Stage));

            Assert.Equal("keep", File.ReadAllText(Path.Combine(existing, "mine.txt")));
        }

        [Fact]
        public void Install_RefusesAFolderNameThatIsAPath()
        {
            Assert.Throws<CatalogInstallException>(() => CatalogInstaller.Install(Zip("mod.json"), "..", Mods, null, Stage));
            Assert.Throws<CatalogInstallException>(() => CatalogInstaller.Install(Zip("mod.json"), "a/b", Mods, null, Stage));
        }

        [Fact]
        public void Verify_ChecksSizeAndHash()
        {
            CatalogEntry entry = new CatalogEntry { Size = 10, Sha256 = "ABCDEF" };

            CatalogInstaller.Verify(entry, 10, "abcdef");
            Assert.Throws<CatalogInstallException>(() => CatalogInstaller.Verify(entry, 11, "abcdef"));
            Assert.Throws<CatalogInstallException>(() => CatalogInstaller.Verify(entry, 10, "abcdee"));
        }
    }
}
