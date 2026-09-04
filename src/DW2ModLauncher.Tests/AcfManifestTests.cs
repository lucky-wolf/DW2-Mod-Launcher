using DW2ModLauncher.Core.Services;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class AcfManifestTests
    {
        private const string SampleAcf = @"
""AppWorkshop""
{
	""appid""		""1531540""
	""WorkshopItemsInstalled""
	{
		""123""
		{
			""size""		""456""
			""timeupdated""		""1700000000""
		}
		""789""
		{
			""timeupdated""		""1710000000""
		}
	}
	""WorkshopItemDetails""
	{
		""123""
		{
			""timeupdated""		""1705000000""
		}
	}
}";

        [Fact]
        public void ParseSectionTimes_ReadsPerItemTimestamps()
        {
            var installed = AcfManifest.ParseSectionTimes(SampleAcf, "WorkshopItemsInstalled");

            Assert.Equal(2, installed.Count);
            Assert.Equal(1700000000L, installed["123"]);
            Assert.Equal(1710000000L, installed["789"]);
        }

        [Fact]
        public void ParseSectionTimes_ReadsDifferentSectionIndependently()
        {
            var details = AcfManifest.ParseSectionTimes(SampleAcf, "WorkshopItemDetails");

            Assert.Single(details);
            Assert.Equal(1705000000L, details["123"]);
        }

        [Fact]
        public void ParseSectionTimes_ReturnsEmpty_ForMissingSection()
        {
            var missing = AcfManifest.ParseSectionTimes(SampleAcf, "NotASection");

            Assert.Empty(missing);
        }
    }
}
