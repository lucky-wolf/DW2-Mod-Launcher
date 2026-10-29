using System;
using System.Collections.Generic;
using System.Linq;
using DW2ModLauncher.Core.Models;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>
    /// File collisions the player said are fine ("launch anyway, and don't ask again"). A collision is remembered by the file and by the mods
    /// that ship it with their versions, so an update of one of them asks again, like the code-hook acknowledgements.
    /// </summary>
    public static class ConflictAcknowledgement
    {
        public static string Key(string file, IEnumerable<ModInfo> mods)
        {
            return (file ?? "").ToLowerInvariant() + "|" + string.Join("|", mods.Select(m => m.StateKey + "@" + (m.Version ?? "")).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray());
        }

        /// <summary>The collisions the player has not acknowledged.</summary>
        public static Dictionary<string, List<ModInfo>> Pending(Dictionary<string, List<ModInfo>> collisions, ICollection<string> acknowledged)
        {
            Dictionary<string, List<ModInfo>> pending = new Dictionary<string, List<ModInfo>>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, List<ModInfo>> kv in collisions)
            {
                if (acknowledged == null || !acknowledged.Contains(Key(kv.Key, kv.Value))) pending[kv.Key] = kv.Value;
            }
            return pending;
        }

        /// <summary>Adds the keys of these collisions to <paramref name="acknowledged"/>.</summary>
        public static void Acknowledge(Dictionary<string, List<ModInfo>> collisions, List<string> acknowledged)
        {
            foreach (KeyValuePair<string, List<ModInfo>> kv in collisions)
            {
                string key = Key(kv.Key, kv.Value);
                if (!acknowledged.Contains(key)) acknowledged.Add(key);
            }
        }
    }
}
