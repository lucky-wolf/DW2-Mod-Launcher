using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DW2ModLauncher.Core.Models;
using DW2ModLauncher.Core.Services;

namespace DW2ModLauncherBeta
{
    public partial class MainForm
    {
        private void WriteIniValues(string path, Dictionary<string, string> values)
        {
            try
            {
                IniFile.Write(path, values);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, Text);
            }
        }

        private LauncherMeta ReadLauncherMeta(ModInfo mod)
        {
            return LauncherMetaReader.Read(mod);
        }

        private void ApplyManagedSelectionToIni(ModInfo mod, bool selected)
        {
            LauncherMeta meta = ReadLauncherMeta(mod);
            if (meta == null || string.IsNullOrWhiteSpace(meta.iniPath) || string.IsNullOrWhiteSpace(meta.enabledKey)) return;
            string ini = Path.Combine(mod.Folder, meta.iniPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(ini)) return;
            Dictionary<string, string> d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            d[meta.enabledKey] = selected ? "true" : "false";
            if (!string.IsNullOrWhiteSpace(meta.languageKey)) d[meta.languageKey] = settings.Language;
            WriteIniValues(ini, d);
        }

        private void ApplyLanguageToManagedMods()
        {
            List<ModInfo> mods = ScanMods(settings.ManagedModsRoot, false);
            mods.AddRange(ScanMods(settings.WorkshopRoot, true));
            foreach (ModInfo mod in mods)
            {
                LauncherMeta meta = ReadLauncherMeta(mod);
                if (meta == null || string.IsNullOrWhiteSpace(meta.iniPath) || string.IsNullOrWhiteSpace(meta.languageKey)) continue;
                string ini = Path.Combine(mod.Folder, meta.iniPath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(ini)) continue;
                Dictionary<string, string> d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                d[meta.languageKey] = settings.Language;
                WriteIniValues(ini, d);
            }
        }

        private string FindManagedIni(ModInfo mod)
        {
            if (mod == null) return null;
            LauncherMeta meta = ReadLauncherMeta(mod);
            if (meta != null && !string.IsNullOrWhiteSpace(meta.iniPath))
            {
                string configured = Path.Combine(mod.Folder, meta.iniPath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(configured)) return configured;
            }
            try
            {
                string[] files = Directory.GetFiles(mod.Folder, "*.ini", SearchOption.TopDirectoryOnly);
                return files.FirstOrDefault();
            }
            catch { return null; }
        }

        private void OpenSelectedManagedIniEditor()
        {
            if (modList == null || modList.SelectedItems.Count == 0) return;
            ModInfo mod = modList.SelectedItems[0].Tag as ModInfo;
            OpenModConfigEditor(mod);
        }
    }
}
