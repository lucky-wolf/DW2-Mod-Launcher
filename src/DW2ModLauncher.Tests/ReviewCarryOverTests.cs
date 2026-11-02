using System.Collections.Generic;
using System.IO;
using DW2ModLauncher.Core.Models;
using DW2ModLauncher.Core.Services;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class ReviewCarryOverTests
    {
        private static ModInfo Mod(string id, string version) => new ModInfo { Id = id, DisplayName = id, Version = version };

        [Fact]
        public void AcknowledgementsMoveToTheNewVersion_AndTheOldOnesStay()
        {
            ModInfo a = Mod("a", "1.0.0"), b = Mod("b", "2.0.0");
            LauncherSettings settings = new LauncherSettings();
            settings.AcknowledgedHooks.Add(HookOverlapAnalyzer.AcknowledgementKey("G:M()", a, b));
            ConflictAcknowledgement.Acknowledge(new Dictionary<string, List<ModInfo>> { ["data/x.xml"] = new List<ModInfo> { a, b } }, settings.AcknowledgedFileConflicts);

            ReviewCarryOver carry = ReviewCarryOver.Apply(settings, a, new[] { a, b }, "1.0.0", "1.0.1");

            Assert.True(carry.SettingsChanged);
            a.Version = "1.0.1";
            Assert.Contains(HookOverlapAnalyzer.AcknowledgementKey("G:M()", a, b), settings.AcknowledgedHooks);
            Assert.Equal(2, settings.AcknowledgedHooks.Count);
            Assert.Empty(ConflictAcknowledgement.Pending(new Dictionary<string, List<ModInfo>> { ["data/x.xml"] = new List<ModInfo> { a, b } }, settings.AcknowledgedFileConflicts));
        }

        [Fact]
        public void APinOnTheOldVersionGainsTheNewOne_ThenTheOldIsPruned()
        {
            string folder = Path.Combine(Path.GetTempPath(), "carry-" + Path.GetRandomFileName());
            Directory.CreateDirectory(folder);
            try
            {
                ModInfo a = Mod("a", "1.0.0");
                ModInfo b = Mod("b", "2.0.0");
                b.Folder = folder;
                b.HooksCoordinatedWith = new List<string> { "a@1.0.0", "other@3" };

                ReviewCarryOver carry = ReviewCarryOver.Apply(new LauncherSettings(), a, new[] { a, b }, "1.0.0", "1.0.1");
                Assert.Equal(new[] { "a@1.0.0", "other@3", "a@1.0.1" }, b.HooksCoordinatedWith);

                carry.PruneOld();
                Assert.Equal(new[] { "other@3", "a@1.0.1" }, b.HooksCoordinatedWith);
                Assert.Empty(carry.Problems);
            }
            finally { Directory.Delete(folder, true); }
        }

        [Fact]
        public void NothingChangesWhenTheVersionStays()
        {
            ModInfo a = Mod("a", "1.0.0"), b = Mod("b", "2.0.0");
            LauncherSettings settings = new LauncherSettings();
            settings.AcknowledgedHooks.Add(HookOverlapAnalyzer.AcknowledgementKey("G:M()", a, b));

            Assert.False(ReviewCarryOver.Apply(settings, a, new[] { a, b }, "1.0.0", "1.0.0").SettingsChanged);
            Assert.Single(settings.AcknowledgedHooks);
        }
    }
}
