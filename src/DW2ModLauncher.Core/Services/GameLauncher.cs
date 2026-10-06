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
        public static ProcessStartInfo BuildStartInfo(string gameRoot, string arguments, IReadOnlyDictionary<string, string> environment = null)
        {
            return BuildStartInfo(gameRoot, arguments, OperatingSystem.IsWindows(), environment);
        }

        internal static ProcessStartInfo BuildStartInfo(string gameRoot, string arguments, bool hostIsWindows, IReadOnlyDictionary<string, string> environment = null)
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            if (hostIsWindows)
            {
                psi.FileName = Path.Combine(gameRoot ?? "", "DistantWorlds2.exe");
                psi.WorkingDirectory = gameRoot;
                psi.Arguments = arguments ?? "";
                // Environment variables can only be set on a process started without the shell.
                psi.UseShellExecute = environment == null || environment.Count == 0;
                if (environment != null)
                {
                    foreach (KeyValuePair<string, string> kv in environment) psi.Environment[kv.Key] = kv.Value;
                }
            }
            else
            {
                psi.FileName = "steam";
                psi.Arguments = "-applaunch " + SteamLocator.AppId + (string.IsNullOrWhiteSpace(arguments) ? "" : " " + arguments);
                psi.UseShellExecute = false;
                // No environment here on purpose: "steam -applaunch" only forwards to the Steam client, which
                // applies the user's own Steam launch options (env vars included) to the game itself.
            }
            return psi;
        }

        /// <summary>Parses the ENV box: one NAME=value per line; blank lines, "#" comments and malformed lines are skipped.</summary>
        public static Dictionary<string, string> ParseEnvironment(string text)
        {
            Dictionary<string, string> env = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string raw in (text ?? "").Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string name = line.Substring(0, eq).Trim();
                if (name.Length == 0 || name.Any(char.IsWhiteSpace)) continue;
                env[name] = line.Substring(eq + 1).Trim();
            }
            return env;
        }

        /// <summary>The ENV box text for a set of variables: one NAME=value per line.</summary>
        public static string FormatEnvironment(IReadOnlyDictionary<string, string> environment)
        {
            if (environment == null) return "";
            return string.Join(Environment.NewLine, environment.Select(kv => kv.Key + "=" + kv.Value));
        }

        /// <summary>Where the shipped loader DLL lives, next to the launcher's own executable.</summary>
        public static string LoaderDllPath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Loader", "DW2ModLauncher.Loader.dll");
        }

        /// <summary>Writes manifest.json beside the loader DLL; the loader reads it at --low-level-inject time.</summary>
        public static void WriteLoaderManifest(IEnumerable<ModInfo> orderedEnabledMods)
        {
            LoaderManifest manifest = LoaderManifestBuilder.Build(orderedEnabledMods.ToList());
            string loaderDir = Path.GetDirectoryName(LoaderDllPath());
            Directory.CreateDirectory(loaderDir);
            File.WriteAllText(Path.Combine(loaderDir, "manifest.json"), JsonSerializer.Serialize(manifest), new UTF8Encoding(false));
        }

        /// <summary>The game command line: the loader injection flag, then the user's own arguments.</summary>
        public static string BuildArguments(string globalLaunchArguments, LaunchMode mode = LaunchMode.Run)
        {
            string loaderDll = GamePaths.ToGameVisiblePath(LoaderDllPath());
            string token = (loaderDll.IndexOf(' ') >= 0 ? "\"" + loaderDll + "\"" : loaderDll) + "!DW2ModLauncher.Loader.Entry.Init";
            List<string> args = new List<string> { "--low-level-inject " + token };
            string modeFlag = mode == LaunchMode.Continue ? "--continue" : mode == LaunchMode.NewGame ? "--new-game" : null;
            // The chosen mode is the only source of --continue / --new-game; Run leaves the user's arguments untouched.
            string userArgs = modeFlag == null ? globalLaunchArguments : StripStartupModeFlags(globalLaunchArguments);
            if (!string.IsNullOrWhiteSpace(userArgs)) args.Add(userArgs.Trim());
            if (modeFlag != null) args.Add(modeFlag);
            return string.Join(" ", args).Trim();
        }

        /// <summary>Removes standalone --continue / --new-game tokens (case-insensitive) from a command line.</summary>
        public static string StripStartupModeFlags(string arguments)
        {
            return Regex.Replace(arguments ?? "", @"(?<!\S)--(?:continue|new-game)(?!\S)", "", RegexOptions.IgnoreCase).Trim();
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
