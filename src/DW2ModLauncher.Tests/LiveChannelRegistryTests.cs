extern alias loader;
using System;
using System.Collections.Generic;
using loader::DW2ModLauncher.Loader;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class LiveChannelRegistryTests
    {
        [Fact]
        public void ProvidedHandlerIsReturnedAndCalledLive()
        {
            var r = new LiveChannelRegistry();
            int answer = 1;
            r.Provide("ModA", "troop-logistics.hubs", 1, new Func<int>(() => answer));

            var handler = (Func<int>)r.Get("Consumer", "troop-logistics.hubs", 1, typeof(Func<int>));
            Assert.Equal(1, handler());
            answer = 2;
            Assert.Equal(2, handler());
            Assert.Equal("ModA", r.ProviderOf("troop-logistics.hubs"));
        }

        [Fact]
        public void UnprovidedChannelIsNullNotAnError()
        {
            var r = new LiveChannelRegistry();
            Assert.Null(r.Get("Consumer", "nobody.provides.this", 1, typeof(Func<int>)));
            Assert.Null(r.ProviderOf("nobody.provides.this"));
        }

        [Fact]
        public void ChannelNamesIgnoreCaseAndOuterSpaces()
        {
            var r = new LiveChannelRegistry();
            r.Provide("ModA", " Some.Channel ", 1, new Func<int>(() => 7));
            Assert.NotNull(r.Get("Consumer", "some.channel", 1, typeof(Func<int>)));
        }

        [Fact]
        public void SameProviderMayReplaceItsHandler()
        {
            var r = new LiveChannelRegistry();
            r.Provide("ModA", "c", 1, new Func<int>(() => 1));
            r.Provide("ModA", "c", 1, new Func<int>(() => 2));
            Assert.Equal(2, ((Func<int>)r.Get("Consumer", "c", 1, typeof(Func<int>)))());
        }

        [Fact]
        public void LaterProviderWinsAndTheLogSaysWhoWasSuperseded()
        {
            var lines = new List<string>();
            var r = new LiveChannelRegistry(lines.Add);
            r.Provide("ModA", "c", 1, new Func<int>(() => 1));
            r.Provide("ModB", "c", 1, new Func<int>(() => 2));

            Assert.Equal(2, ((Func<int>)r.Get("Consumer", "c", 1, typeof(Func<int>)))());
            Assert.Equal("ModB", r.ProviderOf("c"));
            Assert.Equal(2, lines.Count);
            Assert.DoesNotContain("superseding", lines[0]);
            Assert.Contains("ModB", lines[1]);
            Assert.Contains("superseding ModA", lines[1]);
        }

        [Fact]
        public void FirstProviderGetsNoPreviousHandler()
        {
            var r = new LiveChannelRegistry();
            Assert.Null(r.Provide("ModA", "c", 1, new Func<int>(() => 10)));
        }

        [Fact]
        public void WrapperCallingThePreviousHandlerModifiesItsAnswer()
        {
            var r = new LiveChannelRegistry();
            r.Provide("ModA", "c", 1, new Func<int>(() => 10));
            Func<int> previous = null;
            previous = (Func<int>)r.Provide("ModB", "c", 1, new Func<int>(() => previous() * 2));
            Assert.Equal(20, ((Func<int>)r.Get("Consumer", "c", 1, typeof(Func<int>)))());
            Assert.Equal(10, previous());
        }

        [Fact]
        public void SameProviderAgainGetsNoPreviousHandler()
        {
            var r = new LiveChannelRegistry();
            r.Provide("ModA", "c", 1, new Func<int>(() => 1));
            Assert.Null(r.Provide("ModA", "c", 1, new Func<int>(() => 2)));
        }

        [Fact]
        public void SameProviderAgainIsNotASupersession()
        {
            var lines = new List<string>();
            var r = new LiveChannelRegistry(lines.Add);
            r.Provide("ModA", "c", 1, new Func<int>(() => 1));
            r.Provide("ModA", "c", 1, new Func<int>(() => 2));
            Assert.All(lines, l => Assert.DoesNotContain("superseding", l));
        }

        [Fact]
        public void VersionMismatchIsReportedOnceAndConsumerGetsNothing()
        {
            var problems = new List<string>();
            var r = new LiveChannelRegistry(null, m => problems.Add(m.Text));
            r.Provide("ModA", "c", 2, new Func<int>(() => 1));

            Assert.Null(r.Get("ModB", "c", 1, typeof(Func<int>)));
            Assert.Null(r.Get("ModB", "c", 1, typeof(Func<int>)));
            string problem = Assert.Single(problems);
            Assert.Contains("ModB", problem);
            Assert.Contains("v1", problem);
            Assert.Contains("ModA", problem);
            Assert.Contains("v2", problem);
            Assert.Single(r.Snapshot().Problems);
        }

        [Fact]
        public void DelegateTypeMismatchIsReportedEvenAtTheSameVersion()
        {
            var problems = new List<ChannelMismatch>();
            var r = new LiveChannelRegistry(null, problems.Add);
            r.Provide("ModA", "c", 1, new Func<int, int>(x => x));

            Assert.Null(r.Get("ModB", "c", 1, typeof(Func<int>)));
            Assert.Single(problems);
            Assert.Equal(ChannelMismatch.Type, problems[0].Kind);
            Assert.Contains("Func", problems[0].Text);
            Assert.Equal(("ModB", "c", "ModA"), (problems[0].Consumer, problems[0].Channel, problems[0].Provider));
        }

        [Fact]
        public void AskingBeforeAnyoneProvidesIsNotAProblem()
        {
            var problems = new List<ChannelMismatch>();
            var r = new LiveChannelRegistry(null, problems.Add);
            Assert.Null(r.Get("ModB", "c", 1, typeof(Func<int>)));
            Assert.Empty(problems);
        }

        [Fact]
        public void SnapshotListsProvisionsAndWhatConsumersAskedFor()
        {
            var r = new LiveChannelRegistry();
            r.Provide("ModA", "c", 3, new Func<int>(() => 1));
            r.Get("ModB", "c", 3, typeof(Func<int>));
            r.Get("ModB", "d", 1, typeof(Action));

            ObservedChannels snapshot = r.Snapshot();
            ObservedChannelProvision provided = Assert.Single(snapshot.Provided);
            Assert.Equal(("c", "ModA", 3), (provided.Channel, provided.Provider, provided.Version));
            Assert.Equal(2, snapshot.Consumed.Count);
            Assert.Empty(snapshot.Problems);
        }

        [Fact]
        public void MissingNamesAreRefused()
        {
            var r = new LiveChannelRegistry();
            Assert.Throws<ArgumentException>(() => r.Provide("", "c", 1, new Func<int>(() => 1)));
            Assert.Throws<ArgumentException>(() => r.Provide("ModA", " ", 1, new Func<int>(() => 1)));
            Assert.Throws<ArgumentNullException>(() => r.Provide("ModA", "c", 1, null));
        }
    }
}
