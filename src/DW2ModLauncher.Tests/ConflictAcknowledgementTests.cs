using System.Collections.Generic;
using DW2ModLauncher.Core.Models;
using DW2ModLauncher.Core.Services;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class ConflictAcknowledgementTests
    {
        private static Dictionary<string, List<ModInfo>> Collisions(string versionOfB)
        {
            return new Dictionary<string, List<ModInfo>>
            {
                ["data/races.xml"] = new List<ModInfo> { new ModInfo { Id = "a", Version = "1.0" }, new ModInfo { Id = "b", Version = versionOfB } }
            };
        }

        [Fact]
        public void Acknowledged_Collisions_Stay_Quiet_Until_A_Mod_Is_Updated()
        {
            List<string> acknowledged = new List<string>();
            Assert.Single(ConflictAcknowledgement.Pending(Collisions("1.0"), acknowledged));

            ConflictAcknowledgement.Acknowledge(Collisions("1.0"), acknowledged);

            Assert.Empty(ConflictAcknowledgement.Pending(Collisions("1.0"), acknowledged));
            Assert.Single(ConflictAcknowledgement.Pending(Collisions("1.1"), acknowledged));
        }
    }
}
