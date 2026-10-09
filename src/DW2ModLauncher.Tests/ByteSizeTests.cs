using DW2ModLauncher.Core.Services;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class ByteSizeTests
    {
        [Theory]
        [InlineData(0, "0 B")]
        [InlineData(512, "512 B")]
        [InlineData(122880, "120 KiB")]
        [InlineData(13841203, "13.2 MiB")]
        [InlineData(3450000, "3.29 MiB")]
        [InlineData(1000 * 1024, "0.98 MiB")]
        [InlineData(1536, "1.50 KiB")]
        [InlineData(32391123, "30.9 MiB")]
        public void Format(long bytes, string expected)
        {
            Assert.Equal(expected, ByteSize.Format(bytes));
        }
    }
}
