using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using DW2ModLauncher.Core.Models;
using DW2ModLauncher.Core.Services;
using DW2ModLauncher.Core.Services.Publishing;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class DetailsAndSettingsValueTests : IDisposable
    {
        private readonly string dir = Path.Combine(Path.GetTempPath(), "dw2-test-" + Guid.NewGuid().ToString("N"));

        public DetailsAndSettingsValueTests() { Directory.CreateDirectory(dir); }
        public void Dispose() { Directory.Delete(dir, true); }

        private static string T(string key) { return "<" + key + ">" + (key == "ConflictsWith" ? "{Mods}" : ""); }

        // ---- ModDocuments

        [Fact]
        public void ResolveSafe_FindsDocumentInsideMod_AndRejectsTraversalAndMissing()
        {
            Directory.CreateDirectory(Path.Combine(dir, "mod", "docs"));
            File.WriteAllText(Path.Combine(dir, "mod", "docs", "readme.txt"), "x");
            File.WriteAllText(Path.Combine(dir, "secret.txt"), "x");
            ModInfo mod = new ModInfo { Folder = Path.Combine(dir, "mod"), ContentRoot = Path.Combine(dir, "mod") };

            Assert.Equal(Path.Combine(dir, "mod", "docs", "readme.txt"), ModDocuments.ResolveSafe(mod, Path.Combine("docs", "readme.txt")));
            Assert.Null(ModDocuments.ResolveSafe(mod, Path.Combine("..", "secret.txt")));
            Assert.Null(ModDocuments.ResolveSafe(mod, "docs/nope.txt"));
            Assert.Null(ModDocuments.ResolveSafe(mod, ""));
        }

        // ---- ModDetails

        [Fact]
        public void BuildProblems_EmptyForHealthyMods_ListsConflictsAndUpdates()
        {
            ModInfo mod = new ModInfo { IsWorkshop = true, UpdateState = "update", HighRiskConflictCount = 1, ConflictCount = 1, ConflictFiles = new List<string> { "races.xml" }, ConflictOwners = new Dictionary<string, List<ModInfo>> { ["races.xml"] = new List<ModInfo> { new ModInfo { DisplayName = "Other" } } } };

            Assert.Empty(ModDetails.BuildProblems(mod, 1, T));

            List<string> lines = ModDetails.BuildProblems(mod, 3, T);
            Assert.Contains(lines, l => l.StartsWith("<ConflictModWinner>"));
            Assert.Contains(lines, l => l == "   races.xml");
            Assert.Contains(lines, l => l == "<SteamWorkshopUpdateAvailable>");
        }

        [Fact]
        public void SourceText_ShowsThePathUnderTheManagedFolder_AndWorkshopIdComesFromEitherSource()
        {
            string root = Path.Combine(dir, "mods");
            ModInfo local = new ModInfo { SourceName = "Local", Folder = Path.Combine(root, "junk") };
            ModInfo nested = new ModInfo { SourceName = "Local", Folder = Path.Combine(root, "pack", "inner") };
            ModInfo workshop = new ModInfo { SourceName = "Steam Workshop", Folder = Path.Combine(dir, "workshop", "123"), IsWorkshop = true, Id = "123" };
            char sep = Path.DirectorySeparatorChar;

            Assert.Equal("junk", ModDetails.SourceText(local, root));
            Assert.Equal("pack" + sep + "inner", ModDetails.SourceText(nested, root));
            Assert.Equal("Steam Workshop", ModDetails.SourceText(workshop, root));
            Assert.Equal("Local", ModDetails.SourceText(new ModInfo { SourceName = "Local" }, root));

            Assert.Equal("123", ModDetails.WorkshopId(workshop));
            Assert.Null(ModDetails.WorkshopId(local));
            local.WorkshopId = " 456 ";
            Assert.Equal("456", ModDetails.WorkshopId(local));
        }

        [Fact]
        public void BuildLinks_NamesWhoIsOnTheOtherEnd_AmongTheEnabledMods()
        {
            ModInfo xl = new ModInfo { Id = "xl", DisplayName = "XL" };
            xl.Provides.Add(new LauncherProvision { channel = "a.channel", files = new Dictionary<string, string> { ["one.json"] = "p/one.json", ["two.json"] = "p/two.json" } });
            ModInfo cr = new ModInfo { Id = "cr", DisplayName = "CR" };
            cr.Receives.Add("A.Channel");
            cr.Receives.Add("lonely.channel");
            cr.Dependencies.Add(new LauncherDependency { mod = "XL", required = true, minVersion = "1.36.0", loadAfter = true });
            cr.Dependencies.Add(new LauncherDependency { mod = "ghost" });

            string receiver = ModDetails.BuildLinks(cr, new[] { xl, cr }, HookOverlapAnalyzerTests.T);
            string provider = ModDetails.BuildLinks(xl, new[] { xl, cr }, HookOverlapAnalyzerTests.T);
            string providerAlone = ModDetails.BuildLinks(xl, new[] { xl }, HookOverlapAnalyzerTests.T);

            Assert.Contains("Receives: A.Channel\nProviders: XL", receiver);
            Assert.Contains("Receives: lonely.channel\nProviders: none enabled", receiver);
            Assert.Contains("Depends on: XL", receiver);
            Assert.Contains("Depends on: ghost - not enabled", receiver);
            Assert.Contains("Provides: a.channel\nReceivers: CR", provider);
            Assert.DoesNotContain("one.json", provider);
            Assert.Contains("Receivers: none enabled", providerAlone);
            Assert.Equal("", ModDetails.BuildLinks(new ModInfo { Id = "plain" }, new ModInfo[0], HookOverlapAnalyzerTests.T));
        }

        [Fact]
        public void BuildLinks_ListsLiveChannelsLikeFileChannels()
        {
            ModInfo xl = new ModInfo { Id = "xl", DisplayName = "XL" };
            ModInfo cr = new ModInfo { Id = "cr", DisplayName = "CR" };
            Dictionary<ModInfo, HookManifest> declared = new Dictionary<ModInfo, HookManifest>
            {
                [xl] = new HookManifest { Channels = new List<DeclaredChannel> { new DeclaredChannel { Name = "live.profiles", Role = "provides", Version = 1 } } },
                [cr] = new HookManifest
                {
                    Channels = new List<DeclaredChannel>
                {
                    new DeclaredChannel { Name = "Live.Profiles", Role = "consumes", Version = 1 },
                    new DeclaredChannel { Name = "live.lonely", Role = "consumes", Version = 1 } }
                }
            };
            Func<ModInfo, HookManifest> lookup = m => declared.TryGetValue(m, out HookManifest h) ? h : null;

            string receiver = ModDetails.BuildLinks(cr, new[] { xl, cr }, HookOverlapAnalyzerTests.T, lookup);
            string provider = ModDetails.BuildLinks(xl, new[] { xl, cr }, HookOverlapAnalyzerTests.T, lookup);
            string providerAlone = ModDetails.BuildLinks(xl, new[] { xl }, HookOverlapAnalyzerTests.T, lookup);

            Assert.Contains("Receives: Live.Profiles\nProviders: XL", receiver);
            Assert.Contains("Receives: live.lonely\nProviders: none enabled", receiver);
            Assert.Contains("Provides: live.profiles\nReceivers: CR", provider);
            Assert.Contains("Receivers: none enabled", providerAlone);
        }

        [Fact]
        public void BuildLinks_ShowsLiveChannelsTheLastSessionSawForAModWithNoManifest()
        {
            ModInfo icons = new ModInfo { Id = "icons", DisplayName = "Icons", Version = "2.0" };
            ModInfo troops = new ModInfo { Id = "troops", DisplayName = "Troops", Version = "1.0" };
            ObservedHooksFile observed = new ObservedHooksFile
            {
                Mods = new List<ObservedModInfo> { new ObservedModInfo { Key = icons.Key, Version = "2.0" }, new ObservedModInfo { Key = troops.Key, Version = "1.0" } },
                Channels = new ObservedChannelsInfo
                {
                    Provided = new List<ObservedChannelProvisionInfo> { new ObservedChannelProvisionInfo { Channel = "troops.icons", Provider = "Icons", ProviderKey = icons.Key, Version = 1 } },
                    Consumed = new List<ObservedChannelUseInfo> { new ObservedChannelUseInfo { Channel = "troops.icons", Consumer = "Troops", ConsumerKey = troops.Key, Version = 1 } }
                }
            };

            string provider = ModDetails.BuildLinks(icons, new[] { icons, troops }, HookOverlapAnalyzerTests.T, null, observed);
            string receiver = ModDetails.BuildLinks(troops, new[] { icons, troops }, HookOverlapAnalyzerTests.T, null, observed);

            Assert.Contains("Provides: troops.icons\nReceivers: Troops", provider);
            Assert.Contains("Receives: troops.icons\nProviders: Icons", receiver);

            // a session that ran another version of the mod says nothing about the installed one
            icons.Version = "2.1";
            Assert.Equal("", ModDetails.BuildLinks(icons, new[] { icons, troops }, HookOverlapAnalyzerTests.T, null, observed));
        }

        [Fact]
        public void BuildText_ReflectsEnabledState_AndStripsBbCode()
        {
            ModInfo mod = new ModInfo { Description = "Hello [b]bold[/b] world", SourceName = "Src", Folder = "/f", ConflictFiles = new List<string>() };

            string on = ModDetails.BuildText(mod, true, T);
            string off = ModDetails.BuildText(mod, false, T);

            Assert.Contains("Hello bold world", on);
            Assert.Contains("Hello bold world", off);
            // Source and state are shown elsewhere (above the text, and in the list), not repeated in it.
            Assert.DoesNotContain("<Source>", on);
            Assert.DoesNotContain("<State>", on);
            Assert.DoesNotContain("Src", on);
            Assert.DoesNotContain("<NoFileConflicts>", on);
            Assert.DoesNotContain("<ModDisabledNote>", off);
        }

        [Fact]
        public void DescriptionTxt_OverridesShortDescription_ByConvention()
        {
            string modDir = Path.Combine(dir, "desc");
            Directory.CreateDirectory(modDir);
            string modJson = Path.Combine(modDir, "mod.json");
            File.WriteAllText(modJson, "{\"displayName\":\"M\",\"shortDescription\":\"short\"}");
            File.WriteAllText(Path.Combine(modDir, "description.bbcode"), "  the long text" + Environment.NewLine + "over lines  ");

            // description.bbcode in the mod root is used by convention (no mod.json key needed).
            string text = ModDetails.BuildText(ModScanner.ReadModInfo(modDir, modJson, false, T), true, T);
            Assert.Contains("the long text", text);
            Assert.DoesNotContain("short", text);

            // A mod that names its own description file keeps using it.
            File.Move(Path.Combine(modDir, "description.bbcode"), Path.Combine(modDir, "legacy.txt"));
            File.WriteAllText(modJson, "{\"displayName\":\"M\",\"shortDescription\":\"short\",\"descriptionFile\":\"legacy.txt\"}");
            Assert.Contains("the long text", ModDetails.BuildText(ModScanner.ReadModInfo(modDir, modJson, false, T), true, T));

            // Missing, blank, or outside-the-folder files fall back to the short description.
            File.WriteAllText(modJson, "{\"shortDescription\":\"short\",\"descriptionFile\":\"nope.txt\"}");
            Assert.Contains("short", ModDetails.BuildText(ModScanner.ReadModInfo(modDir, modJson, false, T), true, T));
            File.WriteAllText(modJson, "{\"shortDescription\":\"short\",\"descriptionFile\":\"../outside.txt\"}");
            File.WriteAllText(Path.Combine(dir, "outside.txt"), "secret");
            Assert.DoesNotContain("secret", ModDetails.BuildText(ModScanner.ReadModInfo(modDir, modJson, false, T), true, T));
        }

        // ---- mod settings values

        private static ModSettingsField Field(string type, params string[] options)
        {
            return new ModSettingsField { Key = "k", Type = type, Options = options.Length == 0 ? null : new List<string>(options), Min = 1, Max = 50 };
        }

        [Theory]
        [InlineData("bool", ModSettingKind.Bool)]
        [InlineData("INT", ModSettingKind.Integer)]
        [InlineData("float", ModSettingKind.Number)]
        [InlineData("string", ModSettingKind.Text)]
        [InlineData("weird", ModSettingKind.Text)]
        public void KindOf_MapsSchemaTypes(string type, ModSettingKind expected)
        {
            Assert.Equal(expected, ModSettingsValues.KindOf(Field(type)));
        }

        [Fact]
        public void KindOf_EnumNeedsOptions()
        {
            Assert.Equal(ModSettingKind.Choice, ModSettingsValues.KindOf(Field("enum", "a", "b")));
            Assert.Equal(ModSettingKind.Text, ModSettingsValues.KindOf(Field("enum")));
        }

        [Fact]
        public void ToChoice_MatchesCaseInsensitively_ElseFirstOption()
        {
            ModSettingsField f = Field("enum", "Easy", "Hard");
            Assert.Equal("Hard", ModSettingsValues.ToChoice(f, JsonValue.Create("hard")));
            Assert.Equal("Easy", ModSettingsValues.ToChoice(f, JsonValue.Create("nope")));
            Assert.Equal("Easy", ModSettingsValues.ToChoice(f, null));
        }

        [Fact]
        public void ToNumber_ClampsToSchemaRange_AndDefaultsToZeroClamped()
        {
            ModSettingsField f = Field("int");
            Assert.Equal(50m, ModSettingsValues.ToNumber(f, JsonValue.Create(999)));
            Assert.Equal(1m, ModSettingsValues.ToNumber(f, null));
            Assert.Equal(7m, ModSettingsValues.ToNumber(f, JsonValue.Create(7)));
        }

        [Fact]
        public void FromNumber_WritesIntegersAsIntegersAndFloatsAsDoubles()
        {
            Assert.Equal("13", ModSettingsValues.FromNumber(Field("int"), 13m).ToJsonString());
            Assert.Equal("1.5", ModSettingsValues.FromNumber(Field("float"), 1.5m).ToJsonString());
        }

        [Fact]
        public void ToBool_IsFalseForMissingOrWrongType()
        {
            Assert.True(ModSettingsValues.ToBool(JsonValue.Create(true)));
            Assert.False(ModSettingsValues.ToBool(null));
            Assert.False(ModSettingsValues.ToBool(JsonValue.Create("yes")));
        }

        // ---- misc

        [Fact]
        public void UserDataRoot_IsAnAbsolutePath()
        {
            Assert.True(Path.IsPathRooted(UserDataRoot.Get()));
        }

        [Fact]
        public void SupportedVisibilities_StartWithPrivate_AndAlwaysOfferPublic()
        {
            IList<ModVisibility> v = ModPublisherFactory.SupportedVisibilities();
            Assert.Equal(ModVisibility.Private, v[0]);
            Assert.Contains(ModVisibility.Public, v);
        }
    }
}
