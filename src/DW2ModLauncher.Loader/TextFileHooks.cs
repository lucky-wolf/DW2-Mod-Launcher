using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace DW2ModLauncher.Loader
{
    /// <summary>
    /// Lets a mod replace the text files the game reads straight from its data folder instead of through the mod file lookup:
    /// Hints.txt, dialog/*.txt and Galactopedia/**/*.txt (GameText.txt and SystemNames.txt are found through the lookup already).
    /// The game opens them by real path (Stride's FileSystemProvider, or System.IO.File), so the hooks only swap the path of a
    /// request for such a file to the mod's copy before the call runs. Nothing else is redirected.
    /// </summary>
    public static class TextFileHooks
    {
        private static Dictionary<string, string> _files;
        private static string _logPath;
        private static bool _replaceVanilla;

        [ThreadStatic]
        private static bool _checking;
        private static readonly HashSet<string> Logged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Installs the hooks if any mod ships such a file. Returns the number of replaced files.</summary>
        public static int Install(LoaderManifest manifest, string baseDir)
        {
            _files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (LoaderManifestTextFile f in manifest?.TextFiles ?? new List<LoaderManifestTextFile>())
            {
                if (!string.IsNullOrWhiteSpace(f?.Relative) && !string.IsNullOrWhiteSpace(f.Path)) _files[f.Relative.Replace((char)92, '/')] = f.Path;
            }
            _replaceVanilla = manifest?.GalactopediaReplacesVanilla ?? false;
            if (_files.Count == 0 && !_replaceVanilla) return 0;
            _logPath = Path.Combine(baseDir, "textfiles.log");
            try { File.WriteAllText(_logPath, string.Empty); } catch { } // one game start per log, like patches.log

            Harmony harmony = new Harmony("dw2modlauncher.loader.textfiles");
            HarmonyMethod prefix = new HarmonyMethod(typeof(TextFileHooks).GetMethod(nameof(PathPrefix), BindingFlags.Static | BindingFlags.NonPublic));
            HarmonyMethod openPrefix = new HarmonyMethod(typeof(TextFileHooks).GetMethod(nameof(OpenStreamPrefix), BindingFlags.Static | BindingFlags.NonPublic));
            HarmonyMethod existsPrefix = new HarmonyMethod(typeof(TextFileHooks).GetMethod(nameof(FileExistsPrefix), BindingFlags.Static | BindingFlags.NonPublic));

            Type provider = Type.GetType("Stride.Core.IO.FileSystemProvider, Stride.Core.IO", false);
            int hooked = 0;
            if (provider != null)
            {
                foreach (MethodInfo m in provider.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                             .Where(x => (x.Name == "OpenStream" || x.Name == "FileExists") && x.GetParameters().Length > 0 && x.GetParameters()[0].Name == "url" && x.GetParameters()[0].ParameterType == typeof(string)))
                {
                    harmony.Patch(m, prefix: m.Name == "OpenStream" ? openPrefix : existsPrefix);
                    hooked++;
                }
                foreach (MethodInfo m in provider.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                             .Where(x => x.Name == "ListFiles" && x.ReturnType == typeof(string[]) && x.GetParameters().Length > 1 && x.GetParameters()[0].Name == "url" && x.GetParameters()[1].Name == "searchPattern"))
                {
                    harmony.Patch(m, postfix: new HarmonyMethod(typeof(TextFileHooks).GetMethod(nameof(ListFilesPostfix), BindingFlags.Static | BindingFlags.NonPublic)));
                    hooked++;
                }
            }
            else
            {
                Log("Stride FileSystemProvider not found.");
                HookStatus.Failed("textfiles", "text file hook failed: the game's data provider was not found (Hints, dialog, Galactopedia stay unreplaced)", _logPath);
            }
            foreach (string name in new[] { "ReadAllText", "ReadAllLines", "ReadLines", "ReadAllBytes", "OpenRead", "OpenText", "Exists" })
            {
                foreach (MethodInfo m in typeof(File).GetMethods(BindingFlags.Public | BindingFlags.Static)
                             .Where(x => x.Name == name && x.GetParameters().Length > 0 && x.GetParameters()[0].Name == "path" && x.GetParameters()[0].ParameterType == typeof(string)))
                {
                    harmony.Patch(m, prefix: prefix);
                    hooked++;
                }
            }
            foreach (KeyValuePair<string, string> f in _files) Log("replacing data/" + f.Key + " with " + f.Value);
            Log(hooked + " file API(s) hooked.");
            if (hooked == 0) HookStatus.Failed("textfiles", "text file hook failed: no file API could be hooked", _logPath);
            return _files.Count;
        }

        /// <summary>
        /// The path below the data folder (Hints.txt, dialog/x.txt, Galactopedia/GameConcepts/y.txt) of a text file the game may read,
        /// or null when the path cannot be one of the replaceable files. The game names it by real path (.../data/Hints.txt, under Proton
        /// Z:\...\data\Hints.txt), or, through Stride's provider, relative to the data folder (Hints.txt, dialog/x.txt).
        /// This runs on every hooked File call in the process, so the cheap checks come first and nothing is allocated for other paths.
        /// </summary>
        internal static string DataRelative(string path, bool relativeToData)
        {
            if (path == null || path.Length < 5 || !path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) return null;
            if (path.IndexOf("Hints", StringComparison.OrdinalIgnoreCase) < 0
                && path.IndexOf("dialog", StringComparison.OrdinalIgnoreCase) < 0
                && path.IndexOf("Galactopedia", StringComparison.OrdinalIgnoreCase) < 0) return null;
            string normalized = path.Replace((char)92, '/');
            int at = normalized.LastIndexOf("/data/", StringComparison.OrdinalIgnoreCase);
            if (at >= 0) return normalized.Substring(at + 6);
            if (normalized.StartsWith("data/", StringComparison.OrdinalIgnoreCase)) return normalized.Substring(5);
            if (relativeToData && normalized.IndexOf(':') < 0 && !normalized.StartsWith("/", StringComparison.Ordinal)) return normalized;
            return null;
        }

        /// <summary>The mod's copy of a data-folder text file the game asks for, or null.</summary>
        private static string Replacement(string path, bool relativeToData, MethodBase api)
        {
            string relative = DataRelative(path, relativeToData);
            if (relative == null) return null;
            if (!_files.TryGetValue(relative, out string mine) || string.Equals(path, mine, StringComparison.OrdinalIgnoreCase)) return null;
            if (_checking) return null; // the File.Exists below is hooked too
            _checking = true;
            try { if (!File.Exists(mine)) return null; }
            finally { _checking = false; }
            lock (Logged)
            {
                // the API is logged so the hooks can be narrowed to the calls the game really makes
                if (Logged.Add(relative.ToLowerInvariant() + "|" + api?.Name)) Log("served " + path + " from " + mine + " via " + api?.DeclaringType?.Name + "." + api?.Name);
            }
            return mine;
        }

        /// <summary>
        /// The game lists the Galactopedia articles (the file names are the article titles) with ListFiles on the data folder. The list becomes the
        /// game's files (unless a mod replaces them) plus the mods' files; a mod's file of the same name takes the game's place (the open hooks
        /// above serve it), and a name only a mod has is added.
        /// </summary>
        private static void ListFilesPostfix(string url, string searchPattern, ref string[] __result)
        {
            try
            {
                string folder = GalactopediaFolder(url);
                if (folder == null || !string.Equals(searchPattern, "*.txt", StringComparison.OrdinalIgnoreCase)) return;
                string prefix = "Galactopedia/" + folder + "/";
                List<string> list = new List<string>();
                HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (!_replaceVanilla)
                {
                    foreach (string listed in __result ?? new string[0])
                    {
                        if (seen.Add(Path.GetFileName(listed))) list.Add(listed);
                        string dir = listed.Replace((char)92, '/');
                        int cut = dir.LastIndexOf('/');
                        if (cut >= 0) prefix = dir.Substring(0, cut + 1);
                    }
                }
                int added = 0;
                foreach (string relative in _files.Keys.Where(k => k.StartsWith("Galactopedia/" + folder + "/", StringComparison.OrdinalIgnoreCase)))
                {
                    string name = relative.Substring(relative.LastIndexOf('/') + 1);
                    if (seen.Add(name)) { list.Add(prefix + name); added++; }
                }
                __result = list.ToArray();
                lock (Logged)
                {
                    if (Logged.Add("list " + folder)) Log("Galactopedia/" + folder + ": " + list.Count + " article(s), " + added + " from mods" + (_replaceVanilla ? " (game's own dropped)" : "") + ", e.g. " + (list.Count > 0 ? list[0] : "-"));
                }
            }
            catch (Exception ex) { Log("Galactopedia listing failed: " + ex.Message); }
        }

        // "Galactopedia/GameConcepts" (or with a leading slash / data/) -> "GameConcepts"; anything else -> null.
        private static string GalactopediaFolder(string path)
        {
            if (path == null) return null;
            string p = path.Replace((char)92, '/').Trim('/');
            if (p.StartsWith("data/", StringComparison.OrdinalIgnoreCase)) p = p.Substring(5);
            if (!p.StartsWith("Galactopedia/", StringComparison.OrdinalIgnoreCase)) return null;
            string folder = p.Substring(13);
            return folder.Length == 0 || folder.IndexOf('/') >= 0 ? null : folder;
        }


        // Harmony binds the parameters below by name.
        private static void PathPrefix(ref string path, MethodBase __originalMethod)
        {
            try { string mine = Replacement(path, false, __originalMethod); if (mine != null) path = mine; }
            catch (Exception ex) { Log("hook failed for " + path + ": " + ex.Message); }
        }

        // The provider's url is relative to the data folder (Hints.txt), so swapping it for the mod's absolute path would be
        // combined with the data folder and fail; these answer the call directly instead.
        private static bool OpenStreamPrefix(string url, ref Stream __result, MethodBase __originalMethod)
        {
            try
            {
                string mine = Replacement(url, true, __originalMethod);
                if (mine == null) return true;
                __result = new FileStream(mine, FileMode.Open, FileAccess.Read, FileShare.Read);
                return false;
            }
            catch (Exception ex)
            {
                Log("hook failed for " + url + ": " + ex.Message);
                return true;
            }
        }

        private static bool FileExistsPrefix(string url, ref bool __result, MethodBase __originalMethod)
        {
            try
            {
                if (Replacement(url, true, __originalMethod) == null) return true;
                __result = true;
                return false;
            }
            catch (Exception ex)
            {
                Log("hook failed for " + url + ": " + ex.Message);
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
