using System.IO;
using DW2ModLauncher.Core.Services.Updates;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class UpdateApplierTests
    {
        [Fact]
        public void CopiesStagedFilesOverTheInstallAndKeepsOthers()
        {
            string root = Path.Combine(Path.GetTempPath(), "dw2-update-test-" + Path.GetRandomFileName());
            string staged = Path.Combine(root, "staged"), install = Path.Combine(root, "install");
            try
            {
                Directory.CreateDirectory(Path.Combine(staged, "Loader"));
                Directory.CreateDirectory(install);
                File.WriteAllText(Path.Combine(staged, "app.exe"), "new");
                File.WriteAllText(Path.Combine(staged, "Loader", "l.dll"), "new");
                File.WriteAllText(Path.Combine(install, "app.exe"), "old");
                File.WriteAllText(Path.Combine(install, "keep.txt"), "mine");

                // A pid that can't exist counts as "already exited". The relaunch fails (the "exe" isn't one), which is fine here.
                UpdateApplier.Run(new[] { LauncherUpdater.Flag, int.MaxValue.ToString(), staged, install, "app.exe" });

                Assert.Equal("new", File.ReadAllText(Path.Combine(install, "app.exe")));
                Assert.Equal("new", File.ReadAllText(Path.Combine(install, "Loader", "l.dll")));
                Assert.Equal("mine", File.ReadAllText(Path.Combine(install, "keep.txt")));
            }
            finally { Directory.Delete(root, true); }
        }
    }
}
