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
            r.Provide("ModA", "troop-logistics.hubs", new Func<int>(() => answer));

            var handler = (Func<int>)r.Get("troop-logistics.hubs");
            Assert.Equal(1, handler());
            answer = 2;
            Assert.Equal(2, handler());
            Assert.Equal("ModA", r.ProviderOf("troop-logistics.hubs"));
        }

        [Fact]
        public void UnprovidedChannelIsNullNotAnError()
        {
            var r = new LiveChannelRegistry();
            Assert.Null(r.Get("nobody.provides.this"));
            Assert.Null(r.ProviderOf("nobody.provides.this"));
        }

        [Fact]
        public void ChannelNamesIgnoreCaseAndOuterSpaces()
        {
            var r = new LiveChannelRegistry();
            r.Provide("ModA", " Some.Channel ", new Func<int>(() => 7));
            Assert.NotNull(r.Get("some.channel"));
        }

        [Fact]
        public void SameProviderMayReplaceItsHandler()
        {
            var r = new LiveChannelRegistry();
            r.Provide("ModA", "c", new Func<int>(() => 1));
            r.Provide("ModA", "c", new Func<int>(() => 2));
            Assert.Equal(2, ((Func<int>)r.Get("c"))());
        }

        [Fact]
        public void LaterProviderWinsAndTheLogSaysWhoWasSuperseded()
        {
            var lines = new List<string>();
            var r = new LiveChannelRegistry(lines.Add);
            r.Provide("ModA", "c", new Func<int>(() => 1));
            r.Provide("ModB", "c", new Func<int>(() => 2));

            Assert.Equal(2, ((Func<int>)r.Get("c"))());
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
            Assert.Null(r.Provide("ModA", "c", new Func<int>(() => 10)));
        }

        [Fact]
        public void WrapperCallingThePreviousHandlerModifiesItsAnswer()
        {
            var r = new LiveChannelRegistry();
            r.Provide("ModA", "c", new Func<int>(() => 10));
            Func<int> previous = null;
            previous = (Func<int>)r.Provide("ModB", "c", new Func<int>(() => previous() * 2));
            Assert.Equal(20, ((Func<int>)r.Get("c"))());
            Assert.Equal(10, previous());
        }

        [Fact]
        public void SameProviderAgainGetsNoPreviousHandler()
        {
            var r = new LiveChannelRegistry();
            r.Provide("ModA", "c", new Func<int>(() => 1));
            Assert.Null(r.Provide("ModA", "c", new Func<int>(() => 2)));
        }

        [Fact]
        public void SameProviderAgainIsNotASupersession()
        {
            var lines = new List<string>();
            var r = new LiveChannelRegistry(lines.Add);
            r.Provide("ModA", "c", new Func<int>(() => 1));
            r.Provide("ModA", "c", new Func<int>(() => 2));
            Assert.All(lines, l => Assert.DoesNotContain("superseding", l));
        }

        [Fact]
        public void MissingNamesAreRefused()
        {
            var r = new LiveChannelRegistry();
            Assert.Throws<ArgumentException>(() => r.Provide("", "c", new Func<int>(() => 1)));
            Assert.Throws<ArgumentException>(() => r.Provide("ModA", " ", new Func<int>(() => 1)));
            Assert.Throws<ArgumentNullException>(() => r.Provide("ModA", "c", null));
        }
    }
}
