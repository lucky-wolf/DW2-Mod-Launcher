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

        private static string T(string key) { return "<" + key + ">"; }

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
            ModInfo mod = new ModInfo { IsWorkshop = true, UpdateState = "update", HighRiskConflictCount = 1, ConflictCount = 1, ConflictMods = new List<string> { "Other" } };

            Assert.Empty(ModDetails.BuildProblems(mod, 1, T));

            List<string> lines = ModDetails.BuildProblems(mod, 3, T);
            Assert.Contains(lines, l => l.Contains("<ConflictsWith>Other"));
            Assert.Contains(lines, l => l == "<SteamWorkshopUpdateAvailable>");
        }

        [Fact]
        public void BuildText_ReflectsEnabledState_AndStripsBbCode()
        {
            ModInfo mod = new ModInfo { Description = "Hello [b]bold[/b] world", SourceName = "Src", Folder = "/f", ConflictFiles = new List<string>() };

            string on = ModDetails.BuildText(mod, true, T);
            string off = ModDetails.BuildText(mod, false, T);

            Assert.Contains("Hello bold world", on);
            Assert.Contains("ON", on);
            Assert.DoesNotContain("<NoFileConflicts>", on);
            Assert.Contains("OFF", off);
            Assert.DoesNotContain("<ModDisabledNote>", off);
        }

        [Fact]
        public void DescriptionFile_OverridesShortDescription_OnlyWhenNamedInModJson()
        {
            string modDir = Path.Combine(dir, "desc");
            Directory.CreateDirectory(modDir);
            string modJson = Path.Combine(modDir, "mod.json");
            File.WriteAllText(modJson, "{\"displayName\":\"M\",\"shortDescription\":\"short\"}");
            File.WriteAllText(Path.Combine(modDir, "description.txt"), "  the long text" + Environment.NewLine + "over lines  ");

            // Not named in mod.json: a stray description.txt is not used.
            Assert.Contains("short", ModDetails.BuildText(ModScanner.ReadModInfo(modDir, modJson, false, T), true, T));

            File.WriteAllText(modJson, "{\"displayName\":\"M\",\"shortDescription\":\"short\",\"descriptionFile\":\"description.txt\"}");
            string text = ModDetails.BuildText(ModScanner.ReadModInfo(modDir, modJson, false, T), true, T);
            Assert.Contains("the long text", text);
            Assert.DoesNotContain("short", text);

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
