using System;
using System.Diagnostics;
using System.IO;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>Moves a file to the OS's recycle bin / trash instead of deleting it, so a replaced file can always be got back.</summary>
    public static class RecycleBin
    {
        /// <summary>
        /// Sends the file to the recycle bin (Windows) or trash (Linux, through <c>gio trash</c>). Throws <see cref="IOException"/> when that
        /// is not possible; the file is never permanently deleted as a fallback.
        /// </summary>
        public static void Send(string file)
        {
            string full = Path.GetFullPath(file);
            if (!File.Exists(full)) return;
            if (OperatingSystem.IsWindows())
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(full, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                return;
            }
            ProcessStartInfo start = new ProcessStartInfo("gio");
            start.ArgumentList.Add("trash");
            start.ArgumentList.Add(full);
            start.UseShellExecute = false;
            start.RedirectStandardError = true;
            try
            {
                using (Process p = Process.Start(start))
                {
                    string error = p.StandardError.ReadToEnd();
                    p.WaitForExit();
                    if (p.ExitCode != 0) throw new IOException("Could not move " + Path.GetFileName(full) + " to the trash: " + error.Trim());
                }
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                throw new IOException("Could not move " + Path.GetFileName(full) + " to the trash (gio is not available).", ex);
            }
        }
    }
}
