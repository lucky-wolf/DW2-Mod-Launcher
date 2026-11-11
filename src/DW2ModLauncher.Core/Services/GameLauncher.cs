using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using DW2ModLauncher.Core.Models;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>
    /// Builds the process that starts DW2. On Windows that is DistantWorlds2.exe itself. On Linux
    /// the game only runs under Proton, so it must be started through the Steam client
    /// ("steam -applaunch"), which supplies the right prefix and runtime.
    /// </summary>
    public static class GameLauncher
    {
        public static ProcessStartInfo BuildStartInfo(string gameRoot, string arguments)
        {
            return BuildStartInfo(gameRoot, arguments, OperatingSystem.IsWindows());
        }

        internal static ProcessStartInfo BuildStartInfo(string gameRoot, string arguments, bool hostIsWindows)
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            if (hostIsWindows)
            {
                psi.FileName = Path.Combine(gameRoot ?? "", "DistantWorlds2.exe");
                psi.WorkingDirectory = gameRoot;
                psi.Arguments = arguments ?? "";
                psi.UseShellExecute = true;
            }
            else
            {
                psi.FileName = "steam";
                psi.Arguments = "-applaunch " + SteamLocator.AppId + (string.IsNullOrWhiteSpace(arguments) ? "" : " " + arguments);
                psi.UseShellExecute = false;
            }
            return psi;
        }

        /// <summary>Where the shipped loader DLL lives, next to the launcher's own executable.</summary>
        public static string LoaderDllPath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Loader", "DW2ModLauncher.Loader.dll");
        }

        /// <summary>
        /// Writes manifest.json beside the loader DLL; the loader reads it at --low-level-inject time. <paramref name="loaderDirectory"/> (default: the
        /// launcher's own Loader folder) is where the manifest goes and must hold the loader DLL the game is started with, which is how
        /// <see cref="LaunchPreparation"/> keeps a test launch from touching the launcher's. <paramref name="observedHooksPath"/> (default: the
        /// launcher's hooks-observed.json) is where that session's patched game code is recorded. <paramref name="gameMonitor"/> is the launcher's Game monitor choice (blank = Automatic).
        /// </summary>
        public static LoaderManifest WriteLoaderManifest(IEnumerable<ModInfo> orderedEnabledMods, string logDirectory = "", Dictionary<string, string> strings = null, string language = null, string gameRoot = null,
            string loaderDirectory = null, string observedHooksPath = null, string gameMonitor = null)
        {
            LoaderManifest manifest = LoaderManifestBuilder.Build(orderedEnabledMods.ToList(), language, gameRoot);
            if (OperatingSystem.IsWindows() && !string.IsNullOrWhiteSpace(gameMonitor))
            {
                string problem = DisplayChoices.Resolve(gameMonitor, DisplayEnumerator.List(), out string rect, out string adapter, out int refreshRate);
                manifest.DisplayRect = rect;
                manifest.DisplayAdapter = adapter;
                manifest.DisplayRefresh = refreshRate;
                if (problem != null) manifest.ChannelProblems.Add("⚠ " + problem);
            }
            manifest.LogDirectory = string.IsNullOrWhiteSpace(logDirectory) ? "" : GamePaths.ToGameVisiblePath(logDirectory.Trim());
            manifest.ObservedHooksPath = GamePaths.ToGameVisiblePath(string.IsNullOrWhiteSpace(observedHooksPath) ? HookFiles.ObservedPath() : observedHooksPath);
            if (strings != null) manifest.Strings = strings;
            string loaderDir = string.IsNullOrWhiteSpace(loaderDirectory) ? Path.GetDirectoryName(LoaderDllPath()) : loaderDirectory;
            Directory.CreateDirectory(loaderDir);
            File.WriteAllText(Path.Combine(loaderDir, "manifest.json"), JsonSerializer.Serialize(manifest), new UTF8Encoding(false));
            return manifest;
        }

        /// <summary>The game command line: the loader injection flag, then the startup mode flag if any, the font, and the game's own --skip-splash when asked.</summary>
        public static string BuildArguments(LaunchMode mode = LaunchMode.Run, string fontBundle = null, string loaderDirectory = null, bool skipSplash = false)
        {
            string loaderDll = GamePaths.ToGameVisiblePath(string.IsNullOrWhiteSpace(loaderDirectory) ? LoaderDllPath() : Path.Combine(loaderDirectory, Path.GetFileName(LoaderDllPath())));
            string token = (loaderDll.IndexOf(' ') >= 0 ? "\"" + loaderDll + "\"" : loaderDll) + "!DW2ModLauncher.Loader.Entry.Init";
            string args = "--low-level-inject " + token;
            if (mode == LaunchMode.Continue) args += " --continue";
            else if (mode == LaunchMode.NewGame) args += " --new-game";
            // A localization mod's font bundle (the Loader makes the game find it in the mod folder). The name is checked by the manifest builder.
            if (!string.IsNullOrWhiteSpace(fontBundle)) args += " --font " + fontBundle.Trim();
            if (skipSplash) args += " --skip-splash";
            return args;
        }

        /// <summary>The mods to load, in load order.</summary>
        public static List<ModInfo> OrderedEnabled(IEnumerable<ModInfo> allMods, ModOrderState order, LauncherSettings settings)
        {
            return allMods.Where(m => order.IsSelected(m, settings)).OrderBy(m =>
            {
                int index = order.IndexOf(m.ActiveToken);
                return index < 0 ? int.MaxValue : index;
            }).ToList();
        }
    }
}
