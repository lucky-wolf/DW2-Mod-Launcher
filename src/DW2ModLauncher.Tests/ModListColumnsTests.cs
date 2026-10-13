using System.Linq;
using DW2ModLauncher.Core.Services;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class ModListColumnsTests
    {
        [Fact]
        public void NothingSavedGivesTheDefaultOrder()
        {
            Assert.Equal(ModListColumns.Default, ModListColumns.Normalize(null));
            Assert.Equal(ModListColumns.Default, ModListColumns.Normalize(new string[0]));
        }

        [Fact]
        public void ASavedOrderIsKeptAndMissingColumnsAreAppendedInDefaultOrder()
        {
            var order = ModListColumns.Normalize(new[] { "author", "name" });

            Assert.Equal(new[] { "author", "name", "state", "source" }, order);
        }

        [Fact]
        public void UnknownAndRepeatedEntriesAreDroppedAndCaseIsIgnored()
        {
            var order = ModListColumns.Normalize(new[] { "Source", "bogus", "source", "STATE" });

            Assert.Equal(new[] { "source", "state", "name", "author" }, order);
        }

        [Fact]
        public void MovingPlacesTheColumnAfterTheGivenNumberOfOthers()
        {
            Assert.Equal(new[] { "state", "name", "author", "source" }, ModListColumns.Move(ModListColumns.Default, "author", 2));
            Assert.Equal(new[] { "author", "state", "name", "source" }, ModListColumns.Move(ModListColumns.Default, "author", 0));
            Assert.Equal(new[] { "name", "source", "author", "state" }, ModListColumns.Move(ModListColumns.Default, "state", 99));
        }

        [Fact]
        public void ASavedOrderThatStillNamesTheRemovedHealthColumnLosesIt()
        {
            Assert.Equal(new[] { "author", "state", "name", "source" }, ModListColumns.Normalize(new[] { "author", "health", "state" }));
        }

        [Fact]
        public void MovingAnUnknownColumnChangesNothing()
        {
            Assert.Equal(ModListColumns.Default, ModListColumns.Move(ModListColumns.Default, "bogus", 1));
        }

        [Fact]
        public void EveryMoveKeepsEachColumnExactlyOnce()
        {
            foreach (string id in ModListColumns.Default)
            {
                for (int index = -1; index <= 6; index++)
                {
                    var moved = ModListColumns.Move(ModListColumns.Default, id, index);
                    Assert.Equal(ModListColumns.Default.OrderBy(x => x), moved.OrderBy(x => x));
                }
            }
        }
    }
}
