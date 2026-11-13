using System.IO;
using DW2ModLauncher.Core.Models;
using DW2ModLauncher.Core.Services;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class SteamSwitchTests
    {
        [Theory]
        [InlineData(true, true)]
        [InlineData(false, false)]
        public void Resolve_NeverDecided_FollowsWhetherSteamIsInstalled(bool installed, bool expected)
        {
            LauncherSettings s = new LauncherSettings();

            SteamSwitch.Resolve(s, installed);

            Assert.Equal(expected, s.SteamEnabled);
        }

        [Theory]
        [InlineData(true, false)]
        [InlineData(false, true)]
        public void Resolve_KeepsAChoiceTheUserMade(bool chosen, bool installed)
        {
            LauncherSettings s = new LauncherSettings { SteamEnabled = chosen };

            SteamSwitch.Resolve(s, installed);

            Assert.Equal(chosen, s.SteamEnabled);
        }

        [Fact]
        public void EffectiveWorkshopRoot_IsBlankUnlessSteamIsOn()
        {
            LauncherSettings s = new LauncherSettings { WorkshopRoot = "/w" };
            Assert.Equal("", s.EffectiveWorkshopRoot);
            s.SteamEnabled = false;
            Assert.Equal("", s.EffectiveWorkshopRoot);
            s.SteamEnabled = true;
            Assert.Equal("/w", s.EffectiveWorkshopRoot);
        }

        [Fact]
        public void Validate_DoesNotCheckTheWorkshopFolderWhenSteamIsOff()
        {
            string dir = Path.Combine(Path.GetTempPath(), "dw2ml-ss-" + Path.GetRandomFileName());
            string missing = Path.Combine(dir, "nope");
            Assert.Equal(SettingsProblem.WorkshopFolderMissing, LauncherSettingsStore.Validate("", missing, ""));
            Assert.Equal(SettingsProblem.None, LauncherSettingsStore.Validate("", missing, "", "", false));
        }

        [Fact]
        public void SettingsRoundTrip_KeepsAllThreeStatesAndIgnoresTheComputedRoot()
        {
            string dir = Path.Combine(Path.GetTempPath(), "dw2ml-ss-" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            try
            {
                LauncherSettingsStore store = new LauncherSettingsStore(Path.Combine(dir, "s.json"));
                foreach (bool? state in new bool?[] { null, true, false })
                {
                    store.Save(new LauncherSettings { SteamEnabled = state, WorkshopRoot = "/w" });
                    LauncherSettings loaded = store.Load();
                    Assert.Equal(state, loaded.SteamEnabled);
                    Assert.DoesNotContain("EffectiveWorkshopRoot", File.ReadAllText(Path.Combine(dir, "s.json")));
                }
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void PublisherFactory_RefusesToCreateAPublisherWhileSteamIsOff()
        {
            bool before = DW2ModLauncher.Core.Services.Publishing.ModPublisherFactory.Enabled;
            try
            {
                DW2ModLauncher.Core.Services.Publishing.ModPublisherFactory.Enabled = false;
                Assert.Throws<System.InvalidOperationException>(() => DW2ModLauncher.Core.Services.Publishing.ModPublisherFactory.Create(1531540));
            }
            finally { DW2ModLauncher.Core.Services.Publishing.ModPublisherFactory.Enabled = before; }
        }

        [Theory]
        [InlineData(true, true, true)]
        [InlineData(false, true, true)]
        [InlineData(true, false, true)]
        [InlineData(false, false, false)]
        public void CanStart_LinuxNeedsSteam_WindowsDoesNot(bool steam, bool windows, bool expected)
        {
            Assert.Equal(expected, GameLauncher.CanStart(steam, windows));
        }

        [Fact]
        public void Detect_LeavesTheWorkshopFolderAloneWhenSteamIsOff()
        {
            LauncherSettings s = new LauncherSettings { SteamEnabled = false, GameRoot = "", WorkshopRoot = "", ManagedModsRoot = "" };

            PathDetector.Detect(s, false);

            Assert.Equal("", s.WorkshopRoot);
        }
    }
}
