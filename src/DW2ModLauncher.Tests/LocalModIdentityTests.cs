using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using DW2ModLauncher.Core.Models;
using DW2ModLauncher.Core.Services;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class LocalModIdentityTests
    {
        private static ModInfo Local(string folder, string workshopId)
        {
            return new ModInfo { Id = folder, DisplayName = folder, Folder = folder, ActiveToken = "mods/" + folder, WorkshopId = workshopId };
        }

        [Fact]
        public void RenamedFolder_KeepsSettingsAndLoadOrderEntry()
        {
            string id = "9" + Guid.NewGuid().ToString("N");
            string dir = Path.Combine(Path.GetTempPath(), "dw2-identity-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string modsJson = Path.Combine(dir, "mods.json");
            ModInfo before = Local("OldName-" + id, id);
            ModInfo after = Local("NewName-" + id, id);
            string stored = ModSettingsStore.GetSettingsPath(after);
            try
            {
                ModOrderStore.Write(modsJson, new[] { "mods/Other", before.ActiveToken, "mods/Last" });
                LauncherSettings settings = new LauncherSettings();

                // First sight with the old name: settings written under the folder name move to the id-based name.
                if (File.Exists(stored)) File.Delete(stored);
                string legacy = ModSettingsStore.GetSettingsPath(new ModInfo { Id = before.Id, ActiveToken = before.ActiveToken });
                Directory.CreateDirectory(Path.GetDirectoryName(legacy));
                File.WriteAllText(legacy, "{\"Chosen\":7}");
                Assert.Empty(LocalModIdentity.Adopt(new[] { before }, settings, ModOrderStore.Read(modsJson), modsJson, out bool changed));
                Assert.True(changed);
                Assert.False(File.Exists(legacy));
                Assert.True(File.Exists(stored));

                // Folder renamed: the load-order entry is rewritten in place and the settings are still found.
                ModOrderState order = ModOrderStore.Read(modsJson);
                Assert.Empty(LocalModIdentity.Adopt(new[] { after }, settings, order, modsJson, out changed));
                Assert.True(changed);
                Assert.Equal(new[] { "mods/Other", after.ActiveToken, "mods/Last" }, ModOrderStore.Read(modsJson).Order);
                Assert.Equal(7, ModSettingsStore.GetOrCreateValues(after, null)["Chosen"].GetValue<int>());
            }
            finally
            {
                if (File.Exists(stored)) File.Delete(stored);
                Directory.Delete(dir, true);
            }
        }
    }
}
