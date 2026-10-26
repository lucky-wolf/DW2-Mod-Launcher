using System;
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

        public static int Install(string baseDir)
        {
            _logPath = Path.Combine(baseDir, "dw2modlauncher.log");
            Type game = FindGameType();
            if (game == null)
            {
                Report("DistantWorlds2.DWGame");
                return 0;
            }
            Harmony harmony = new Harmony("dw2modlauncher.loader.gamestate");
            HarmonyMethod start = new HarmonyMethod(typeof(GameState).GetMethod(nameof(GameStarting), BindingFlags.Static | BindingFlags.NonPublic));
            int hooked = 0;
            foreach (string name in new[] { "StartGameNewGenerate", "LoadGame" })
            {
                MethodInfo method = game.GetMethod(name, BindingFlags.Public | BindingFlags.Instance);
                if (method == null)
                {
                    Report("DWGame." + name);
                    continue;
                }
                harmony.Patch(method, prefix: start);
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
        private static void GameStarting()
        {
            int generation = System.Threading.Interlocked.Increment(ref _generation);
            Log("A real game is starting (generation " + generation + ").");
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
