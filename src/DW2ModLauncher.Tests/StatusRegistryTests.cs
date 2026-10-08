extern alias loader;
using System.Collections.Generic;
using loader::DW2ModLauncher.Loader;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class StatusRegistryTests
    {
        static StatusRegistry NewRegistry() => new StatusRegistry();

        static ModStatusSnapshot Only(StatusRegistry r)
        {
            List<ModStatusSnapshot> all = r.Snapshot();
            Assert.Single(all);
            return all[0];
        }

        [Fact]
        public void RegisterIsIdempotentAndMergesNonEmptyValues()
        {
            StatusRegistry r = NewRegistry();
            r.ReportLoad("Mod", "Mod", "1.0.0", "10-06 15:05", null);
            r.Register("Mod", null, "", "");
            r.Register("Mod", "Nice Name", "1.0.1", null);

            ModStatusSnapshot m = Only(r);
            Assert.Equal("Nice Name", m.Name);
            Assert.Equal("1.0.1", m.Version);
            Assert.Equal("10-06 15:05", m.Built);
        }

        [Fact]
        public void CallsBeforeRegisterCreateTheRowInOrder()
        {
            StatusRegistry r = NewRegistry();
            r.SetLevel("B", StatusLevel.Ok, null);
            r.Installed("A", "Hubs");

            List<ModStatusSnapshot> all = r.Snapshot();
            Assert.Equal(new[] { "B", "A" }, new[] { all[0].Id, all[1].Id });
            Assert.Equal("B", all[0].Name);
        }

        [Fact]
        public void LoadFailureIsAnError()
        {
            StatusRegistry r = NewRegistry();
            r.ReportLoad("Mod", "Mod", null, null, "DLL not found\nsecond line");

            ModStatusSnapshot m = Only(r);
            Assert.Equal(StatusLevel.Error, m.Level);
            Assert.Equal("DLL not found", m.LoadError);
        }

        [Fact]
        public void LevelIsTheWorstOfExplicitErrorsAndDetails()
        {
            StatusRegistry r = NewRegistry();
            r.SetLevel("Mod", StatusLevel.Info, "hello");
            Assert.Equal(StatusLevel.Info, Only(r).Level);

            r.Detail("Mod", "cap", "3 ships capped", StatusLevel.Warn);
            Assert.Equal(StatusLevel.Warn, Only(r).Level);

            r.Error("Mod", "FuelFirst failed to install\nstack trace");
            ModStatusSnapshot m = Only(r);
            Assert.Equal(StatusLevel.Error, m.Level);
            Assert.Equal(new[] { "FuelFirst failed to install" }, m.Errors);
        }

        [Fact]
        public void DuplicateErrorsAreKeptOnce()
        {
            StatusRegistry r = NewRegistry();
            r.Error("Mod", "boom");
            r.Error("Mod", "boom");
            r.Error("Mod", "  boom  ");

            Assert.Single(Only(r).Errors);
        }

        [Fact]
        public void DetailReplacesByKeyKeepsOrderAndEmptyRemoves()
        {
            StatusRegistry r = NewRegistry();
            r.Detail("Mod", "a", "one", StatusLevel.Ok);
            r.Detail("Mod", "b", "two", StatusLevel.Ok);
            r.Detail("Mod", "a", "uno", StatusLevel.Ok);

            Assert.Equal(new[] { "uno", "two" }, Only(r).Details.ConvertAll(d => d.Text));

            r.Detail("Mod", "a", "", StatusLevel.Ok);
            Assert.Equal(new[] { "two" }, Only(r).Details.ConvertAll(d => d.Text));
        }

        [Fact]
        public void RevisionMovesOnChangesButNotOnRepeats()
        {
            StatusRegistry r = NewRegistry();
            r.Installed("Mod", "Hubs");
            long afterInstall = r.Revision;

            r.Installed("Mod", "Hubs");
            Assert.Equal(afterInstall, r.Revision);

            r.Error("Mod", "boom");
            Assert.True(r.Revision > afterInstall);

            long afterError = r.Revision;
            r.Error("Mod", "boom");
            Assert.Equal(afterError, r.Revision);
        }

        [Fact]
        public void FacadeNeverThrowsAndMapsLevels()
        {
            ModStatus.Register(null, null, null, null);
            ModStatus.Detail("facade-test", "k", "t", 99);
            ModStatus.Detail("facade-test", "k2", "t", -4);

            ModStatusSnapshot m = ModStatus.Registry.Snapshot().Find(s => s.Id == "facade-test");
            Assert.NotNull(m);
            Assert.Equal(StatusLevel.Error, m.Details[0].Level);
            Assert.Equal(StatusLevel.Ok, m.Details[1].Level);
        }
    }

    public class StatusTextTests
    {
        static ModStatusSnapshot Mod(string name, StatusLevel level = StatusLevel.Ok)
        {
            return new ModStatusSnapshot { Id = name, Name = name, Level = level };
        }

        [Fact]
        public void NoModsSaysSo()
        {
            Assert.Equal("DW2 mods: none loaded", StatusText.Line(new List<ModStatusSnapshot>()));
        }

        [Fact]
        public void WithoutProblemsOnlyTheCountIsClaimed()
        {
            ModStatusSnapshot one = Mod("A");
            one.Features.Add("Hubs");
            one.Summary = "idle";

            Assert.Equal("DW2 mods: 1 loaded", StatusText.Line(new List<ModStatusSnapshot> { one }));
            Assert.Equal("DW2 mods: 3 loaded", StatusText.Line(new List<ModStatusSnapshot> { Mod("A"), Mod("B"), Mod("C") }));
        }

        [Fact]
        public void ErrorsNameTheModAndTheFirstMessage()
        {
            ModStatusSnapshot bad = Mod("Freighter", StatusLevel.Error);
            bad.Errors.AddRange(new[] { "FuelFirst failed to install", "second" });
            var mods = new List<ModStatusSnapshot> { Mod("Other"), bad };

            Assert.Equal("1 mod with a problem: Freighter: FuelFirst failed to install (+1 more)", StatusText.Line(mods));
        }

        [Fact]
        public void LoadFailureIsReportedAsFailedToLoad()
        {
            ModStatusSnapshot bad = Mod("Freighter", StatusLevel.Error);
            bad.LoadError = "DLL not found";

            Assert.Equal("Freighter ERROR: failed to load: DLL not found", StatusText.Line(new List<ModStatusSnapshot> { bad }));
        }

        [Fact]
        public void SeveralBadModsAreCounted()
        {
            ModStatusSnapshot a = Mod("A", StatusLevel.Error);
            a.Errors.Add("x");
            ModStatusSnapshot b = Mod("B", StatusLevel.Warn);
            b.Details.Add(new StatusDetail { Key = "k", Text = "t", Level = StatusLevel.Warn });

            Assert.Equal("2 mods with problems: A: x (+1 more mods)", StatusText.Line(new List<ModStatusSnapshot> { a, b }));
        }

        [Fact]
        public void PanelListsTheWorstFirstAndKeepsRegistrationOrderOtherwise()
        {
            ModStatusSnapshot a = Mod("A");
            ModStatusSnapshot b = Mod("B", StatusLevel.Error);
            b.Errors.Add("boom");
            ModStatusSnapshot c = Mod("C");

            List<PanelRow> rows = StatusText.Rows(new List<ModStatusSnapshot> { a, b, c });

            Assert.Equal(StatusLevel.Error, rows[0].Level);
            Assert.StartsWith("B  ERROR", rows[0].Text);
            Assert.StartsWith("A", rows[1].Text);
            Assert.StartsWith("C", rows[2].Text);
        }

        [Fact]
        public void BlockListsDetailsErrorsFeaturesAndLog()
        {
            ModStatusSnapshot m = Mod("M", StatusLevel.Error);
            m.Errors.Add("boom");
            m.Details.Add(new StatusDetail { Key = "ships", Text = "12", Level = StatusLevel.Ok });
            m.Details.Add(new StatusDetail { Key = "cap", Text = "full", Level = StatusLevel.Warn });
            m.Features.Add("Hubs");
            m.LogPath = "C:\\logs\\m.log";

            Assert.Equal("M  ERROR\n  ! boom\n  ships: 12\n  ! cap: full\n  features: Hubs\n  log: C:\\logs\\m.log", StatusText.Block(m));
        }
    }
}
