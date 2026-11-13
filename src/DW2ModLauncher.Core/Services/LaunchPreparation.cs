using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using DW2ModLauncher.Core.Diagnostics;
using DW2ModLauncher.Core.Models;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>
    /// Everything the launcher does before it starts the game, without a window and without starting the game: read the game's enabled mods, build
    /// the loader manifest (with each mod's merged channel files, settings, translations and patches) and work out the game's command line.
    /// A tool that starts the game itself (the dev-only test MCP server's game_launch) calls this through <see cref="Flag"/>, so a test session
    /// loads code mods exactly as a player's launch does. Nothing the launcher or a player owns is changed: the loader DLL is copied beside the
    /// manifest, so the launcher's own Loader folder and hooks-observed.json are left alone, and mods.json is read but never written.
    /// </summary>
    public static class LaunchPreparation
    {
        /// <summary>First argument of the helper process: <c>--prepare-launch --game-root &lt;folder&gt; --out &lt;folder&gt; [--extra-mod &lt;folder&gt;]...</c></summary>
        public const string Flag = "--prepare-launch";

        /// <summary>The file written in the output folder: a serialized <see cref="Result"/>.</summary>
        public const string ResultFile = "launch.json";

        public class Result
        {
            /// <summary>The game's command line (without the executable), ready to start the game with; null when <see cref="Error"/> is set.</summary>
            public string Arguments { get; set; }
            /// <summary>The folder holding the loader DLL copy and manifest.json the arguments point at.</summary>
            public string LoaderDirectory { get; set; }
            /// <summary>The code mods the loader will start, in order, as "name (dll)".</summary>
            public List<string> Mods { get; set; } = new List<string>();
            /// <summary>One line per merged channel slot saying which mods supplied it (ModChannels.Describe).</summary>
            public List<string> Channels { get; set; } = new List<string>();
            /// <summary>What the launcher would warn about before a launch, and what it could not hand over; the launch goes ahead.</summary>
            public List<string> Problems { get; set; } = new List<string>();
            /// <summary>Why no launch could be prepared, or null.</summary>
            public string Error { get; set; }
        }

        /// <summary>The helper process: writes <see cref="ResultFile"/> into the output folder and returns the exit code (0 prepared, 1 not).</summary>
        public static int Run(string[] args)
        {
            string gameRoot = null;
            string outDir = null;
            List<string> extras = new List<string>();
            for (int i = 1; i < args.Length; i++)
            {
                string value = i + 1 < args.Length ? args[i + 1] : null;
                if (args[i] == "--game-root" && value != null) { gameRoot = value; i++; }
                else if (args[i] == "--out" && value != null) { outDir = value; i++; }
                else if (args[i] == "--extra-mod" && value != null) { extras.Add(value); i++; }
                else { Console.Error.WriteLine("Unknown or incomplete argument: " + args[i]); return 1; }
            }
            if (string.IsNullOrWhiteSpace(gameRoot) || string.IsNullOrWhiteSpace(outDir))
            {
                Console.Error.WriteLine(Flag + " needs --game-root and --out.");
                return 1;
            }
            Result result;
            try
            {
                result = Prepare(gameRoot, outDir, extras);
            }
            catch (Exception ex)
            {
                // the reporting point: the caller reads the file and shows the player (or the tester) exactly this
                Logger.LogException("Prepare launch", ex);
                result = new Result { Error = ex is PathTokenException path ? "A mod setting names a folder that cannot be resolved: " + path.Message : ex.GetType().Name + ": " + ex.Message };
            }
            try
            {
                Directory.CreateDirectory(outDir);
                File.WriteAllText(Path.Combine(outDir, ResultFile), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                Logger.LogException("Write the prepared launch", ex);
                Console.Error.WriteLine("Could not write " + Path.Combine(outDir, ResultFile) + ": " + ex.Message);
                return 1;
            }
            return result.Error == null ? 0 : 1;
        }

        /// <summary>
        /// Prepares a launch of the game installed in <paramref name="gameRoot"/> with the mods its mods.json enables (the launcher's own settings
        /// supply everything else: the Workshop folder, the language, per-mod settings). Each folder in <paramref name="extraMods"/> is a mod
        /// loaded first whether or not mods.json lists it, for a dev-only mod that must never be enabled for play.
        /// </summary>
        public static Result Prepare(string gameRoot, string outDir, IList<string> extraMods)
        {
            if (!GameFolder.IsGameRoot(gameRoot)) throw new InvalidOperationException(gameRoot + " is not a Distant Worlds 2 folder.");
            Result result = new Result();
            Func<string, string> t = key => Localization.Get("en", key);

            LauncherSettingsStore store = new LauncherSettingsStore(Path.Combine(UserDataRoot.GetLauncherDataRoot(AppContext.BaseDirectory), "launcher_settings.json"));
            LauncherSettings settings = store.Load();
            if (store.LoadError != null) result.Problems.Add("⚠ " + store.LoadError);
            settings.GameRoot = gameRoot;
            settings.ManagedModsRoot = Path.Combine(gameRoot, "mods");
            LauncherSettingsStore.Normalize(settings);
            SteamSwitch.Resolve(settings);
            foreach (InstalledLanguagePacks.Problem problem in Localization.UseInstalledPacks(settings.ManagedModsRoot, settings.EffectiveWorkshopRoot))
                result.Problems.Add("⚠ " + problem.File + ": " + problem.Message);
            if (!Localization.AvailableLanguageCodes().Contains(settings.Language)) settings.Language = "en";

            ModOrderState modOrder = ModOrderStore.Read(ModOrderStore.PathFor(settings.ManagedModsRoot, settings.GameRoot));
            if (modOrder.ReadFailed) throw new InvalidOperationException("mods.json in " + settings.ManagedModsRoot + " could not be read (see the launcher log).");
            if (!modOrder.FileFound) result.Problems.Add("⚠ " + Path.Combine(settings.ManagedModsRoot, "mods.json") + " does not exist, so no mod is enabled.");

            List<ModInfo> installed = ModScanner.ScanMods(settings.ManagedModsRoot, false, t);
            installed.AddRange(ModScanner.ScanMods(settings.EffectiveWorkshopRoot, true, t));
            foreach (ModInfo bad in installed.Where(m => m.ModJsonError != null)) result.Problems.Add("⚠ " + bad.DisplayName + ": " + bad.ModJsonError);
            foreach (ModInfo bad in installed.Where(m => m.LauncherJsonError != null)) result.Problems.Add("⚠ " + bad.DisplayName + ": dw2modlauncher.json is not valid: " + bad.LauncherJsonError);

            List<ModInfo> enabled = GameLauncher.OrderedEnabled(installed, modOrder, settings);
            // mods.json is not written here (that is the caller's file), so the dependency order is applied to this launch only
            ModDependencies.OrderResult dependencyOrder = ModDependencies.Enforce(enabled, t);
            if (dependencyOrder.Cycle.Count > 0)
                result.Problems.Add("⚠ The load-order rules contradict each other for: " + string.Join(", ", dependencyOrder.Cycle.Select(m => m.DisplayName ?? m.Id)));
            if (dependencyOrder.Changed)
                result.Problems.Add("⚠ Loaded in a different order than mods.json to meet dependency rules (mods.json is not changed): " + string.Join("; ", dependencyOrder.Moves));
            foreach (ModInfo mod in enabled)
                foreach (string problem in ModDependencies.ProblemsOf(mod, enabled, installed, t)) result.Problems.Add("⚠ " + (mod.DisplayName ?? mod.Id) + ": " + problem);

            List<ModInfo> loadOrder = new List<ModInfo>();
            foreach (string folder in extraMods ?? new List<string>())
            {
                string modJson = Directory.Exists(folder) ? ModScanner.FindDirectModJson(folder) : null;
                if (modJson == null) throw new InvalidOperationException("The extra mod folder " + folder + " does not exist or has no mod.json.");
                loadOrder.Add(ModScanner.ReadModInfo(folder, modJson, false, t));
            }
            loadOrder.AddRange(dependencyOrder.Order);

            string loaderDll = GameLauncher.LoaderDllPath();
            if (!File.Exists(loaderDll)) throw new FileNotFoundException("The launcher's Loader DLL is missing (build the launcher).", loaderDll);
            string loaderDirectory = Path.Combine(Path.GetFullPath(outDir), "Loader");
            Directory.CreateDirectory(loaderDirectory);
            File.Copy(loaderDll, Path.Combine(loaderDirectory, Path.GetFileName(loaderDll)), true);

            string language = WidgetLanguage.Choose(loadOrder, settings, out string unknownLanguage);
            if (unknownLanguage != null) result.Problems.Add("⚠ " + NamedText.Fill(t("LaunchWidgetLanguageUnknown"), ("Language", unknownLanguage)));
            LoaderManifest manifest = GameLauncher.WriteLoaderManifest(loadOrder, "", WidgetLanguage.Strings(language), language, settings.GameRoot,
                loaderDirectory, Path.Combine(Path.GetFullPath(outDir), "hooks-observed.json"), settings.GameMonitor);
            result.Problems.AddRange(LaunchDiagnostics.Build(enabled, installed, modOrder, loaderDll, manifest.Entries, t));
            result.Problems.AddRange(manifest.ChannelProblems);

            result.Channels.AddRange(ModChannels.Describe(loadOrder));
            result.Mods.AddRange(manifest.Entries.Select(e => e.DisplayName + " (" + Path.GetFileName(e.DllPath) + ")"));
            result.LoaderDirectory = loaderDirectory;
            result.Arguments = GameLauncher.BuildArguments(LaunchMode.Run, manifest.Fonts.LastOrDefault()?.Name, loaderDirectory, settings.SkipSplash);
            return result;
        }
    }
}
