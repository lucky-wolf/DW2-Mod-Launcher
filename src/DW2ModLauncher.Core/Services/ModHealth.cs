using DW2ModLauncher.Core.Models;

namespace DW2ModLauncher.Core.Services
{
    public static class ModHealth
    {
        // 0 = disabled/n-a, 1 = ok, 2 = caution (duplicates/low-risk/identical/update
        // available), 3 = conflict (high-risk file collision). Shared between the
        // Health column's text/color and its column-sort order.
        public static int Severity(ModInfo mod, bool isSelected)
        {
            if (mod == null || !isSelected) return 0;
            if (mod.HighRiskConflictCount > 0 || mod.EnabledCopyCount > 0) return 3;
            bool caution = mod.LowRiskConflictCount > 0 || mod.IdenticalFileCount > 0 ||
                           (mod.IsWorkshop && mod.UpdateState == "update") || LauncherRequirement.IsUnmet(mod);
            return caution ? 2 : 1;
        }
    }
}
