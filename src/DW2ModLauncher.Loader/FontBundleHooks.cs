using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace DW2ModLauncher.Loader
{
    /// <summary>
    /// Lets a mod ship the font bundle for the game's <c>--font Name</c> switch. The game looks for <c>data/db/bundles/Name.bundle</c> only
    /// (and then loads it, plus the hashed content file the header names, from the same folder), while a mod's bundles sit in the mod
    /// folder. Two small hooks on Stride's VirtualFileSystem make those exact files appear in the bundle folder:
    /// <c>FileExists</c> says yes and <c>OpenStream</c> opens the mod's copy. Nothing else is redirected.
    /// </summary>
    public static class FontBundleHooks
    {
        private const string BundleFolder = "/data/db/bundles/";

        private static List<LoaderManifestFont> _fonts;
        private static string _logPath;

        /// <summary>Installs the hooks if any mod declared a font. Returns the number of fonts.</summary>
        public static int Install(LoaderManifest manifest, string baseDir)
        {
            _fonts = (manifest?.Fonts ?? new List<LoaderManifestFont>())
                .Where(f => !string.IsNullOrWhiteSpace(f?.Name) && !string.IsNullOrWhiteSpace(f.Folder)).ToList();
            if (_fonts.Count == 0) return 0;
            _logPath = Path.Combine(baseDir, "fonts.log");
            try { File.WriteAllText(_logPath, string.Empty); } catch { } // one game start per log, like patches.log

            Type vfs = Type.GetType("Stride.Core.IO.VirtualFileSystem, Stride.Core.IO", false);
            if (vfs == null)
            {
                Log("ERROR: Stride VirtualFileSystem not found; font bundles will not be found by the game.");
                HookStatus.Failed("font", "font hook failed: the game's file system was not found, so the mod's font will not load", _logPath);
                return 0;
            }
            Harmony harmony = new Harmony("dw2modlauncher.loader.fontbundle");
            MethodInfo exists = vfs.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .FirstOrDefault(m => m.Name == "FileExists" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string));
            if (exists != null) harmony.Patch(exists, postfix: new HarmonyMethod(typeof(FontBundleHooks).GetMethod(nameof(FileExistsPostfix), BindingFlags.Static | BindingFlags.NonPublic)));
            else
            {
                Log("ERROR: VirtualFileSystem.FileExists not found.");
                HookStatus.Failed("font", "font hook failed: VirtualFileSystem.FileExists not found (game update?)", _logPath);
            }

            MethodInfo pre = typeof(FontBundleHooks).GetMethod(nameof(OpenStreamPrefix), BindingFlags.Static | BindingFlags.NonPublic);
            int opened = 0;
            foreach (MethodInfo m in vfs.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly).Where(x => x.Name == "OpenStream"))
            {
                ParameterInfo[] ps = m.GetParameters();
                if (ps.Length == 0 || ps[0].Name != "path" || !typeof(Stream).IsAssignableFrom(m.ReturnType)) continue;
                harmony.Patch(m, prefix: new HarmonyMethod(pre));
                opened++;
            }
            if (opened == 0)
            {
                Log("ERROR: VirtualFileSystem.OpenStream not found.");
                HookStatus.Failed("font", "font hook failed: VirtualFileSystem.OpenStream not found (game update?)", _logPath);
            }
            foreach (LoaderManifestFont f in _fonts) Log("font bundle '" + f.Name + "' from " + f.Folder + " (" + f.DisplayName + ")");
            return _fonts.Count;
        }

        /// <summary>The mod's copy of a bundle file the game asks for in the bundle folder, or null.</summary>
        private static string RealFile(string path)
        {
            if (path == null || !path.StartsWith(BundleFolder, StringComparison.OrdinalIgnoreCase) || !path.EndsWith(".bundle", StringComparison.OrdinalIgnoreCase)) return null;
            string file = path.Substring(BundleFolder.Length);
            if (file.Length == 0 || file.IndexOf('/') >= 0 || file.IndexOf((char)92) >= 0) return null;
            // The last declared font wins, so look from the end. "Name.bundle" and "Name.<hash>.bundle" both belong to the font.
            for (int i = _fonts.Count - 1; i >= 0; i--)
            {
                LoaderManifestFont f = _fonts[i];
                if (!file.StartsWith(f.Name + ".", StringComparison.OrdinalIgnoreCase)) continue;
                string real = Path.Combine(f.Folder, file);
                if (File.Exists(real)) return real;
            }
            return null;
        }

        // Harmony binds the parameters below by name.
        private static void FileExistsPostfix(string path, ref bool __result)
        {
            if (__result) return;
            try { if (RealFile(path) != null) __result = true; }
            catch (Exception ex) { Log("FileExists hook failed: " + ex.Message); }
        }

        private static bool OpenStreamPrefix(string path, ref Stream __result)
        {
            try
            {
                string real = RealFile(path);
                if (real == null) return true;
                __result = new FileStream(real, FileMode.Open, FileAccess.Read, FileShare.Read);
                Log("served " + path + " from " + real);
                return false;
            }
            catch (Exception ex)
            {
                Log("OpenStream hook failed for " + path + ": " + ex.Message);
                return true;
            }
        }

        private static void Log(string message)
        {
            try
            {
                if (_logPath != null) File.AppendAllText(_logPath, "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " + message + Environment.NewLine);
            }
            catch
            {
                // Logging must never crash the game.
            }
        }
    }
}
