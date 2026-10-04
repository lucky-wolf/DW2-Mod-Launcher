using System;
using System.IO;
using System.Text;
using System.Text.Json;
using DW2ModLauncher.Core.Models;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>Reads a mod's optional dw2modlauncher.json (injection).</summary>
    public static class LauncherMetaReader
    {
        public static LauncherMeta Read(ModInfo mod)
        {
            try
            {
                if (mod == null || string.IsNullOrWhiteSpace(mod.Folder)) return null;
                string path = Path.Combine(mod.Folder, "dw2modlauncher.json");
                if (!File.Exists(path)) return null;
                return JsonSerializer.Deserialize<LauncherMeta>(File.ReadAllText(path, Encoding.UTF8));
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
