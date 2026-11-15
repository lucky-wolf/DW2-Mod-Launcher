using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using DW2ModLauncher.Core.Models;
using DW2ModLauncher.Core.Services;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class ModSettingsMigrationTests : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "dw2ml-migration-" + Guid.NewGuid().ToString("N"));
        private readonly List<ModInfo> made = new List<ModInfo>();

        public void Dispose()
        {
            foreach (ModInfo mod in made)
            {
                foreach (string path in new[] { ModSettingsStore.GetSettingsPath(mod), ModSettingsStore.GetDefaultsPath(mod), ModSettingsStore.GetRetiredPath(mod) })
                    if (File.Exists(path)) File.Delete(path);
            }
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        private ModInfo MakeMod(string name, string schemaJson)
        {
            string folder = Path.Combine(root, name);
            Directory.CreateDirectory(folder);
            ModInfo mod = new ModInfo { Id = "test/" + name + "-" + Guid.NewGuid().ToString("N"), DisplayName = name, Folder = folder, ContentRoot = folder, Uid = ModUid.New() };
            mod.ActiveToken = mod.Id;
            SetSchema(mod, schemaJson);
            made.Add(mod);
            return mod;
        }

        private static void SetSchema(ModInfo mod, string schemaJson)
        {
            File.WriteAllText(Path.Combine(mod.Folder, "settings.schema.json"), schemaJson);
        }

        private static string Field(string key, string type, string defaultJson, string extra = "")
        {
            return "{ \"key\": \"" + key + "\", \"type\": \"" + type + "\", \"default\": " + defaultJson + extra + " }";
        }

        private static string Schema(string fields, string moved = null)
        {
            return "{ \"fields\": [" + fields + "]" + (moved == null ? "" : ", \"_moved\": " + moved) + " }";
        }

        private static string MovedTo(string key, ModInfo receiver, string newKey)
        {
            return "{ \"" + key + "\": { \"mod\": \"" + receiver.Uid + "\", \"key\": \"" + newKey + "\" } }";
        }

        private static string MovedFrom(ModInfo giver, string key)
        {
            return ", \"movedFrom\": [{ \"mod\": \"" + giver.Uid + "\", \"key\": \"" + key + "\" }]";
        }

        private static JsonObject Stored(ModInfo mod)
        {
            // what the settings editor would show: read with the mod's own schema, so settings nothing was moved into are at their defaults
            return ModSettingsStore.GetOrCreateValues(mod, ModSettingsSchemaReader.Read(mod.Folder));
        }

        private static ModSettingsMigration.Result Run(params ModInfo[] mods)
        {
            return ModSettingsMigration.Run(mods, _ => true, key => key);
        }

        // a giver as it was before the move (it has the setting, and the player chose a value), and as it is after (the setting gone, listed in _moved)
        private (ModInfo giver, ModInfo receiver) ChosenMove(bool chosen)
        {
            ModInfo giver = MakeMod("giver", Schema(Field("Tweak", "bool", "false")));
            ModInfo receiver = MakeMod("receiver", Schema(Field("Other", "int", "1")));
            ModSettingsStore.GetOrCreateValues(giver, ModSettingsSchemaReader.Read(giver.Folder));
            if (chosen) ModSettingsStore.SaveValues(giver, new JsonObject { ["Tweak"] = true });
            SetSchema(giver, Schema(Field("Remaining", "int", "3"), MovedTo("Tweak", receiver, "Tweak")));
            SetSchema(receiver, Schema(Field("Tweak", "bool", "false", MovedFrom(giver, "Tweak"))));
            return (giver, receiver);
        }

        [Fact]
        public void ChosenValue_MovesToTheReceiver_WhenBothModsSayTheSetting_Moved()
        {
            (ModInfo giver, ModInfo receiver) = ChosenMove(true);

            ModSettingsMigration.Result result = Run(giver, receiver);

            Assert.Empty(result.Problems);
            Assert.True(Stored(receiver)["Tweak"].GetValue<bool>());
            Assert.False(Stored(giver).ContainsKey("Tweak"));
        }

        [Fact]
        public void ValueMoves_OnlyOnce()
        {
            (ModInfo giver, ModInfo receiver) = ChosenMove(true);
            Run(giver, receiver);
            ModSettingsStore.SaveValues(receiver, new JsonObject { ["Tweak"] = false });

            Run(giver, receiver);

            Assert.False(Stored(receiver)["Tweak"].GetValue<bool>());
            Assert.False(File.Exists(ModSettingsStore.GetRetiredPath(giver)));
        }

        [Fact]
        public void GiverThatNeverSaidItMovedIt_CannotBeRaided()
        {
            ModInfo giver = MakeMod("giver", Schema(Field("Tweak", "bool", "false")));
            ModInfo thief = MakeMod("thief", Schema(Field("Tweak", "bool", "false")));
            ModSettingsStore.GetOrCreateValues(giver, ModSettingsSchemaReader.Read(giver.Folder));
            ModSettingsStore.SaveValues(giver, new JsonObject { ["Tweak"] = true });
            SetSchema(giver, Schema(Field("Remaining", "int", "3")));
            SetSchema(thief, Schema(Field("Tweak", "bool", "false", MovedFrom(giver, "Tweak"))));

            Run(giver, thief);

            Assert.False(Stored(thief)["Tweak"].GetValue<bool>());
        }

        [Fact]
        public void GiverThatNamedAnotherReceiver_IsNotRaidedByThisOne()
        {
            (ModInfo giver, ModInfo receiver) = ChosenMove(true);
            ModInfo other = MakeMod("other", Schema(Field("Tweak", "bool", "false", MovedFrom(giver, "Tweak"))));

            Run(giver, other, receiver);

            Assert.False(Stored(other)["Tweak"].GetValue<bool>());
            Assert.True(Stored(receiver)["Tweak"].GetValue<bool>());
        }

        [Fact]
        public void ValueTheReceiverAlreadyChose_IsNeverOverwritten_AndIsSpent()
        {
            (ModInfo giver, ModInfo receiver) = ChosenMove(true);
            // the receiver's default is true, so false is the player's own choice
            SetSchema(receiver, Schema(Field("Tweak", "bool", "true", MovedFrom(giver, "Tweak"))));
            ModSettingsStore.GetOrCreateValues(receiver, ModSettingsSchemaReader.Read(receiver.Folder));
            ModSettingsStore.SaveValues(receiver, new JsonObject { ["Tweak"] = false });

            Run(giver, receiver);

            Assert.False(Stored(receiver)["Tweak"].GetValue<bool>());
            Assert.False(File.Exists(ModSettingsStore.GetRetiredPath(giver)));
        }

        [Fact]
        public void ValueThePlayerNeverChose_IsNotMoved_SoTheReceiversDefaultStands()
        {
            (ModInfo giver, ModInfo receiver) = ChosenMove(false);
            SetSchema(receiver, Schema(Field("Tweak", "bool", "true", MovedFrom(giver, "Tweak"))));

            Run(giver, receiver);

            Assert.True(Stored(receiver)["Tweak"].GetValue<bool>());
        }

        [Fact]
        public void GiverRead_BeforeTheMigrationRuns_DoesNotLoseTheValue()
        {
            (ModInfo giver, ModInfo receiver) = ChosenMove(true);
            ModSettingsStore.GetOrCreateValues(giver, ModSettingsSchemaReader.Read(giver.Folder));

            Run(giver, receiver);

            Assert.True(Stored(receiver)["Tweak"].GetValue<bool>());
        }

        [Fact]
        public void ReceiverInstalledLater_StillGetsTheValue()
        {
            (ModInfo giver, ModInfo receiver) = ChosenMove(true);
            ModSettingsMigration.Run(new[] { giver }, _ => true, key => key); // the receiver is not there yet

            Assert.True(File.Exists(ModSettingsStore.GetRetiredPath(giver)));
            Run(giver, receiver);

            Assert.True(Stored(receiver)["Tweak"].GetValue<bool>());
        }

        [Fact]
        public void ReceiverThatIsNotEnabled_IsLeftAloneUntilItIs()
        {
            (ModInfo giver, ModInfo receiver) = ChosenMove(true);

            ModSettingsMigration.Run(new[] { giver, receiver }, m => m == giver, key => key);

            Assert.False(File.Exists(ModSettingsStore.GetSettingsPath(receiver)));
            Run(giver, receiver);
            Assert.True(Stored(receiver)["Tweak"].GetValue<bool>());
        }

        [Fact]
        public void SecondSourceIsTried_WhenTheSettingPassedThroughAnotherMod()
        {
            ModInfo first = MakeMod("first", Schema(Field("Tweak", "bool", "false")));
            ModInfo last = MakeMod("last", Schema(Field("Other", "int", "1")));
            ModSettingsStore.GetOrCreateValues(first, ModSettingsSchemaReader.Read(first.Folder));
            ModSettingsStore.SaveValues(first, new JsonObject { ["Tweak"] = true });
            SetSchema(first, Schema(Field("Remaining", "int", "3"), MovedTo("Tweak", last, "Tweak")));
            ModInfo middle = MakeMod("middle", Schema(Field("Other", "int", "1"))); // never had it, or gone
            SetSchema(last, Schema(Field("Tweak", "bool", "false", ", \"movedFrom\": [{ \"mod\": \"" + middle.Uid + "\", \"key\": \"Tweak\" }, { \"mod\": \"" + first.Uid + "\", \"key\": \"Tweak\" }]")));

            Run(first, middle, last);

            Assert.True(Stored(last)["Tweak"].GetValue<bool>());
        }

        [Fact]
        public void ValueOfTheWrongKind_IsReported_AndTheDefaultKept()
        {
            ModInfo giver = MakeMod("giver", Schema(Field("Tweak", "string", "\"\"")));
            ModInfo receiver = MakeMod("receiver", Schema(Field("Other", "int", "1")));
            ModSettingsStore.GetOrCreateValues(giver, ModSettingsSchemaReader.Read(giver.Folder));
            ModSettingsStore.SaveValues(giver, new JsonObject { ["Tweak"] = "lots" });
            SetSchema(giver, Schema(Field("Remaining", "int", "3"), MovedTo("Tweak", receiver, "Count")));
            SetSchema(receiver, Schema(Field("Count", "int", "1", MovedFrom(giver, "Tweak"))));

            ModSettingsMigration.Result result = Run(giver, receiver);

            Assert.Single(result.Problems);
            Assert.Equal("SettingMoveWrongType", result.Problems[0]);
            Assert.Equal(1, Stored(receiver)["Count"].GetValue<int>());
        }

        [Fact]
        public void NumberOutsideTheRange_AndChoiceNotOffered_AreReported()
        {
            ModInfo giver = MakeMod("giver", Schema(Field("Level", "int", "5") + "," + Field("Mode", "string", "\"a\"")));
            ModInfo receiver = MakeMod("receiver", Schema(Field("Other", "int", "1")));
            ModSettingsStore.GetOrCreateValues(giver, ModSettingsSchemaReader.Read(giver.Folder));
            ModSettingsStore.SaveValues(giver, new JsonObject { ["Level"] = 50, ["Mode"] = "z" });
            SetSchema(giver, Schema(Field("Remaining", "int", "3"), "{ \"Level\": { \"mod\": \"" + receiver.Uid + "\", \"key\": \"Level\" }, \"Mode\": { \"mod\": \"" + receiver.Uid + "\", \"key\": \"Mode\" } }"));
            SetSchema(receiver, Schema(Field("Level", "int", "5", ", \"min\": 0, \"max\": 10" + MovedFrom(giver, "Level"))
                + "," + Field("Mode", "enum", "\"a\"", ", \"options\": [\"a\", \"b\"]" + MovedFrom(giver, "Mode"))));

            ModSettingsMigration.Result result = Run(giver, receiver);

            Assert.Equal(new[] { "SettingMoveOutOfRange", "SettingMoveNotAnOption" }, result.Problems);
            Assert.Equal(5, Stored(receiver)["Level"].GetValue<int>());
        }

        [Fact]
        public void Reader_RejectsMovedKeyTheSchemaStillDeclares()
        {
            ModInfo mod = MakeMod("m", Schema(Field("Tweak", "bool", "false"), "{ \"Tweak\": { \"mod\": \"" + ModUid.New() + "\", \"key\": \"Tweak\" } }"));

            Assert.Throws<InvalidDataException>(() => ModSettingsSchemaReader.Read(mod.Folder));
        }

        [Fact]
        public void Reader_RejectsReceiversAndSourcesNotNamedByUid()
        {
            ModInfo bad = MakeMod("bad", Schema(Field("A", "bool", "false"), "{ \"B\": { \"mod\": \"Some Mod\", \"key\": \"B\" } }"));
            ModInfo badFrom = MakeMod("badfrom", Schema(Field("A", "bool", "false", ", \"movedFrom\": [{ \"mod\": \"Some Mod\", \"key\": \"A\" }]")));

            Assert.Throws<InvalidDataException>(() => ModSettingsSchemaReader.Read(bad.Folder));
            Assert.Throws<InvalidDataException>(() => ModSettingsSchemaReader.Read(badFrom.Folder));
        }

        [Fact]
        public void Reader_KeepsASchemaThatGaveEverythingAway()
        {
            ModInfo mod = MakeMod("empty", "{ \"_moved\": { \"A\": { \"mod\": \"" + ModUid.New() + "\", \"key\": \"A\" } } }");

            ModSettingsSchema schema = ModSettingsSchemaReader.Read(mod.Folder);

            Assert.NotNull(schema);
            Assert.Single(schema.Moved);
        }
    }
}
