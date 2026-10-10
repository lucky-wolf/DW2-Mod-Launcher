using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using DW2ModLauncher.Core.Models;
using DW2ModLauncher.Core.Services;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class ModSettingsStoreTests
    {
        private static ModInfo MakeMod(string token)
        {
            return new ModInfo { Id = token, ActiveToken = token, DisplayName = token };
        }

        [Fact]
        public void GetOrCreateValues_SynthesizesDefaults_WhenNoFileExists()
        {
            ModInfo mod = MakeMod("test/store-defaults-" + Guid.NewGuid().ToString("N"));
            ModSettingsSchema schema = new ModSettingsSchema
            {
                Groups = new List<ModSettingsGroup>
                {
                    new ModSettingsGroup
                    {
                        Name = "fields",
                        Fields = new List<ModSettingsField>
                        {
                            new ModSettingsField { Key = "Enabled", Type = "bool", Default = true },
                            new ModSettingsField { Key = "Volume", Type = "float", Default = 0.5 },
                        }
                    }
                }
            };
            string path = ModSettingsStore.GetSettingsPath(mod);
            try
            {
                Assert.False(File.Exists(path));

                JsonObject values = ModSettingsStore.GetOrCreateValues(mod, schema);

                Assert.True(values["Enabled"].GetValue<bool>());
                Assert.Equal(0.5, values["Volume"].GetValue<double>());
                Assert.True(File.Exists(path));
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Fact]
        public void GetOrCreateValues_ReturnsStoredValues_WhenFileAlreadyExists()
        {
            ModInfo mod = MakeMod("test/store-existing-" + Guid.NewGuid().ToString("N"));
            string path = ModSettingsStore.GetSettingsPath(mod);
            try
            {
                JsonObject saved = new JsonObject { ["Enabled"] = false };
                ModSettingsStore.SaveValues(mod, saved);

                JsonObject values = ModSettingsStore.GetOrCreateValues(mod, null);

                Assert.False(values["Enabled"].GetValue<bool>());
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Fact]
        public void GetOrCreateValues_FillsAndPersistsMissingKeys_KeepingStoredOnes()
        {
            ModInfo mod = MakeMod("test/store-missing-" + Guid.NewGuid().ToString("N"));
            ModSettingsSchema schema = new ModSettingsSchema
            {
                Groups = new List<ModSettingsGroup>
                {
                    new ModSettingsGroup
                    {
                        Name = "fields",
                        Fields = new List<ModSettingsField>
                        {
                            new ModSettingsField { Key = "Enabled", Type = "bool", Default = true },
                            new ModSettingsField { Key = "Added", Type = "bool", Default = true },
                        }
                    }
                }
            };
            string path = ModSettingsStore.GetSettingsPath(mod);
            try
            {
                ModSettingsStore.SaveValues(mod, new JsonObject { ["Enabled"] = false });

                ModSettingsStore.GetOrCreateValues(mod, schema);

                // Read back from disk (no schema): the new key is really in the file, the stored one untouched.
                JsonObject onDisk = ModSettingsStore.GetOrCreateValues(mod, null);
                Assert.False(onDisk["Enabled"].GetValue<bool>());
                Assert.True(onDisk["Added"].GetValue<bool>());
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Fact]
        public void GetOrCreateValues_Throws_AndLeavesFileUntouched_WhenFileIsCorrupt()
        {
            ModInfo mod = MakeMod("test/store-corrupt-" + Guid.NewGuid().ToString("N"));
            string path = ModSettingsStore.GetSettingsPath(mod);
            ModSettingsSchema schema = new ModSettingsSchema
            {
                Groups = new List<ModSettingsGroup>
                {
                    new ModSettingsGroup { Name = "fields", Fields = new List<ModSettingsField> { new ModSettingsField { Key = "Enabled", Type = "bool", Default = true } } }
                }
            };
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, "{ \"Enabled\": fals");

                Assert.Throws<InvalidDataException>(() => ModSettingsStore.GetOrCreateValues(mod, schema));

                Assert.Equal("{ \"Enabled\": fals", File.ReadAllText(path));
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Fact]
        public void SaveValues_RoundTrips()
        {
            ModInfo mod = MakeMod("test/store-roundtrip-" + Guid.NewGuid().ToString("N"));
            string path = ModSettingsStore.GetSettingsPath(mod);
            try
            {
                ModSettingsStore.SaveValues(mod, new JsonObject { ["Name"] = "value" });

                JsonObject reloaded = ModSettingsStore.GetOrCreateValues(mod, null);

                Assert.Equal("value", reloaded["Name"].GetValue<string>());
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
    }
}
