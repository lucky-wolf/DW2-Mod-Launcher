using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using DW2ModLauncher.Core.Models;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>
    /// Environment snapshots: a timestamped folder holding mods.json, the launcher settings, and a copy of
    /// every enabled mod. No UI; all methods throw on I/O failure.
    /// </summary>
    public static class SnapshotStore
    {
        /// <summary>Returns the new snapshot's folder.</summary>
        public static string Create(string snapshotsRoot, string modsJsonPath, string settingsPath, IEnumerable<ModInfo> enabledMods)
        {
            string root = Path.Combine(snapshotsRoot, DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(root);
            if (!string.IsNullOrWhiteSpace(modsJsonPath) && File.Exists(modsJsonPath)) File.Copy(modsJsonPath, Path.Combine(root, "mods.json"), true);
            if (File.Exists(settingsPath)) File.Copy(settingsPath, Path.Combine(root, "launcher_settings.json"), true);
            Dictionary<string, string> manifest = new Dictionary<string, string>();
            foreach (ModInfo mod in enabledMods)
            {
                FileNames.CopyDirectory(mod.Folder, Path.Combine(root, "MODs", FileNames.Safe(mod.ActiveToken)));
                manifest[mod.ActiveToken] = mod.Folder;
            }
            File.WriteAllText(Path.Combine(root, "snapshot_manifest.json"), JsonSerializer.Serialize(manifest), new UTF8Encoding(false));
            return root;
        }

        /// <summary>Null when there are no snapshots.</summary>
        public static string FindLatest(string snapshotsRoot)
        {
            return Directory.Exists(snapshotsRoot) ? Directory.GetDirectories(snapshotsRoot).OrderByDescending(x => x).FirstOrDefault() : null;
        }

        /// <summary>Copies the snapshot's mods, mod order and launcher settings back into place.</summary>
        public static void Restore(string snapshotRoot, string modsJsonPath, string settingsPath)
        {
            string manifestPath = Path.Combine(snapshotRoot, "snapshot_manifest.json");
            Dictionary<string, string> manifest = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(manifestPath, Encoding.UTF8));
            foreach (KeyValuePair<string, string> kv in manifest)
            {
                string source = Path.Combine(snapshotRoot, "MODs", FileNames.Safe(kv.Key));
                if (Directory.Exists(source) && Directory.Exists(Path.GetDirectoryName(kv.Value))) FileNames.CopyDirectory(source, kv.Value);
            }
            string savedOrder = Path.Combine(snapshotRoot, "mods.json");
            if (File.Exists(savedOrder) && !string.IsNullOrWhiteSpace(modsJsonPath)) File.Copy(savedOrder, modsJsonPath, true);
            string savedSettings = Path.Combine(snapshotRoot, "launcher_settings.json");
            if (File.Exists(savedSettings)) File.Copy(savedSettings, settingsPath, true);
        }
    }
}
