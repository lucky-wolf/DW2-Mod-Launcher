using System;
using System.Collections.Generic;
using DW2ModLauncher.Core.Services;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class NamedTextTests
    {
        [Fact]
        public void NamesAreFilledWhereverTheTextPutsThem()
        {
            Assert.Equal("Delete the profile Raid?", NamedText.Fill("Delete the profile {Profile}?", ("Profile", "Raid")));
            Assert.Equal("Raid: 3", NamedText.Fill("{Name}: {Count}", ("Count", 3), ("Name", "Raid")));
            Assert.Equal("3 - Raid", NamedText.Fill("{Count} - {Name}", ("Name", "Raid"), ("Count", 3)));
        }

        [Fact]
        public void ANameMayBeUsedTwiceOrNotAtAll()
        {
            Assert.Equal("a a", NamedText.Fill("{X} {X}", ("X", "a")));
            Assert.Equal("fixed", NamedText.Fill("fixed", ("X", "a")));
        }

        [Fact]
        public void AFormatAfterAColonIsApplied()
        {
            Assert.Equal("1.5", NamedText.Fill("{Size:0.#}", ("Size", 1.5)));
            Assert.Equal("2.00", NamedText.Fill("{Size:0.00}", ("Size", 2)));
        }

        [Fact]
        public void DoubledBracesAreLiteral()
        {
            Assert.Equal("{Name} is Raid", NamedText.Fill("{{Name}} is {Name}", ("Name", "Raid")));
        }

        [Fact]
        public void AnUnknownPlaceholderStaysVisibleAndIsReported()
        {
            List<string> unknown = new List<string>();

            string text = NamedText.Fill("{Mod} lacks {Thing}", unknown.Add, ("Mod", "XL"));

            Assert.Equal("XL lacks {Thing}", text);
            Assert.Equal(new[] { "Thing" }, unknown);
        }

        [Fact]
        public void AStalePositionalPlaceholderIsLeftAsWrittenNotTreatedAsAName()
        {
            Assert.Equal("Does not exist: {0}", NamedText.Fill("Does not exist: {0}", ("Path", "x")));
        }

        [Fact]
        public void TextWithoutPlaceholdersAndStrayBracesPassesThrough()
        {
            Assert.Equal("a { b } c", NamedText.Fill("a { b } c", ("Path", "x")));
            Assert.Equal("open { brace", NamedText.Fill("open { brace"));
        }

        [Fact]
        public void TheSameNameGivenTwiceIsACodeBug()
        {
            Assert.Throws<ArgumentException>(() => NamedText.Fill("{X}", ("X", 1), ("X", 2)));
        }

        [Fact]
        public void NamesListsEachPlaceholderOnceInOrder()
        {
            Assert.Equal(new[] { "Mod", "Count" }, NamedText.Names("{Mod} has {Count:0} of {Mod}, {{Literal}} and {0}"));
        }
    }
}
