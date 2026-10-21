using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;

namespace DW2ModLauncher.Loader
{
    // What the mods actually patched, as Harmony reports it, written to the launcher's data folder (docs/plans/hook-manifest.md, "Observed layer").
    // The launcher reads it next session to warn about mods that patch the same method. It needs no cooperation from the mods: the owner of a
    // patch is the mod whose folder the patch code was loaded from, not the free-form Harmony ID, and the effect is worked out from the patch
    // method's shape. Mods patch late (settings, first use), so the file is written once a minute after the mods are loaded and again at exit.
    //
    // The Harmony types are only resolved when Snapshot runs, so Install itself is safe without 0Harmony.dll.
    public static class ObservedHooks
    {
        public const string Launcher = "launcher";
        public const string Unknown = "unknown";

        static readonly TimeSpan FirstSnapshotDelay = TimeSpan.FromSeconds(60);
        static Timer _timer;
        static LoaderManifest _manifest;
        static string _error;

        // Starts the snapshots. Does nothing when the manifest names no file to write (a manifest from an older launcher).
        public static void Install(LoaderManifest manifest)
        {
            if (string.IsNullOrWhiteSpace(manifest?.ObservedHooksPath)) return;
            _manifest = manifest;
            _timer = new Timer(_ => Write(), null, FirstSnapshotDelay, Timeout.InfiniteTimeSpan);
            AppDomain.CurrentDomain.ProcessExit += (s, e) => Write();
        }

        static readonly object WriteGate = new object();

        static void Write()
        {
            lock (WriteGate)
            {
                try
                {
                    Observation observation = Snapshot(_manifest);
                    string path = _manifest.ObservedHooksPath;
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    string temp = path + ".tmp";
                    File.WriteAllText(temp, JsonSerializer.Serialize(observation, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
                    if (File.Exists(path)) File.Delete(path);
                    File.Move(temp, path);
                    _error = null;
                }
                catch (Exception ex)
                {
                    // Reported once per distinct message: the writer runs again at exit and a persistent failure would otherwise repeat.
                    if (_error != ex.Message)
                    {
                        _error = ex.Message;
                        Entry.Log("ERROR observed hooks could not be written to " + _manifest.ObservedHooksPath + ": " + ex);
                        HookStatus.Warn("observedhooks", StatusStrings.Get("WidgetObservedHooksFailed", _manifest.ObservedHooksPath, ex.Message), null);
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static Observation Snapshot(LoaderManifest manifest)
        {
            Observation o = new Observation
            {
                CapturedAt = DateTime.UtcNow.ToString("o"),
                GameVersion = GameVersion(),
                Mods = (manifest.Entries ?? new List<LoaderManifestEntry>()).Where(e => !string.IsNullOrWhiteSpace(e.ModKey))
                    .GroupBy(e => e.ModKey, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
                    .Select(e => new ObservedMod { Key = e.ModKey, Name = e.DisplayName, Version = e.ModVersion }).ToList(),
                Channels = LiveChannels.Registry.Snapshot()
            };
            foreach (MethodBase method in HarmonyLib.Harmony.GetAllPatchedMethods().ToList())
            {
                HarmonyLib.Patches info = HarmonyLib.Harmony.GetPatchInfo(method);
                if (info == null) continue;
                ObservedHook hook = new ObservedHook { Target = Describe(method) };
                Add(hook, "prefix", info.Prefixes, manifest);
                Add(hook, "postfix", info.Postfixes, manifest);
                Add(hook, "transpiler", info.Transpilers, manifest);
                Add(hook, "finalizer", info.Finalizers, manifest);
                if (hook.Patches.Count > 0) o.Hooks.Add(hook);
            }
            o.Hooks = o.Hooks.OrderBy(h => h.Target, StringComparer.Ordinal).ToList();
            return o;
        }

        static void Add(ObservedHook hook, string kind, IEnumerable<HarmonyLib.Patch> patches, LoaderManifest manifest)
        {
            foreach (HarmonyLib.Patch patch in patches ?? Enumerable.Empty<HarmonyLib.Patch>())
            {
                MethodInfo code = patch.PatchMethod;
                hook.Patches.Add(new ObservedPatch
                {
                    Owner = OwnerOf(code?.DeclaringType?.Assembly, manifest),
                    Assembly = code?.DeclaringType?.Assembly.GetName().Name,
                    HarmonyId = patch.owner,
                    Kind = kind,
                    Effect = EffectOf(kind, code),
                    Priority = patch.priority,
                    Before = patch.before?.ToList() ?? new List<string>(),
                    After = patch.after?.ToList() ?? new List<string>(),
                    Method = code == null ? null : Describe(code)
                });
            }
        }

        // The mod whose folder the patch code was loaded from (the deepest one, in case of nested folders), the launcher for the loader's own
        // patches, otherwise "unknown" with the assembly name recorded beside it.
        internal static string OwnerOf(Assembly assembly, LoaderManifest manifest)
        {
            if (assembly == null) return Unknown;
            if (assembly == typeof(ObservedHooks).Assembly) return Launcher;
            string location;
            try { location = assembly.Location; } catch (NotSupportedException) { return Unknown; }
            return OwnerOfPath(location, manifest);
        }

        internal static string OwnerOfPath(string location, LoaderManifest manifest)
        {
            if (string.IsNullOrWhiteSpace(location)) return Unknown;
            string full = Path.GetFullPath(location);
            LoaderManifestEntry best = null;
            int bestLength = -1;
            foreach (LoaderManifestEntry entry in manifest.Entries ?? new List<LoaderManifestEntry>())
            {
                if (string.IsNullOrWhiteSpace(entry.ModFolder) || string.IsNullOrWhiteSpace(entry.ModKey)) continue;
                string folder = Path.GetFullPath(entry.ModFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (full.StartsWith(folder, StringComparison.OrdinalIgnoreCase) && folder.Length > bestLength) { best = entry; bestLength = folder.Length; }
            }
            return best?.ModKey ?? Unknown;
        }

        // How hard a patch bites, from the shape of its method: a prefix that returns bool can skip the original; a transpiler rewrites it; a
        // by-reference argument or a returned value changes what the original is given or gives back; anything else only looks.
        internal static string EffectOf(string kind, MethodInfo code)
        {
            if (code == null) return "observes";
            ParameterInfo[] parameters = code.GetParameters();
            switch (kind)
            {
                case "transpiler": return "rewrites";
                case "prefix":
                    if (code.ReturnType == typeof(bool)) return "skips-original";
                    return ChangesArguments(parameters) ? "changes-args" : parameters.Any(p => p.Name == "__result" && p.ParameterType.IsByRef) ? "changes-result" : "observes";
                case "postfix":
                    if (code.ReturnType != typeof(void) || parameters.Any(p => p.Name == "__result" && p.ParameterType.IsByRef)) return "changes-result";
                    return ChangesArguments(parameters) ? "changes-args" : "observes";
                case "finalizer":
                    return code.ReturnType != typeof(void) ? "changes-result" : "observes";
                default: return "observes";
            }
        }

        static bool ChangesArguments(ParameterInfo[] parameters)
        {
            return parameters.Any(p => p.ParameterType.IsByRef && !(p.Name ?? "").StartsWith("__", StringComparison.Ordinal));
        }

        // Harmony's own spelling, Namespace.Type:Method(argtypes), so what is observed and what a mod declares compare as plain strings.
        internal static string Describe(MethodBase method)
        {
            string type = method.DeclaringType == null ? "" : method.DeclaringType.FullName;
            return type + ":" + method.Name + "(" + string.Join(",", method.GetParameters().Select(p => p.ParameterType.FullName ?? p.ParameterType.Name)) + ")";
        }

        static string GameVersion()
        {
            try { return Process.GetCurrentProcess().MainModule?.FileVersionInfo.FileVersion; }
            catch (Exception ex) when (ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception || ex is NotSupportedException)
            {
                Entry.Log("The game's version could not be read for the observed hooks: " + ex.Message);
                return null;
            }
        }

        internal class Observation
        {
            public int Schema { get; set; } = 1;
            public string CapturedAt { get; set; }
            public string GameVersion { get; set; }
            public List<ObservedMod> Mods { get; set; } = new List<ObservedMod>();
            public List<ObservedHook> Hooks { get; set; } = new List<ObservedHook>();
            public ObservedChannels Channels { get; set; }
        }

        internal class ObservedMod
        {
            public string Key { get; set; }
            public string Name { get; set; }
            public string Version { get; set; }
        }

        internal class ObservedHook
        {
            public string Target { get; set; }
            public List<ObservedPatch> Patches { get; set; } = new List<ObservedPatch>();
        }

        internal class ObservedPatch
        {
            public string Owner { get; set; }
            public string Assembly { get; set; }
            public string HarmonyId { get; set; }
            public string Kind { get; set; }
            public string Effect { get; set; }
            public int Priority { get; set; }
            public List<string> Before { get; set; } = new List<string>();
            public List<string> After { get; set; } = new List<string>();
            public string Method { get; set; }
        }
    }
}
