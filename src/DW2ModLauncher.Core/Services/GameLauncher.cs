using System;
using System.Diagnostics;
using System.IO;

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
    }
}
