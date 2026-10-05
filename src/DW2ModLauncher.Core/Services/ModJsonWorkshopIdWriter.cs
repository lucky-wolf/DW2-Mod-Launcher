using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>
    /// Writes the "workshopId" field back into a Mod's own mod.json after a first-time publish
    /// (see ModPublishCommandBuilder/docs/DLL Injection.md's publish workflow). DW2's own
    /// --ugc-publish tool never writes this back itself - the official guide has the Mod author
    /// copy the id out of the new Workshop item's URL and hand-edit it into mod.json - so this is
    /// the one piece of that workflow the launcher can genuinely automate. The field name is
    /// exactly "workshopId" (case-sensitive, per the guide), and everything else already in
    /// mod.json is preserved as-is.
    /// </summary>
    public static class ModJsonWorkshopIdWriter
    {
        public static void Write(string modJsonPath, long workshopId)
        {
            JsonObject root = JsonNode.Parse(File.ReadAllText(modJsonPath, Encoding.UTF8)) as JsonObject;
            if (root == null) throw new InvalidDataException("mod.json is not a JSON object.");
            root["workshopId"] = workshopId;
            ModJsonFile.Save(modJsonPath, root);
        }

        /// <summary>Removes "workshopId" (the Workshop item was deleted on Steam) so the Mod publishes as a new item.</summary>
        public static void Clear(string modJsonPath)
        {
            JsonObject root = JsonNode.Parse(File.ReadAllText(modJsonPath, Encoding.UTF8)) as JsonObject;
            if (root == null || !root.Remove("workshopId")) return;
            ModJsonFile.Save(modJsonPath, root);
        }
    }
}
