using System;
using System.IO;
using System.Text;
using System.Text.Json;
using DW2ModLauncher.Core.Diagnostics;
using DW2ModLauncher.Core.Models;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>Reads a mod's optional dw2modlauncher.json (injection, dependencies, ...).</summary>
    public static class LauncherMetaReader
    {
        /// <summary>
        /// The mod's dw2modlauncher.json, or null when it has none. A file that exists but cannot be read or parsed is logged
        /// and also yields null; callers that can show the player why use <see cref="TryRead"/>.
        /// </summary>
        public static LauncherMeta Read(ModInfo mod)
        {
            LauncherMeta meta = TryRead(mod, out string error);
            if (error != null) Logger.Log("Read dw2modlauncher.json", (mod?.Folder ?? "") + ": " + error);
            return meta;
        }

        /// <summary>
        /// Like <see cref="Read"/>, but says why a file that exists could not be used: <paramref name="error"/> is null when the file
        /// is absent or read fine, else the reason (the mod's launcher metadata is then unknown, so its dependencies, tags and uid are missing).
        /// </summary>
        public static LauncherMeta TryRead(ModInfo mod, out string error)
        {
            error = null;
            if (mod == null || string.IsNullOrWhiteSpace(mod.Folder)) return null;
            string path = Path.Combine(mod.Folder, "dw2modlauncher.json");
            if (!File.Exists(path)) return null;
            try
            {
                return JsonSerializer.Deserialize<LauncherMeta>(File.ReadAllText(path, Encoding.UTF8), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (Exception ex) when (ex is JsonException || ex is IOException || ex is UnauthorizedAccessException)
            {
                error = ex.Message;
                return null;
            }
        }
    }
}
