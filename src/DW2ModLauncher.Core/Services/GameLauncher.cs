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

        /// <summary>Writes manifest.json beside the loader DLL; the loader reads it at --low-level-inject time.</summary>
        public static void WriteLoaderManifest(IEnumerable<ModInfo> orderedEnabledMods)
        {
            LoaderManifest manifest = LoaderManifestBuilder.Build(orderedEnabledMods.ToList());
            string loaderDir = Path.GetDirectoryName(LoaderDllPath());
            Directory.CreateDirectory(loaderDir);
            File.WriteAllText(Path.Combine(loaderDir, "manifest.json"), JsonSerializer.Serialize(manifest), new UTF8Encoding(false));
        }

        /// <summary>The game command line: the loader injection flag, then the startup mode flag if any.</summary>
        public static string BuildArguments(LaunchMode mode = LaunchMode.Run)
        {
            string loaderDll = GamePaths.ToGameVisiblePath(LoaderDllPath());
            string token = (loaderDll.IndexOf(' ') >= 0 ? "\"" + loaderDll + "\"" : loaderDll) + "!DW2ModLauncher.Loader.Entry.Init";
            string args = "--low-level-inject " + token;
            if (mode == LaunchMode.Continue) args += " --continue";
            else if (mode == LaunchMode.NewGame) args += " --new-game";
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
