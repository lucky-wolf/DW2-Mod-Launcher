using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace DW2ModLauncher.Loader
{
    // Entry point invoked by the game's own --low-level-inject flag. This is the ONLY managed
    // mod target the launcher ever injects: --low-level-inject
    //   "<path>\DW2ModLauncher.Loader.dll"!DW2ModLauncher.Loader.Entry.Init
    // Every enabled mod is then loaded from here, in manifest order, via reflection - see
    // docs/dll-injection.md.
    public static class Entry
    {
        private static bool _started;
        private static string _logPath;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Init()
        {
            if (_started) return;
            _started = true;

            string baseDir = Path.GetDirectoryName(typeof(Entry).Assembly.Location) ?? AppContext.BaseDirectory;
            _logPath = Path.Combine(baseDir, "loader.log");
            Log("Entry.Init() called.");

            string manifestPath = Path.Combine(baseDir, "manifest.json");
            LoaderManifest manifest;
            try
            {
                if (!File.Exists(manifestPath))
                {
                    Log("ERROR: manifest.json not found at " + manifestPath);
                    return;
                }
                manifest = JsonSerializer.Deserialize<LoaderManifest>(File.ReadAllText(manifestPath));
            }
            catch (Exception ex)
            {
                Log("ERROR: failed to read/parse manifest.json: " + ex);
                return;
            }

            foreach (LoaderManifestEntry entry in manifest?.Entries ?? new System.Collections.Generic.List<LoaderManifestEntry>())
                LoadOne(entry);
        }

        private static void LoadOne(LoaderManifestEntry entry)
        {
            string label = string.IsNullOrWhiteSpace(entry?.DisplayName) ? entry?.EntryType ?? "(unknown mod)" : entry.DisplayName;
            try
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.DllPath) || string.IsNullOrWhiteSpace(entry.EntryType))
                {
                    Log("SKIP " + label + ": incomplete manifest entry.");
                    return;
                }
                if (!File.Exists(entry.DllPath))
                {
                    Log("SKIP " + label + ": DLL not found at " + entry.DllPath);
                    return;
                }

                Assembly assembly = Assembly.LoadFrom(entry.DllPath);
                int split = entry.EntryType.LastIndexOf('.');
                if (split <= 0)
                {
                    Log("SKIP " + label + ": entry type '" + entry.EntryType + "' is not Namespace.Type.Method form.");
                    return;
                }
                string typeName = entry.EntryType.Substring(0, split);
                string methodName = entry.EntryType.Substring(split + 1);
                Type type = assembly.GetType(typeName, throwOnError: false);
                if (type == null)
                {
                    Log("SKIP " + label + ": type '" + typeName + "' not found in " + entry.DllPath);
                    return;
                }

                MethodInfo withOptions = type.GetMethod("InitWithOptions", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
                if (withOptions != null)
                {
                    withOptions.Invoke(null, new object[] { entry.SettingsJson ?? "{}" });
                    Log("OK " + label + ": called InitWithOptions(string).");
                    return;
                }

                MethodInfo plain = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
                if (plain == null)
                {
                    Log("SKIP " + label + ": no matching '" + methodName + "()' or 'InitWithOptions(string)' found on " + typeName);
                    return;
                }
                plain.Invoke(null, null);
                Log("OK " + label + ": called " + methodName + "().");
            }
            catch (Exception ex)
            {
                Log("ERROR " + label + ": " + ex);
            }
        }

        private static void Log(string message)
        {
            try
            {
                if (string.IsNullOrEmpty(_logPath)) return;
                File.AppendAllText(_logPath, "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " + message + Environment.NewLine);
            }
            catch
            {
                // Logging must never crash the loader.
            }
        }
    }
}
