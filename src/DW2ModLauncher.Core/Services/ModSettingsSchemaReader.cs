using System.IO;
using System.Text;
using System.Text.Json;
using DW2ModLauncher.Core.Models;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>
    /// Reads a mod's optional settings.schema.json (shipped by the mod author, describing the
    /// shape of its own settings.json values) into a ModSettingsSchema.
    /// </summary>
    public static class ModSettingsSchemaReader
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        public static ModSettingsSchema Read(string modContentRoot)
        {
            if (string.IsNullOrWhiteSpace(modContentRoot)) return null;
            string path = Path.Combine(modContentRoot, "settings.schema.json");
            if (!File.Exists(path)) return null;
            try
            {
                ModSettingsSchema schema = JsonSerializer.Deserialize<ModSettingsSchema>(File.ReadAllText(path, Encoding.UTF8), Options);
                return schema != null && schema.Fields != null && schema.Fields.Count > 0 ? schema : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
