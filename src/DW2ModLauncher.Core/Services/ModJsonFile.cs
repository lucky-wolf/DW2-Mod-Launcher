using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>
    /// The one place the launcher writes a mod.json. mod.json is DW2's own file and is not read for injection (that is
    /// dw2modlauncher.json), so a stale "launcher" block left over from an older launcher is dropped on every write.
    /// </summary>
    public static class ModJsonFile
    {
        private static readonly JsonSerializerOptions Indented = new JsonSerializerOptions { WriteIndented = true };

        /// <summary>Removes the obsolete "launcher" key (any casing). True if one was removed.</summary>
        public static bool StripObsoleteKeys(JsonObject root)
        {
            string key = root.Select(kv => kv.Key).FirstOrDefault(k => k.Equals("launcher", StringComparison.OrdinalIgnoreCase));
            return key != null && root.Remove(key);
        }

        public static void Save(string modJsonPath, JsonObject root)
        {
            StripObsoleteKeys(root);
            File.WriteAllText(modJsonPath, root.ToJsonString(Indented), new UTF8Encoding(false));
        }
    }
}
