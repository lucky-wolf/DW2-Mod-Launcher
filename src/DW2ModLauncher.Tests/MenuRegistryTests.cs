extern alias loader;
using System.Collections.Generic;
using loader::DW2ModLauncher.Loader;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class MenuRegistryTests
    {
        [Fact]
        public void LauncherIsFirstThenAlphabeticalByLabel()
        {
            var r = new MenuRegistry();
            r.Add("ZMod", "Zeta", () => { });
            r.Add("AMod", "alpha", () => { });
            r.Add(MenuRegistry.LauncherId, "Zzz Launcher", () => { });

            List<MenuEntrySnapshot> all = r.Snapshot();
            Assert.Equal(new[] { MenuRegistry.LauncherId, "AMod", "ZMod" }, all.ConvertAll(e => e.Id));
        }

        [Fact]
        public void AddReplacesAndKeepsEnabledState()
        {
            var r = new MenuRegistry();
            r.Add("Mod", "Old", () => { });
            r.SetEnabled("Mod", false);
            int clicked = 0;
            r.Add("Mod", " New ", () => clicked++);

            MenuEntrySnapshot e = Assert.Single(r.Snapshot());
            Assert.Equal("New", e.Label);
            Assert.False(e.Enabled);
            e.OnClick();
            Assert.Equal(1, clicked);
        }

        [Fact]
        public void RevisionOnlyMovesOnRealChanges()
        {
            var r = new MenuRegistry();
            r.Add("Mod", "Mod", () => { });
            long rev = r.Revision;
            r.SetEnabled("Mod", true);
            r.Remove("Nope");
            Assert.Equal(rev, r.Revision);
            r.Remove("Mod");
            Assert.True(r.Revision > rev);
            Assert.Empty(r.Snapshot());
        }

        [Fact]
        public void IgnoresMissingIdOrHandlerAndBlankLabelFallsBackToId()
        {
            var r = new MenuRegistry();
            r.Add("", "x", () => { });
            r.Add("Mod", "x", null);
            Assert.Empty(r.Snapshot());
            r.Add("Mod", "  ", () => { });
            Assert.Equal("Mod", Assert.Single(r.Snapshot()).Label);
        }

        [Fact]
        public void ModMenuSwallowsHandlerExceptions()
        {
            var r = new MenuRegistry();
            ModMenu.Add("ThrowingMod", "Throws", () => throw new System.InvalidOperationException());
            MenuEntrySnapshot e = ModMenu.Registry.Snapshot().Find(x => x.Id == "ThrowingMod");
            e.OnClick();
            ModMenu.Remove("ThrowingMod");
        }
    }
}
