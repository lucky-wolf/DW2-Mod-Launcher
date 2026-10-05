using DW2ModLauncher.Core.Services.Publishing;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class ModVersionTests
    {
        [Theory]
        [InlineData("1.35.8", "1.35.9")]
        [InlineData("1.0.0", "1.0.1")]
        [InlineData("2.0", "2.0.1")]
        [InlineData("3", "3.0.1")]
        [InlineData("1.2.9", "1.2.10")]
        [InlineData("1.2.3.4", "1.2.4.0")]
        [InlineData("", "")]
        [InlineData("beta", "beta")]
        [InlineData("1.0-rc1", "1.0-rc1")]
        public void BumpPatch(string input, string expected)
        {
            Assert.Equal(expected, ModVersion.BumpPatch(input));
        }
    }
}
