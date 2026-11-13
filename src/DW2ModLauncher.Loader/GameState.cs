using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;

namespace DW2ModLauncher.Loader
{
    /// <summary>
    /// Tells code mods when a real game exists. The game loads its data and builds galaxies for more than the game itself: the menu's
    /// background, the new-game wizard's preview. A mod that checks the game's data against its own expectations (and warns when they differ)
    /// would be judging data that is about to be replaced. A real game starts in exactly two places, <c>DWGame.StartGameNewGenerate</c> (a new
    /// game, after the data has been loaded for it, before its galaxy is generated) and <c>DWGame.LoadGame</c> (a saved game, before the save is
    /// read and its post-load fixes run), and <see cref="Generation"/> goes up by one at each. (<c>StartGameExisting</c> is not used: a new game
    /// calls it too, after generating, and a saved game calls it after its post-load fixes.)
    ///
    /// Mods reach this by reflection (a static class with BCL-only signatures, like <c>LiveChannels</c>); the DW2-XL code mods do it in
    /// <c>dll/Shared/GameState.cs</c>. 0 means no real game has started yet: nothing the mod can see is the game's data, so it does not warn
    /// about it. A mod that caches something derived from the game's data keeps the generation with the cache and rebuilds when it changes.
    /// </summary>
    public static class GameState
    {
        private static int _generation;
        private static string _logPath;

        /// <summary>0 until a real game starts, then 1 for the first, 2 for the next (a new game, a loaded save), and so on.</summary>
        public static int Generation => _generation;

        /// <summary>True once a real game has started: the data the mods can see is the game's own.</summary>
        public static bool Ready => _generation > 0;

        /// <summary>
        /// Raised once per real game, when its galaxy is complete: a new game after generation, a saved game after the save is read and its
        /// post-load fixes have run (both end in <c>DWGame.StartGameExisting</c>, which nothing else calls, so the menu's and the wizard's
        /// galaxies never raise it). The argument is the game's <c>Galaxy</c> (typed <c>object</c>: the loader has no reference to the game).
        /// <see cref="Generation"/> is already the new game's when this is raised. Handlers run on the thread that started the game,
        /// inside the game's own start-up, so they should be short. Each handler is called on its own: one that throws is reported on the
        /// status line and in the log, and the others still run. Subscribe at load time; a handler added after a game started is not told
        /// about that game.
        /// </summary>
        public static event Action<object> GameReady;

        /// <summary>
        /// The code mods this session loaded, in load order, one line each: <c>n|id|version|built|ok</c> (the last field is <c>failed</c> when the
        /// loader could not run the mod). The first line is the loader itself: <c>loader|version</c>; after the code mods come the <see cref="InstalledManifest"/> lines, one per enabled mod. Meant for stamping into a saved game
        /// (see <see cref="GameReady"/>) so a bug report's save says which mods, in which versions and order, were running. Plain text, no
        /// newline at the end; <c>|</c> never appears in an id or a version.
        /// </summary>
        public static string ModManifest
        {
            get
            {
                var text = new System.Text.StringBuilder();
                text.Append("loader|").Append(typeof(GameState).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                    ?? typeof(GameState).Assembly.GetName().Version?.ToString() ?? "unknown");
                int n = 0;
                foreach (ModStatusSnapshot mod in ModStatus.Registry.Snapshot())
                {
                    n++;
                    text.Append('\n').Append(n).Append('|').Append(mod.Id).Append('|').Append(mod.Version ?? "unknown").Append('|').Append(mod.Built ?? "unknown")
                        .Append('|').Append(mod.LoadError == null ? "ok" : "failed");
                }
                string installed = InstalledManifest;
                if (installed.Length > 0) text.Append('\n').Append(installed);
                return text.ToString();
            }
        }

        private static List<LoaderManifestInstalledMod> _installed = new List<LoaderManifestInstalledMod>();
        private static bool _startedNew;

        /// <summary>True when the real game now running was created from scratch (its XML was read, and its data built, in this session); false for a saved game, whose data was baked in by whichever session created it. Meaningful from <see cref="GameReady"/> on.</summary>
        public static bool StartedNew => _startedNew;

        /// <summary>
        /// Every enabled mod, code or not, in load order, one line each: <c>installed|folder|name|version|code</c> or <c>installed|folder|name|version|data</c>
        /// (version is <c>unknown</c> when the mod declares none). A new game's XML, XML patches and text are baked into it when it is created,
        /// so a mod author or bug reader needs the versions of these mods at that moment, not at the latest save. A <c>|</c> or line break in a
        /// name or version is replaced by a space (it would break the line format); nothing else is altered. No newline at the end.
        /// </summary>
        public static string InstalledManifest
        {
            get
            {
                var text = new System.Text.StringBuilder();
                foreach (LoaderManifestInstalledMod mod in _installed)
                {
                    if (text.Length > 0) text.Append('\n');
                    text.Append("installed|").Append(Field(mod.Folder)).Append('|').Append(Field(mod.Name)).Append('|')
                        .Append(string.IsNullOrWhiteSpace(mod.Version) ? "unknown" : Field(mod.Version)).Append('|').Append(mod.HasCode ? "code" : "data");
                }
                return text.ToString();
            }
        }

        private static object _galaxy;

        /// <summary>
        /// The "created with" list Positronic Matrix stored in the running game's galaxy (<c>GlobalVariables["mod.positronic-matrix.created-with"]</c>),
        /// the <c>installed|...</c> lines of the mods that built the game's data. Null when no real game is ready or the game has no such value
        /// (created before the list existed, or Positronic Matrix absent). Read for the in-game Mods menu's Game data dialog; the key is
        /// Positronic Matrix's store convention (<c>mod.&lt;owner&gt;.&lt;key&gt;</c>), so a rename there needs one here.
        /// </summary>
        public static string CreatedWith(out bool gameReady)
        {
            object galaxy = _galaxy;
            gameReady = galaxy != null;
            if (galaxy == null) return null;
            FieldInfo field = galaxy.GetType().GetField("GlobalVariables", BindingFlags.Public | BindingFlags.Instance)
                ?? throw new MissingFieldException("Galaxy.GlobalVariables");
            return field.GetValue(galaxy) is IDictionary<string, object> variables && variables.TryGetValue("mod.positronic-matrix.created-with", out object value)
                ? value as string
                : null;
        }

        private static string Field(string value) => (value ?? "").Replace('|', ' ').Replace('\r', ' ').Replace('\n', ' ');

        public static int Install(string baseDir, List<LoaderManifestInstalledMod> installed = null)
        {
            _installed = installed ?? new List<LoaderManifestInstalledMod>();
            _logPath = Path.Combine(baseDir, "dw2modlauncher.log");
            Type game = FindGameType();
            if (game == null)
            {
                Report("DistantWorlds2.DWGame");
                return 0;
            }
            Harmony harmony = new Harmony("dw2modlauncher.loader.gamestate");
            int hooked = 0;
            foreach (string name in new[] { "StartGameNewGenerate", "LoadGame" })
            {
                HarmonyMethod start = new HarmonyMethod(typeof(GameState).GetMethod(name == "LoadGame" ? nameof(LoadStarting) : nameof(NewStarting), BindingFlags.Static | BindingFlags.NonPublic));
                MethodInfo method = game.GetMethod(name, BindingFlags.Public | BindingFlags.Instance);
                if (method == null)
                {
                    Report("DWGame." + name);
                    continue;
                }
                harmony.Patch(method, prefix: start);
                hooked++;
            }
            MethodInfo existing = game.GetMethod("StartGameExisting", BindingFlags.Public | BindingFlags.Instance);
            if (existing == null)
            {
                Report("DWGame.StartGameExisting");
            }
            else
            {
                harmony.Patch(existing, postfix: new HarmonyMethod(typeof(GameState).GetMethod(nameof(GameReadyPostfix), BindingFlags.Static | BindingFlags.NonPublic)));
                hooked++;
            }
            return hooked;
        }

        // The game's own assembly (the executable) holds DWGame; it may not be a name Type.GetType can guess, so the loaded assemblies are searched.
        private static Type FindGameType()
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType("DistantWorlds2.DWGame", false);
                if (type != null) return type;
            }
            return null;
        }

        // Harmony binds nothing here: a prefix with no parameters runs for either method and lets the original go on.
        private static void NewStarting()
        {
            _startedNew = true;
            GameStarting();
        }

        private static void LoadStarting()
        {
            _startedNew = false;
            GameStarting();
        }

        private static void GameStarting()
        {
            int generation = System.Threading.Interlocked.Increment(ref _generation);
            Log("A real game is starting (generation " + generation + ").");
        }

        // __0 is StartGameExisting's first argument, the galaxy. The postfix runs only when the method returned, so a load that failed earlier
        // (LoadGame never reaches StartGameExisting then) raises nothing.
        private static void GameReadyPostfix(object __0)
        {
            _galaxy = __0;
            Action<object> handlers = GameReady;
            if (handlers == null) return;
            Log("A real game is ready (generation " + _generation + "); telling " + handlers.GetInvocationList().Length + " subscriber(s).");
            foreach (Delegate handler in handlers.GetInvocationList())
            {
                string who = handler.Method.DeclaringType?.Assembly.GetName().Name ?? handler.Method.Name;
                try
                {
                    ((Action<object>)handler)(__0);
                }
                catch (Exception ex)
                {
                    Log("ERROR: " + who + " failed while handling GameReady: " + ex);
                    HookStatus.Failed("gamestate-ready-" + who, StatusStrings.Get("WidgetGameReadyFailed", ("Mod", who), ("Problem", ex.Message)), _logPath);
                }
            }
        }

        private static void Report(string what)
        {
            Log("ERROR: game state is not tracked: " + what + " not found (game update?). Mods that wait for a real game will not warn.");
            HookStatus.Warn("gamestate", StatusStrings.Get("WidgetGameStateNotHooked", ("Method", what)), _logPath);
        }

        private static void Log(string message)
        {
            try
            {
                if (_logPath == null) return;
                File.AppendAllText(_logPath, "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " + message + Environment.NewLine);
            }
            catch (Exception ex)
            {
                // Logging must never crash the game, but the status line says the log is lost.
                HookStatus.LogFailed(_logPath, ex);
            }
        }
    }
}
