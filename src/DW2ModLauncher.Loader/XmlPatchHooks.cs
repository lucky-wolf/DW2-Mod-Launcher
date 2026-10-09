using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DW2ModLauncher.XmlPatching;
using HarmonyLib;

namespace DW2ModLauncher.Loader
{
    /// <summary>
    /// Applies the mods' XML patches (docs/plans/xml-patching.md) to the game's data as it is loaded. Two small Harmony hooks:
    /// a postfix on VirtualFileSystem.OpenStream swaps the stream of a data file that has patches for a patched in-memory copy,
    /// and a prefix on the 2-argument DwModSupport.ListDataFiles marks the start and end of a load pass (the first and last data
    /// groups of Galaxy.LoadStaticBaseData). Nothing is installed when no enabled mod has patches. Everything here is fail-soft:
    /// a problem is logged to patches.log and the game loads unpatched data.
    /// </summary>
    internal static class XmlPatchHooks
    {
        private const string FirstGroup = "ColonyEventDefinitions";
        private const string LastGroup = "SystemNames";

        private static readonly Regex RootElement = new Regex(@"<(ArrayOf[A-Za-z0-9_.]+)", RegexOptions.Compiled);
        private static readonly ConditionalWeakTable<Stream, object> Patched = new ConditionalWeakTable<Stream, object>();
        private static readonly object LogLock = new object();

        private static PatchRunner _runner;
        private static string _logPath;
        private static int _logged;
        private static Dictionary<string, SchemaRoot> _schemas;
        private static List<string> _modFolders;

        /// <summary>Reads every patch file of the manifest, and if there are any installs the hooks. Returns the number of patch files loaded.</summary>
        public static int Install(LoaderManifest manifest, string baseDir)
        {
            _logPath = Path.Combine(baseDir, "patches.log");
            try { File.WriteAllText(_logPath, "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] XML patching: reading patch files." + Environment.NewLine); }
            catch { _logPath = null; }

            _modFolders = manifest?.ModFolders ?? new List<string>();
            PatchRunner runner = new PatchRunner(SchemaFor, KeyMap.Default);
            runner.LateRoots.Add("ArrayOfTourItem");
            foreach (LoaderManifestPatchSet set in manifest?.Patches ?? new List<LoaderManifestPatchSet>())
            {
                foreach (string file in set.Files ?? new List<string>())
                {
                    try
                    {
                        runner.AddFile(set.DisplayName, DisplayPath(set, file), File.ReadAllText(file), set.Order);
                    }
                    catch (Exception ex)
                    {
                        Log("cannot read " + file + ": " + ex.Message);
                    }
                }
            }
            FlushReport(runner);
            if (runner.Files.Count == 0)
            {
                Log("no usable patch files; nothing installed.");
                return 0;
            }

            _runner = runner;
            Harmony harmony = new Harmony("dw2modlauncher.loader.xmlpatch");
            PatchOpenStream(harmony);
            PatchProviderOpenStream(harmony);
            PatchListDataFiles(harmony);
            AppDomain.CurrentDomain.ProcessExit += (_, __) => EndPass();
            Log(runner.Files.Count + " patch file(s) loaded; hooks installed.");
            return runner.Files.Count;
        }

        // ---- hooks ----

        private static void PatchOpenStream(Harmony harmony)
        {
            Type vfs = Type.GetType("Stride.Core.IO.VirtualFileSystem, Stride.Core.IO", false);
            if (vfs == null)
            {
                Log("ERROR: Stride VirtualFileSystem not found; patches will not be applied.");
                HookStatus.Failed("patches", "XML patches are not applied: the game's file system was not found", _logPath);
                return;
            }
            MethodInfo post = typeof(XmlPatchHooks).GetMethod(nameof(OpenStreamPostfix), BindingFlags.Static | BindingFlags.NonPublic);
            foreach (MethodInfo m in vfs.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly).Where(x => x.Name == "OpenStream"))
            {
                ParameterInfo[] ps = m.GetParameters();
                if (ps.Length == 0 || ps[0].Name != "path" || !typeof(Stream).IsAssignableFrom(m.ReturnType)) continue;
                try
                {
                    harmony.Patch(m, postfix: new HarmonyMethod(post));
                }
                catch (Exception ex)
                {
                    Log("ERROR: could not hook OpenStream(" + string.Join(", ", ps.Select(p => p.Name)) + "): " + ex.Message);
                    HookStatus.Failed("patches", "XML patches are not applied: OpenStream could not be hooked", _logPath);
                }
            }
        }

        // The tour (tutorial) items are the one data file the game opens straight through the data folder's provider with a relative url
        // ("TourItems.xml"), not through VirtualFileSystem.OpenStream, so they need their own hook.
        private static void PatchProviderOpenStream(Harmony harmony)
        {
            Type provider = Type.GetType("Stride.Core.IO.FileSystemProvider, Stride.Core.IO", false);
            MethodInfo post = typeof(XmlPatchHooks).GetMethod(nameof(ProviderOpenStreamPostfix), BindingFlags.Static | BindingFlags.NonPublic);
            foreach (MethodInfo m in (provider?.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly) ?? new MethodInfo[0]).Where(x => x.Name == "OpenStream"))
            {
                ParameterInfo[] ps = m.GetParameters();
                if (ps.Length == 0 || ps[0].Name != "url" || !typeof(Stream).IsAssignableFrom(m.ReturnType)) continue;
                try { harmony.Patch(m, postfix: new HarmonyMethod(post)); }
                catch (Exception ex)
                {
                    Log("WARNING: could not hook the data provider's OpenStream (tour items will not be patched): " + ex.Message);
                    HookStatus.Warn("tours", "tutorial tours are not patched: the data provider could not be hooked", _logPath);
                }
            }
        }

        private static void ProviderOpenStreamPostfix(string url, ref Stream __result)
        {
            if (url == null || url.IndexOf('/') >= 0 || url.IndexOf((char)92) >= 0 || !url.StartsWith("TourItems", StringComparison.OrdinalIgnoreCase)) return;
            OpenStreamPostfix("/data/" + url, ref __result);
        }

        private static void PatchListDataFiles(Harmony harmony)
        {
            Type support = Type.GetType("DistantWorlds.Types.DwModSupport, DistantWorlds.Types", false);
            MethodInfo pre = typeof(XmlPatchHooks).GetMethod(nameof(ListDataFilesPrefix), BindingFlags.Static | BindingFlags.NonPublic);
            // Only the 2-argument wrapper every caller uses: the 3-argument overload has exception filters Harmony cannot re-emit.
            MethodInfo target = support?.GetMethods(BindingFlags.Public | BindingFlags.Static).FirstOrDefault(m => m.Name == "ListDataFiles" && m.GetParameters().Length == 2);
            if (target == null)
            {
                Log("WARNING: DwModSupport.ListDataFiles not found; load passes will not be delimited (patches still apply, but the summary is only written at exit).");
                return;
            }
            try
            {
                harmony.Patch(target, prefix: new HarmonyMethod(pre));
            }
            catch (Exception ex)
            {
                Log("WARNING: could not hook ListDataFiles: " + ex.Message);
            }
        }

        // Harmony binds the parameters below by name.

        private static void OpenStreamPostfix(string path, ref Stream __result)
        {
            Stream stream = __result;
            try
            {
                PatchRunner runner = _runner;
                if (runner == null || stream == null || path == null) return;
                if (!path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) return;
                if (!(path.StartsWith("/data/", StringComparison.Ordinal) || path.StartsWith("/mods/", StringComparison.Ordinal) || path.StartsWith("/steam/", StringComparison.Ordinal))) return;
                if (!stream.CanSeek || !stream.CanRead || Patched.TryGetValue(stream, out _)) return;

                string root = PeekRoot(stream);
                if (root == null || !runner.HasPatchesFor(root)) return;

                XDocument doc = XDocument.Load(stream);
                int changes = runner.Apply(doc, path, OrderOf(path));
                if (changes == 0)
                {
                    stream.Position = 0;
                    return;
                }

                MemoryStream patched = new MemoryStream();
                doc.Save(patched);
                patched.Position = 0;
                Patched.Add(patched, null);
                stream.Dispose();
                __result = patched;
                Log(path + ": " + changes + " change(s) applied.");
            }
            catch (Exception ex)
            {
                Log("ERROR while patching " + path + " (the data loads unpatched): " + ex);
                try { if (stream != null && stream.CanSeek) stream.Position = 0; } catch { }
            }
        }

        private static void ListDataFilesPrefix(string searchPattern)
        {
            try
            {
                if (searchPattern == null) return;
                if (searchPattern.StartsWith(FirstGroup, StringComparison.Ordinal)) _runner?.BeginPass();
                else if (searchPattern.StartsWith(LastGroup, StringComparison.Ordinal)) EndPass();
            }
            catch (Exception ex)
            {
                Log("ERROR in pass hook: " + ex.Message);
            }
        }

        private static void EndPass()
        {
            PatchRunner runner = _runner;
            if (runner == null) return;
            runner.Finish();
            FlushReport(runner);
            foreach (string line in runner.Report.SummaryLines()) Log(line);
            ReportProblems(runner);
        }

        // Patch problems never stop the game and many players play on with them (a translation that targets an entity another mod
        // removed, say), so they are shown as an amber warning, never red: red is kept for a feature that could not install at all.
        private static void ReportProblems(PatchRunner runner)
        {
            int errors = runner.Report.Entries.Count(e => e.Severity == Severity.Error);
            int warnings = runner.Report.Entries.Count(e => e.Severity == Severity.Warning);
            if (errors + warnings == 0) return;
            HookStatus.Warn("patch-problems", "XML patches: " + errors + " item(s) skipped, " + warnings + " warning(s) - see patches.log", _logPath);
        }

        // ---- helpers ----

        /// <summary>
        /// The load-order position of the mod a data file belongs to: /mods/Folder/x.xml and /steam/WorkshopId/x.xml are found by their
        /// folder name in the manifest's mod list, the game's own /data/ comes before every mod. Unknown files count as the game's.
        /// </summary>
        private static int OrderOf(string path)
        {
            string[] parts = path.Split('/');
            if (parts.Length > 3 && parts[0].Length == 0 && (parts[1] == "mods" || parts[1] == "steam") && _modFolders != null)
            {
                int i = _modFolders.FindIndex(f => string.Equals(f, parts[2], StringComparison.OrdinalIgnoreCase));
                if (i >= 0) return i;
            }
            return int.MinValue;
        }

        /// <summary>The first <c>&lt;ArrayOfX&gt;</c> in the head of the stream; the position is restored to 0.</summary>
        private static string PeekRoot(Stream s)
        {
            byte[] head = new byte[4096];
            s.Position = 0;
            int read = s.Read(head, 0, head.Length);
            s.Position = 0;
            Match m = RootElement.Match(Encoding.UTF8.GetString(head, 0, read));
            return m.Success ? m.Groups[1].Value : null;
        }

        private static string DisplayPath(LoaderManifestPatchSet set, string file)
        {
            string normalized = file.Replace('\\', '/');
            int i = normalized.LastIndexOf("/patches/", StringComparison.OrdinalIgnoreCase);
            return (set.DisplayName ?? "mod") + ": " + (i >= 0 ? normalized.Substring(i + 1) : Path.GetFileName(file));
        }

        private static SchemaRoot SchemaFor(string rootElement)
        {
            if (_schemas == null) _schemas = BuildSchemas();
            return _schemas.TryGetValue(rootElement, out SchemaRoot s) ? s : null;
        }

        /// <summary>One schema per data list type of the game (a type with a static XmlSerializationHelper "XmlSerializer" field, e.g. RaceList).</summary>
        private static Dictionary<string, SchemaRoot> BuildSchemas()
        {
            Dictionary<string, SchemaRoot> schemas = new Dictionary<string, SchemaRoot>(StringComparer.Ordinal);
            Assembly types = Assembly.Load(new AssemblyName("DistantWorlds.Types"));
            Type[] all;
            try
            {
                all = types.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                all = ex.Types.Where(t => t != null).ToArray();
            }

            foreach (Type t in all)
            {
                FieldInfo f = t.IsClass ? t.GetField("XmlSerializer", BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly) : null;
                if (f == null || !f.FieldType.IsGenericType || f.FieldType.GetGenericTypeDefinition().Name != "XmlSerializationHelper`1") continue;
                SchemaReflector reflector = new SchemaReflector();
                SchemaRoot root = reflector.BuildRoot(t);
                if (root == null) continue;
                schemas[root.RootElement] = root;
                Log("schema " + root.RootElement + " <- " + t.Name + " (" + root.EntityType.Members.Count + " fields" + (reflector.Notes.Count > 0 ? ", " + reflector.Notes.Count + " not patchable" : string.Empty) + ")");
                foreach (string note in reflector.Notes.Take(5)) Log("  not patchable: " + note);
            }
            return schemas;
        }

        private static void FlushReport(PatchRunner runner)
        {
            List<PatchDiagnostic> entries = runner.Report.Entries;
            for (; _logged < entries.Count; _logged++)
                Log(entries[_logged].ToString());
        }

        private static void Log(string message)
        {
            if (_logPath == null) return;
            try
            {
                lock (LogLock)
                    File.AppendAllText(_logPath, "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " + message + Environment.NewLine);
            }
            catch
            {
                // Logging must never crash the game.
            }
        }
    }
}
