using DW2ModLauncher.Core.Models;

namespace DW2ModLauncher.Core.Services
{
    public static class ModHealth
    {
        // 0 = disabled/n-a, 1 = ok, 2 = caution (duplicates/low-risk/identical/update
        // available), 3 = conflict (high-risk file collision). Shared between the
        // Health column's text/color and its column-sort order.
        /// <summary>
        /// For a mod that is not enabled: 3 or 2 when its code overlaps an enabled mod's badly enough that enabling it would be a conflict or a
        /// caution, else 0. Only the code-hook findings; file collisions and the rest are not worked out for a mod that is not enabled.
        /// </summary>
        public static int HookSeverityIfEnabled(ModInfo mod)
        {
            return mod == null ? 0 : mod.HookConflictCount > 0 ? 3 : mod.HookCautionCount > 0 ? 2 : 0;
        }

        public static int Severity(ModInfo mod, bool isSelected)
        {
            if (mod == null || !isSelected) return 0;
            if (mod.HighRiskConflictCount > 0 || mod.HookConflictCount > 0 || mod.EnabledCopyCount > 0) return 3;
            bool caution = mod.LowRiskConflictCount > 0 || mod.HookCautionCount > 0 || mod.IdenticalFileCount > 0 ||
                           (mod.IsWorkshop && mod.UpdateState == "update") || LauncherRequirement.IsUnmet(mod);
            return caution ? 2 : 1;
        }
    }
}
