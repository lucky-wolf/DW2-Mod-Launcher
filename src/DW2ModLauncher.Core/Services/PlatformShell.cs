using System;
using System.Diagnostics;
using System.IO;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>OS-level "open this for the user" actions, so UI code never shells out to explorer.exe directly.</summary>
    public interface IPlatformShell
    {
        void OpenFolder(string path);
        void OpenFile(string path);
        void OpenUrl(string url);
    }

    public static class PlatformShell
    {
        public static IPlatformShell Create()
        {
            return OperatingSystem.IsWindows() ? new WindowsShell() : new XdgShell();
        }
    }

    internal sealed class WindowsShell : IPlatformShell
    {
        public void OpenFolder(string path)
        {
            // explorer.exe's own argument parsing silently falls back to its default folder
            // (observed: opens Documents) on a path with a mix of '/' and '\' separators -
            // e.g. GameRoot-derived paths like "c:/program files (x86)/steam\steamapps\..."
            // (settings.GameRoot itself can be stored that way; ManagedModsRoot inherits it).
            // .NET's own Directory.Exists/Process.Start tolerate the mix fine, so this went
            // unnoticed until explorer.exe itself had to parse it. Path.GetFullPath
            // canonicalizes to all-backslash on Windows, which explorer.exe parses correctly.
            Process.Start("explorer.exe", "\"" + Path.GetFullPath(path) + "\"");
        }

        public void OpenFile(string path)
        {
            ProcessStartInfo start = new ProcessStartInfo();
            start.FileName = path;
            start.WorkingDirectory = Path.GetDirectoryName(path);
            start.UseShellExecute = true;
            Process.Start(start);
        }

        public void OpenUrl(string url)
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
    }

    /// <summary>Linux (and other freedesktop-style systems): everything goes through xdg-open.</summary>
    internal sealed class XdgShell : IPlatformShell
    {
        public void OpenFolder(string path) { XdgOpen(Path.GetFullPath(path)); }
        public void OpenFile(string path) { XdgOpen(path); }
        public void OpenUrl(string url) { XdgOpen(url); }

        private static void XdgOpen(string target)
        {
            ProcessStartInfo start = new ProcessStartInfo("xdg-open");
            start.ArgumentList.Add(target);
            start.UseShellExecute = false;
            Process.Start(start);
        }
    }
}
