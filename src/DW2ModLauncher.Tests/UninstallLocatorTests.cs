using System.Collections.Generic;
using DW2ModLauncher.Core.Services;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class UninstallLocatorTests
    {
        [Theory]
        [InlineData("\"C:\\Games\\Distant Worlds 2\\unins000.exe\" /SILENT", "C:\\Games\\Distant Worlds 2")]
        [InlineData("C:\\Games\\DW2\\unins000.exe /SILENT", "C:\\Games\\DW2")]
        [InlineData("C:\\Games\\DW2\\DistantWorlds2.exe,0", "C:\\Games\\DW2")]
        [InlineData("MsiExec.exe /X{ABC}", "")]
        [InlineData("", "")]
        [InlineData("\"unterminated", "")]
        public void FolderOfProgram_ReadsTheProgramsFolder(string text, string expected)
        {
            Assert.Equal(expected, UninstallLocator.FolderOfProgram(text));
        }

        [Fact]
        public void CandidatesFrom_UsesInstallLocationThenUninstallerThenIcon_OnlyForDw2()
        {
            var entries = new List<UninstallLocator.InstalledProgram>
            {
                new UninstallLocator.InstalledProgram { DisplayName = "Something Else", InstallLocation = "C:\\Other" },
                new UninstallLocator.InstalledProgram { DisplayName = "Distant Worlds 2", InstallLocation = "D:\\DW2\\", UninstallString = "\"E:\\Unins\\unins000.exe\"", DisplayIcon = "D:\\DW2\\DistantWorlds2.exe" }
            };

            Assert.Equal(new[] { "D:\\DW2", "E:\\Unins" }, UninstallLocator.CandidatesFrom(entries));
        }

        [Fact]
        public void CandidatesFrom_UsesTheUninstallerFolderWhenNoInstallLocation()
        {
            var entries = new List<UninstallLocator.InstalledProgram>
            {
                new UninstallLocator.InstalledProgram { DisplayName = "distant worlds 2 version 1.2", UninstallString = "\"C:\\Matrix\\DW2\\unins000.exe\"" }
            };

            Assert.Equal(new[] { "C:\\Matrix\\DW2" }, UninstallLocator.CandidatesFrom(entries));
        }
    }
}
