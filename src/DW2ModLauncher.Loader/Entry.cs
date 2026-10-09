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
    // docs/DLL Injection.md.
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
            _logPath = Path.Combine(LogDirectory(baseDir), "dw2modlauncher.log");
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

            // After the mods, so the panel already lists all of them; a widget that cannot install changes nothing for the mods.
            try
            {
                StatusWidget.Install(Log);
            }
            catch (Exception ex)
            {
                Log("ERROR status widget not installed: " + ex);
            }

            InstallXmlPatching(manifest, baseDir);
            InstallFontBundles(manifest, baseDir);
            InstallTextFiles(manifest, baseDir);
            InstallTitleLookup(manifest, baseDir);
        }

        // Kept in its own method so a missing 0Harmony.dll fails here (caught, logged) instead of stopping the mods above from loading:
        // the JIT only resolves Harmony types when this method is first called.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void InstallXmlPatching(LoaderManifest manifest, string baseDir)
        {
            try
            {
                int files = XmlPatchHooks.Install(manifest, baseDir);
                if (files > 0) Log("XML patching: " + files + " patch file(s) loaded; see patches.log.");
            }
            catch (Exception ex)
            {
                Log("ERROR: XML patching could not be installed (mods are unaffected): " + ex);
            }
        }

        // Own method for the same reason as InstallXmlPatching: Harmony types are resolved only when this is first called.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void InstallFontBundles(LoaderManifest manifest, string baseDir)
        {
            try
            {
                int fonts = FontBundleHooks.Install(manifest, baseDir);
                if (fonts > 0) Log("Font bundles: " + fonts + " declared; see fonts.log.");
            }
            catch (Exception ex)
            {
                Log("ERROR: font bundle support could not be installed (mods are unaffected): " + ex);
            }
        }

        // Own method for the same reason as InstallXmlPatching.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void InstallTextFiles(LoaderManifest manifest, string baseDir)
        {
            try
            {
                int files = TextFileHooks.Install(manifest, baseDir);
                if (files > 0) Log("Text files: " + files + " replacement(s) for Hints/dialog/Galactopedia; see textfiles.log.");
            }
            catch (Exception ex)
            {
                Log("ERROR: text file replacement could not be installed (mods are unaffected): " + ex);
            }
        }

        // Own method for the same reason as InstallXmlPatching. Only for translations: a Mod with patches or replaced text files.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void InstallTitleLookup(LoaderManifest manifest, string baseDir)
        {
            try
            {
                if ((manifest?.Patches?.Count ?? 0) == 0 && (manifest?.TextFiles?.Count ?? 0) == 0) return;
                TitleLookupHooks.Install(baseDir);
            }
            catch (Exception ex)
            {
                Log("ERROR: title lookup support could not be installed (mods are unaffected): " + ex);
            }
        }

        private static void LoadOne(LoaderManifestEntry entry)
        {
            string label = string.IsNullOrWhiteSpace(entry?.DisplayName) ? entry?.EntryType ?? "(unknown mod)" : entry.DisplayName;
            // the status id is the DLL's file name without extension, which is also what a mod's assembly name is
            string id = string.IsNullOrWhiteSpace(entry?.DllPath) ? label : Path.GetFileNameWithoutExtension(entry.DllPath);
            string version = null;
            string built = null;
            try
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.DllPath) || string.IsNullOrWhiteSpace(entry.EntryType))
                {
                    Log("SKIP " + label + ": incomplete manifest entry.");
                    ModStatus.Registry.ReportLoad(id, id, null, null, "incomplete manifest entry");
                    return;
                }
                if (!File.Exists(entry.DllPath))
                {
                    Log("SKIP " + label + ": DLL not found at " + entry.DllPath);
                    ModStatus.Registry.ReportLoad(id, id, null, null, "DLL not found at " + entry.DllPath);
                    return;
                }

                Assembly assembly = Assembly.LoadFrom(entry.DllPath);
                version = ReadVersion(assembly);
                built = ReadBuilt(entry.DllPath);
                int split = entry.EntryType.LastIndexOf('.');
                if (split <= 0)
                {
                    Log("SKIP " + label + ": entry type '" + entry.EntryType + "' is not Namespace.Type.Method form.");
                    ModStatus.Registry.ReportLoad(id, id, version, built, "entry type '" + entry.EntryType + "' is not Namespace.Type.Method form");
                    return;
                }
                string typeName = entry.EntryType.Substring(0, split);
                string methodName = entry.EntryType.Substring(split + 1);
                Type type = assembly.GetType(typeName, throwOnError: false);
                if (type == null)
                {
                    Log("SKIP " + label + ": type '" + typeName + "' not found in " + entry.DllPath);
                    ModStatus.Registry.ReportLoad(id, id, version, built, "type '" + typeName + "' not found");
                    return;
                }

                MethodInfo withOptions = type.GetMethod("InitWithOptions", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
                if (withOptions != null)
                {
                    withOptions.Invoke(null, new object[] { entry.SettingsJson ?? "{}" });
                    Log("OK " + label + ": called InitWithOptions(string).");
                    ModStatus.Registry.ReportLoad(id, id, version, built, null);
                    return;
                }

                MethodInfo plain = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
                if (plain == null)
                {
                    Log("SKIP " + label + ": no matching '" + methodName + "()' or 'InitWithOptions(string)' found on " + typeName);
                    ModStatus.Registry.ReportLoad(id, id, version, built, "no '" + methodName + "()' or 'InitWithOptions(string)' on " + typeName);
                    return;
                }
                plain.Invoke(null, null);
                Log("OK " + label + ": called " + methodName + "().");
                ModStatus.Registry.ReportLoad(id, id, version, built, null);
            }
            catch (Exception ex)
            {
                Log("ERROR " + label + ": " + ex);
                // Invoke wraps whatever the mod threw
                Exception cause = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                ModStatus.Registry.ReportLoad(id, id, version, built, cause.GetType().Name + ": " + cause.Message);
            }
        }

        // The assembly's informational version without the "+commit" suffix, so the line shows what a human would call the version.
        private static string ReadVersion(Assembly assembly)
        {
            try
            {
                string v = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? assembly.GetName().Version?.ToString();
                if (string.IsNullOrEmpty(v)) return null;
                int plus = v.IndexOf('+');
                return plus >= 0 ? v.Substring(0, plus) : v;
            }
            catch (Exception)
            {
                return null;
            }
        }

        // The DLL's own file time, so a stale build is visible at a glance.
        private static string ReadBuilt(string dllPath)
        {
            try
            {
                return File.GetLastWriteTime(dllPath).ToString("MM-dd HH:mm");
            }
            catch (Exception)
            {
                return null;
            }
        }

        // The game's own log folder, <install>/data/Logs (where SessionLog.txt lives): the game runs from its install folder, so that is
        // the folder of the running exe. Without a data folder (not running in the game) the log stays next to the loader.
        private static string LogDirectory(string fallback)
        {
            try
            {
                string data = Path.Combine(AppContext.BaseDirectory, "data");
                if (!Directory.Exists(data)) return fallback;
                string logs = Path.Combine(data, "Logs");
                Directory.CreateDirectory(logs);
                return logs;
            }
            catch
            {
                return fallback;
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
