using System;
using System.IO;
using DW2ModLauncher.Core.Services.Publishing;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class ModChangelogTests
    {
        private static string MakeFolder(string changelog)
        {
            string dir = Path.Combine(Path.GetTempPath(), "dw2-changelog-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            if (changelog != null) File.WriteAllText(Path.Combine(dir, ModChangelog.FileName), changelog);
            return dir;
        }

        [Fact]
        public void ChangeNote_UsesUnreleasedLines()
        {
            string dir = MakeFolder("## Unreleased\n- one\n- two\n\n## 1.0.0 (2026-01-01)\n- old\n");
            try { Assert.Equal("Version 1.1.0\n\n- one\n- two", ModChangelog.ChangeNote(dir, "1.1.0")); }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ChangeNote_WithoutFileIsJustTheVersion()
        {
            string dir = MakeFolder(null);
            try { Assert.Equal("Version 1.1.0", ModChangelog.ChangeNote(dir, "1.1.0")); }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ChangeNote_RepublishSendsTheExistingSectionAgain()
        {
            string dir = MakeFolder("## Unreleased\n\n## 1.1.0 (2026-02-01)\n- shipped\n");
            try { Assert.Equal("Version 1.1.0\n\n- shipped", ModChangelog.ChangeNote(dir, "1.1.0")); }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void Stamp_NamesTheSectionAndAddsAnEmptyUnreleasedAbove()
        {
            string dir = MakeFolder("# Changelog\n\n## Unreleased\n- one\n\n## 1.0.0 (2026-01-01)\n- old\n");
            try
            {
                Assert.True(ModChangelog.Stamp(dir, "1.1.0", new DateTime(2026, 10, 14)));
                Assert.Equal("# Changelog\n\n## Unreleased\n\n## 1.1.0 (2026-10-14)\n- one\n\n## 1.0.0 (2026-01-01)\n- old\n",
                    File.ReadAllText(ModChangelog.PathFor(dir)));
                Assert.Equal("", ModChangelog.UnreleasedText(dir));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void Stamp_WithNothingUnreleasedChangesNothing()
        {
            string text = "## Unreleased\n\n## 1.0.0 (2026-01-01)\n- old\n";
            string dir = MakeFolder(text);
            try
            {
                Assert.False(ModChangelog.Stamp(dir, "1.1.0", DateTime.Now));
                Assert.Equal(text, File.ReadAllText(ModChangelog.PathFor(dir)));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void Stamp_SameVersionMergesIntoItsSection()
        {
            string dir = MakeFolder("## Unreleased\n- fix\n\n## 1.1.0 (2026-02-01)\n- shipped\n");
            try
            {
                Assert.True(ModChangelog.Stamp(dir, "1.1.0", DateTime.Now));
                Assert.Equal("## Unreleased\n\n## 1.1.0 (2026-02-01)\n- fix\n\n- shipped\n", File.ReadAllText(ModChangelog.PathFor(dir)));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void SetUnreleased_ReplacesTheLinesAndCreatesWhatIsMissing()
        {
            string dir = MakeFolder("## Unreleased\n- old\n\n## 1.0.0 (2026-01-01)\n- shipped\n");
            string empty = MakeFolder(null);
            try
            {
                ModChangelog.SetUnreleased(dir, "- new one\n- new two");
                Assert.Equal("## Unreleased\n- new one\n- new two\n\n## 1.0.0 (2026-01-01)\n- shipped\n", File.ReadAllText(ModChangelog.PathFor(dir)));
                ModChangelog.SetUnreleased(empty, "- first");
                Assert.Equal("- first", ModChangelog.UnreleasedText(empty));
            }
            finally { Directory.Delete(dir, true); Directory.Delete(empty, true); }
        }

        [Fact]
        public void Tokens_ExpandVersionAndDate()
        {
            Assert.Equal("[b]Version 1.2.3[/b] 2026-10-14", DescriptionTokens.Expand("[b]Version {version}[/b] {date}", "1.2.3", new DateTime(2026, 10, 14)));
        }

        [Fact]
        public void Tokens_UnknownPlaceholderIsAnError()
        {
            Assert.Throws<FormatException>(() => DescriptionTokens.Expand("{versoin}", "1.2.3", DateTime.Now));
        }

        [Fact]
        public void Tokens_ChangesBecomeABBCodeList()
        {
            Assert.Equal("Now:\n[list]\n[*]one\n[*]two\n[/list]", DescriptionTokens.Expand("Now:\n{changes}", "1.0.0", DateTime.Now, new[] { "one", "two" }));
        }

        [Fact]
        public void Tokens_ChangesWithNoEntriesIsAnError()
        {
            Assert.Throws<FormatException>(() => DescriptionTokens.Expand("{changes}", "1.0.0", DateTime.Now));
        }

        [Fact]
        public void LatestChanges_PrefersUnreleasedThenTheVersionThenTheNewestRelease()
        {
            string dir = MakeFolder("## Unreleased\n- new\n\n## 1.1.0 (2026-02-01)\n- shipped\n\n## 1.0.0 (2026-01-01)\n- old\n");
            try { Assert.Equal(new[] { "new" }, ModChangelog.LatestChanges(dir, "1.1.0")); }
            finally { Directory.Delete(dir, true); }
            dir = MakeFolder("## Unreleased\n\n## 1.1.0 (2026-02-01)\n- shipped\n\n## 1.0.0 (2026-01-01)\n- old\n");
            try
            {
                Assert.Equal(new[] { "old" }, ModChangelog.LatestChanges(dir, "1.0.0"));
                Assert.Equal(new[] { "shipped" }, ModChangelog.LatestChanges(dir, "9.9.9"));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void Notes_FollowTheVersionSectionWhenThereIsNoUnreleased()
        {
            string dir = MakeFolder("## 1.0.17\n- one\n- two\n\n## 1.0.16\n- old\n");
            try
            {
                Assert.Equal("- one\n- two", ModChangelog.NotesText(dir, "1.0.17"));
                ModChangelog.SetNotes(dir, "1.0.17", "- one\n- two\n- three");
                Assert.Equal("- one\n- two\n- three", ModChangelog.NotesText(dir, "1.0.17"));
                Assert.Equal("- old", ModChangelog.NotesText(dir, "1.0.16"));
                Assert.Equal("", ModChangelog.NotesText(dir, "1.0.18"));
                Assert.Equal("", ModChangelog.UnreleasedText(dir));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void Notes_PreferUnreleasedWhenItHasLines()
        {
            string dir = MakeFolder("## Unreleased\n- new\n\n## 1.0.17\n- shipped\n");
            try
            {
                Assert.Equal("- new", ModChangelog.NotesText(dir, "1.0.17"));
                ModChangelog.SetNotes(dir, "1.0.17", "- newer");
                Assert.Equal("- newer", ModChangelog.UnreleasedText(dir));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void Notes_WithNowhereToGoCreateUnreleased()
        {
            string dir = MakeFolder("## 1.0.16\n- old\n");
            try
            {
                ModChangelog.SetNotes(dir, "1.0.17", "- first");
                Assert.Equal("- first", ModChangelog.UnreleasedText(dir));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void LatestChanges_NoChangelogIsEmpty()
        {
            string dir = MakeFolder(null);
            try { Assert.Empty(ModChangelog.LatestChanges(dir, "1.0.0")); }
            finally { Directory.Delete(dir, true); }
        }
    }
}
