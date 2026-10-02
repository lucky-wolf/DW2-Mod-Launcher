using System;
using System.Collections.Generic;
using System.Linq;
using DW2ModLauncher.Core.Models;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>
    /// DW2's own mods.json load order as last read, plus the rules for deciding whether a mod counts as
    /// enabled. When mods.json exists it is authoritative; otherwise the launcher's own saved selection is used.
    /// </summary>
    public class ModOrderState
    {
        public List<string> Order { get; set; } = new List<string>();
        public bool FileFound { get; set; }
        public bool ReadFailed { get; set; }

        public int IndexOf(string token)
        {
            return Order == null ? -1 : Order.FindIndex(x => string.Equals(x, token, StringComparison.OrdinalIgnoreCase));
        }

        public bool Contains(string token) { return IndexOf(token) >= 0; }

        public bool IsSelected(ModInfo mod, LauncherSettings settings)
        {
            if (mod == null) return false;
            if (FileFound && !string.IsNullOrWhiteSpace(mod.ActiveToken)) return Contains(mod.ActiveToken);
            bool selected;
            if (settings.SelectedMods != null && settings.SelectedMods.TryGetValue(mod.Key, out selected)) return selected;
            return false;
        }

        // Red conflicts are based only on the authoritative enabled set.
        // Installed but disabled Workshop/local copies must not participate.
        public bool IsEnabledForConflict(ModInfo mod, LauncherSettings settings)
        {
            if (mod == null || string.IsNullOrWhiteSpace(mod.ActiveToken)) return false;
            if (FileFound) return Contains(mod.ActiveToken);
            bool enabled;
            return settings.SelectedMods != null && settings.SelectedMods.TryGetValue(mod.Key, out enabled) && enabled;
        }
    }
}
