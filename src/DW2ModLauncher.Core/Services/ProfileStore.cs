using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using DW2ModLauncher.Core.Models;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>One way a saved profile differs from what is installed now.</summary>
    public class ProfileVersionChange
    {
        public string Token { get; set; }
        /// <summary>Null when the mod is not installed at all.</summary>
        public ModInfo Installed { get; set; }
        public string SavedVersion { get; set; }
    }

    /// <summary>Mod profiles, one JSON file each under a Profiles folder. No UI.</summary>
    public class ProfileStore
    {
        private readonly string root;

        public ProfileStore(string root)
        {
            this.root = root;
        }

        private string FileFor(string name) { return Path.Combine(root, FileNames.Safe(name) + ".json"); }

        public List<string> ListNames()
        {
            List<string> names = new List<string>();
            try
            {
                Directory.CreateDirectory(root);
                foreach (string path in Directory.GetFiles(root, "*.json", SearchOption.TopDirectoryOnly))
                    names.Add(Path.GetFileNameWithoutExtension(path));
            }
            catch { }
            return names;
        }

        public bool Exists(string name) { return File.Exists(FileFor(name)); }

        public static ModProfile Capture(string name, IEnumerable<string> order, string launchArguments, IEnumerable<ModInfo> mods)
        {
            ModProfile profile = new ModProfile();
            profile.Name = name;
            profile.Order = new List<string>(order ?? new List<string>());
            profile.ManualLaunchArguments = launchArguments;
            foreach (ModInfo mod in mods ?? new List<ModInfo>())
                profile.Versions[mod.ActiveToken ?? mod.Key] = mod.Version ?? "";
            return profile;
        }

        /// <summary>Throws on I/O failure.</summary>
        public void Save(ModProfile profile)
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(FileFor(profile.Name), JsonSerializer.Serialize(profile), new UTF8Encoding(false));
        }

        /// <summary>Null if there is no such profile; throws if the file is unreadable.</summary>
        public ModProfile Load(string name)
        {
            string path = FileFor(name);
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<ModProfile>(File.ReadAllText(path, Encoding.UTF8));
        }

        public void Delete(string name)
        {
            string path = FileFor(name);
            if (File.Exists(path)) File.Delete(path);
        }

        public static List<ProfileVersionChange> CompareVersions(ModProfile profile, IEnumerable<ModInfo> installedMods)
        {
            List<ModInfo> all = (installedMods ?? new List<ModInfo>()).ToList();
            List<ProfileVersionChange> changes = new List<ProfileVersionChange>();
            foreach (KeyValuePair<string, string> saved in profile.Versions ?? new Dictionary<string, string>())
            {
                ModInfo installed = all.FirstOrDefault(m => string.Equals(m.ActiveToken, saved.Key, StringComparison.OrdinalIgnoreCase) || string.Equals(m.Key, saved.Key, StringComparison.OrdinalIgnoreCase));
                if (installed == null || !string.Equals(installed.Version ?? "", saved.Value ?? "", StringComparison.OrdinalIgnoreCase))
                    changes.Add(new ProfileVersionChange { Token = saved.Key, Installed = installed, SavedVersion = saved.Value });
            }
            return changes;
        }
    }
}
