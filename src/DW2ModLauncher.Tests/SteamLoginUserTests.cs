using DW2ModLauncher.Core.Services;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class SteamLoginUserTests
    {
        private const string Vdf = "\"users\"\n{\n\t\"76561197986586484\"\n\t{\n\t\t\"AccountName\"\t\t\"login\"\n\t\t\"PersonaName\"\t\t\"Mordachai\"\n\t\t\"timestamp\"\t\t\"1792155945\"\n\t}\n"
            + "\t\"76561198000000001\"\n\t{\n\t\t\"PersonaName\"\t\t\"Newer \\\"Alt\\\"\"\n\t\t\"MostRecent\"\t\t\"1\"\n\t\t\"timestamp\"\t\t\"1700000000\"\n\t}\n}\n";

        [Fact]
        public void ParsesEveryAccount()
        {
            var accounts = SteamLoginUser.Parse(Vdf);
            Assert.Equal(2, accounts.Count);
            Assert.Equal("76561197986586484", accounts[0].SteamId);
            Assert.Equal("Mordachai", accounts[0].PersonaName);
            Assert.False(accounts[0].MostRecent);
            Assert.Equal(1792155945L, accounts[0].Timestamp);
            Assert.Equal("Newer \"Alt\"", accounts[1].PersonaName);
        }

        [Fact]
        public void MostRecentFlagBeatsTimestamp()
        {
            Assert.Equal("Newer \"Alt\"", SteamLoginUser.PickMostRecent(SteamLoginUser.Parse(Vdf)).PersonaName);
        }

        [Fact]
        public void WithoutTheFlagTheNewestTimestampWins()
        {
            string vdf = Vdf.Replace("\"MostRecent\"\t\t\"1\"", "\"MostRecent\"\t\t\"0\"");
            Assert.Equal("Mordachai", SteamLoginUser.PickMostRecent(SteamLoginUser.Parse(vdf)).PersonaName);
        }

        [Fact]
        public void NobodyLoggedInGivesNull()
        {
            Assert.Null(SteamLoginUser.PickMostRecent(SteamLoginUser.Parse("\"users\"\n{\n}\n")));
        }
    }
}
