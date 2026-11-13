using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>
    /// Finds a Distant Worlds 2 bought directly from Matrix (a standard Windows installer) through the entry every such installer leaves
    /// in Windows' list of installed programs: its install location, else the folder the uninstaller (or the program's icon) lives in.
    /// </summary>
    public static class UninstallLocator
    {
        private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

        /// <summary>The fields of one installed-programs entry that can point at the install folder.</summary>
        internal sealed class InstalledProgram
        {
            public string DisplayName = "";
            public string InstallLocation = "";
            public string UninstallString = "";
            public string DisplayIcon = "";
        }

        public static List<string> Candidates()
        {
            if (!OperatingSystem.IsWindows()) return new List<string>();
            return CandidatesFrom(ReadEntries());
        }

        internal static List<string> CandidatesFrom(IEnumerable<InstalledProgram> entries)
        {
            List<string> found = new List<string>();
            foreach (InstalledProgram e in entries)
            {
                if (e.DisplayName == null || e.DisplayName.IndexOf("Distant Worlds 2", StringComparison.OrdinalIgnoreCase) < 0) continue;
                AddIfAny(found, Clean(e.InstallLocation));
                AddIfAny(found, FolderOfProgram(e.UninstallString));
                AddIfAny(found, FolderOfProgram(e.DisplayIcon));
            }
            return found;
        }

        /// <summary>The folder holding the program a command line or icon reference names: <c>"C:\Games\DW2\unins000.exe" /silent</c>, <c>C:\Games\DW2\DistantWorlds2.exe,0</c>.</summary>
        internal static string FolderOfProgram(string commandLine)
        {
            if (string.IsNullOrWhiteSpace(commandLine)) return "";
            string s = commandLine.Trim();
            string program;
            if (s.StartsWith("\"", StringComparison.Ordinal))
            {
                int end = s.IndexOf('"', 1);
                if (end < 0) return "";
                program = s.Substring(1, end - 1);
            }
            else
            {
                int exe = s.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                if (exe < 0) return "";
                program = s.Substring(0, exe + 4);
            }
            // The Windows path form is parsed here whatever the host, so the tests behave the same on Linux.
            int slash = Math.Max(program.LastIndexOf('\\'), program.LastIndexOf('/'));
            return slash <= 0 ? "" : program.Substring(0, slash);
        }

        private static string Clean(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            return path.Trim().Trim('"').TrimEnd('\\', '/');
        }

        private static void AddIfAny(List<string> list, string path)
        {
            if (path.Length > 0 && !list.Contains(path)) list.Add(path);
        }

        [SupportedOSPlatform("windows")]
        private static List<InstalledProgram> ReadEntries()
        {
            List<InstalledProgram> entries = new List<InstalledProgram>();
            foreach (RegistryHive hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
            {
                foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                {
                    using (RegistryKey root = RegistryKey.OpenBaseKey(hive, view))
                    using (RegistryKey uninstall = root.OpenSubKey(UninstallKey))
                    {
                        if (uninstall == null) continue;
                        foreach (string name in uninstall.GetSubKeyNames())
                        {
                            using (RegistryKey k = uninstall.OpenSubKey(name))
                            {
                                if (k == null) continue;
                                entries.Add(new InstalledProgram
                                {
                                    DisplayName = k.GetValue("DisplayName") as string ?? "",
                                    InstallLocation = k.GetValue("InstallLocation") as string ?? "",
                                    UninstallString = k.GetValue("UninstallString") as string ?? "",
                                    DisplayIcon = k.GetValue("DisplayIcon") as string ?? ""
                                });
                            }
                        }
                    }
                }
            }
            return entries;
        }
    }
}
