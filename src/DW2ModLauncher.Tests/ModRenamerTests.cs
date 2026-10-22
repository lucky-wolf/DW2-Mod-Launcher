using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DW2ModLauncher.Core.Models;
using DW2ModLauncher.Core.Services;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class ModRenamerTests
    {
        private static string MakeRoot()
        {
            string root = Path.Combine(Path.GetTempPath(), "dw2-rename-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

        [Fact]
        public void Rename_MovesFolderCoverArtAndEveryListEntryInTheSameSlot()
        {
            string root = MakeRoot();
            try
            {
                string folder = LocalModManager.Create(root, "Old Name");
                string modJson = Path.Combine(folder, "mod.json");
                File.WriteAllText(Path.Combine(folder, "Old Name.png"), "png");
                File.WriteAllText(modJson, "{ \"displayName\": \"Old Name\", \"version\": \"1.0.0\", \"previewImage\": \"Old Name.png\" }");
                ModOrderStore.Write(Path.Combine(root, "mods.json"), new[] { "steam/1", "mods/Old Name", "mods/Other" });
                ModOrderStore.Write(Path.Combine(root, "mods." + GameProfileStore.Escape("Alt") + ".json"), new[] { "mods/Old Name", "steam/1" });

                LauncherSettings settings = new LauncherSettings { ManagedModsRoot = root };
                settings.SelectedMods["managed:Old Name"] = true;
                settings.LastSelectedMod = "mods/Old Name";
                settings.CollapsedSettingGroups["Old Name"] = new List<string> { "g" };
                settings.AcknowledgedHooks.Add("Target | managed:Old Name@1.0.0 | managed:Zed@1");
                ModOrderState order = ModOrderStore.Read(Path.Combine(root, "mods.json"));
                ModInfo mod = ModScanner.ReadModInfo(folder, modJson, false, k => k);

                ModRenameResult result = ModRenamer.Rename(mod, "New: Name", settings, order, new[] { mod });

                string newFolder = Path.Combine(root, "New Name");
                Assert.Equal(newFolder, result.NewFolder);
                Assert.False(Directory.Exists(folder));
                Assert.True(File.Exists(Path.Combine(newFolder, "New Name.png")));
                string json = File.ReadAllText(Path.Combine(newFolder, "mod.json"));
                Assert.Contains("\"displayName\": \"New: Name\"", json);
                Assert.Contains("\"previewImage\": \"New Name.png\"", json);
                Assert.Equal(new[] { "steam/1", "mods/New Name", "mods/Other" }, ModOrderStore.Read(Path.Combine(root, "mods.json")).Order);
                Assert.Equal(new[] { "mods/New Name", "steam/1" }, ModOrderStore.Read(Path.Combine(root, "mods." + GameProfileStore.Escape("Alt") + ".json")).Order);
                Assert.Equal(new[] { "steam/1", "mods/New Name", "mods/Other" }, order.Order);
                Assert.True(settings.SelectedMods["managed:New Name"]);
                Assert.Equal("mods/New Name", settings.LastSelectedMod);
                Assert.True(settings.CollapsedSettingGroups.ContainsKey("New Name"));
                Assert.Equal("Target | managed:New Name@1.0.0 | managed:Zed@1", settings.AcknowledgedHooks.Single());
                Assert.Empty(result.Problems);
            }
            finally { Directory.Delete(root, true); }
        }

        [Fact]
        public void Rename_Refuses_ExistingFolder_AndChangesNothing()
        {
            string root = MakeRoot();
            try
            {
                string folder = LocalModManager.Create(root, "One");
                LocalModManager.Create(root, "Two");
                LauncherSettings settings = new LauncherSettings { ManagedModsRoot = root };
                ModInfo mod = ModScanner.ReadModInfo(folder, Path.Combine(folder, "mod.json"), false, k => k);

                Assert.Throws<IOException>(() => ModRenamer.Rename(mod, "Two", settings, new ModOrderState(), new[] { mod }));
                Assert.True(Directory.Exists(folder));
                Assert.Contains("\"displayName\": \"One\"", File.ReadAllText(Path.Combine(folder, "mod.json")));
            }
            finally { Directory.Delete(root, true); }
        }
    }
}
