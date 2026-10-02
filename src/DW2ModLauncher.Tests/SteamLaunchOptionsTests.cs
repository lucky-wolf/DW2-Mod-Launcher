using DW2ModLauncher.Core.Services;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class SteamLaunchOptionsTests
    {
        [Fact]
        public void Parse_EnvVarBeforeCommand()
        {
            SteamLaunchOptions o = SteamLaunchOptions.Parse("DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=0 %command%");
            Assert.Equal("0", o.Environment["DOTNET_SYSTEM_GLOBALIZATION_INVARIANT"]);
            Assert.Equal("", o.Arguments);
            Assert.Equal("", o.Wrapper);
        }

        [Fact]
        public void Parse_EnvWrapperAndArguments()
        {
            SteamLaunchOptions o = SteamLaunchOptions.Parse("A=1 B=\"x y\" mangohud --dlsym %command% -windowed \"a b\"");
            Assert.Equal("1", o.Environment["A"]);
            Assert.Equal("x y", o.Environment["B"]);
            Assert.Equal("mangohud --dlsym", o.Wrapper);
            Assert.Equal("-windowed \"a b\"", o.Arguments);
        }

        [Fact]
        public void Parse_NoCommandToken_IsAllArguments()
        {
            SteamLaunchOptions o = SteamLaunchOptions.Parse("-windowed");
            Assert.Equal("-windowed", o.Arguments);
            Assert.Empty(o.Environment);
        }

        [Fact]
        public void ExtractLaunchOptions_FindsOnlyTheRequestedApp()
        {
            string vdf = "\"UserLocalConfigStore\"\n{\n\t\"Software\"\n\t{\n\t\t\"Valve\"\n\t\t{\n\t\t\t\"Steam\"\n\t\t\t{\n\t\t\t\t\"apps\"\n\t\t\t\t{\n" +
                "\t\t\t\t\t\"1\"\n\t\t\t\t\t{\n\t\t\t\t\t\t\"LaunchOptions\"\t\t\"other %command%\"\n\t\t\t\t\t}\n" +
                "\t\t\t\t\t\"1531540\"\n\t\t\t\t\t{\n\t\t\t\t\t\t\"LaunchOptions\"\t\t\"X=\\\"a b\\\" %command%\"\n\t\t\t\t\t}\n" +
                "\t\t\t\t}\n\t\t\t}\n\t\t}\n\t}\n}\n";
            Assert.Equal("X=\"a b\" %command%", SteamLaunchOptions.ExtractLaunchOptions(vdf, "1531540"));
            Assert.Null(SteamLaunchOptions.ExtractLaunchOptions(vdf, "999"));
        }

        [Fact]
        public void BuildStartInfo_AppliesEnvironment()
        {
            var env = new System.Collections.Generic.Dictionary<string, string> { ["K"] = "v" };
            var psi = GameLauncher.BuildStartInfo(@"C:\g", "--x", true, env);
            Assert.False(psi.UseShellExecute);
            Assert.Equal("v", psi.Environment["K"]);
        }

        [Fact]
        public void BuildStartInfo_OnLinux_LeavesSteamsEnvironmentAlone()
        {
            var env = new System.Collections.Generic.Dictionary<string, string> { ["K_ONLY_IN_TEST"] = "v" };
            var psi = GameLauncher.BuildStartInfo("/g", "--x", false, env);
            Assert.False(psi.Environment.ContainsKey("K_ONLY_IN_TEST"));
        }

        [Fact]
        public void EnvironmentText_RoundTripsAndSkipsJunk()
        {
            var env = GameLauncher.ParseEnvironment("# note\nA=1\n\nbad line\n B = x y \n=nope\n");
            Assert.Equal(2, env.Count);
            Assert.Equal("1", env["A"]);
            Assert.Equal("x y", env["B"]);
            Assert.Equal(env, GameLauncher.ParseEnvironment(GameLauncher.FormatEnvironment(env)));
        }
    }
}
