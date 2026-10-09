using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using DW2ModLauncher.Core.Diagnostics;

namespace DW2ModLauncher.Core.Services.Updates
{
    /// <summary>
    /// The helper half of a self-update: a headless copy of the launcher run as
    /// <c>--apply-update pid stagedDir installDir exeName</c>. Waits for the launcher to exit, copies the staged
    /// files over the install folder (the exe last, so an interrupted copy still leaves a launcher that starts) and
    /// starts the launcher again. Files that are not in the package are never touched (user data lives in AppData).
    /// </summary>
    public static class UpdateApplier
    {
        private const int CopyAttempts = 40;

        public static int Run(string[] args)
        {
            // args[0] is the flag itself.
            if (args.Length < 5 || !int.TryParse(args[1], out int pid)) return 2;
            string staged = args[2], installDir = args[3], exeName = args[4];
            string exePath = Path.Combine(installDir, exeName);
            try
            {
                if (!WaitForExit(pid))
                {
                    Logger.Log("Update", "The launcher did not exit; update not applied.");
                    return 1;
                }
                string[] files = Directory.GetFiles(staged, "*", SearchOption.AllDirectories)
                    .OrderBy(f => string.Equals(Path.GetRelativePath(staged, f), exeName, StringComparison.Ordinal) ? 1 : 0)
                    .ToArray();
                foreach (string file in files) CopyWithRetry(file, Path.Combine(installDir, Path.GetRelativePath(staged, file)));
                Logger.Log("Update", "Installed " + files.Length + " files into " + installDir);
            }
            catch (Exception ex)
            {
                Logger.LogException("Update install", ex);
            }
            // Start whatever is installed: the new launcher normally, the old one if the copy failed.
            try
            {
                Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = false, WorkingDirectory = installDir });
            }
            catch (Exception ex)
            {
                Logger.LogException("Update relaunch", ex);
                return 1;
            }
            return 0;
        }

        private static bool WaitForExit(int pid)
        {
            try
            {
                using (Process p = Process.GetProcessById(pid)) return p.WaitForExit(60000);
            }
            catch (ArgumentException) { return true; } // already gone
        }

        /// <summary>The exe stays locked for a moment after its process ends (antivirus, closing handles), so retry.</summary>
        private static void CopyWithRetry(string from, string to)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(to));
            for (int attempt = 1; ; attempt++)
            {
                try { File.Copy(from, to, true); return; }
                catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException) && attempt < CopyAttempts)
                {
                    Thread.Sleep(500);
                }
            }
        }
    }
}
