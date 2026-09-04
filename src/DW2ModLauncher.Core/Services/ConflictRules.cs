using System.IO;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>
    /// Decides which relative file paths inside a MOD folder are excluded from file-conflict detection
    /// (launcher metadata, docs/previews, installer tools, source/archive artifacts).
    /// </summary>
    public static class ConflictRules
    {
        public static bool IsIgnored(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath)) return true;
            string rel = relativePath.Replace('/', '\\').TrimStart('\\');
            string file = Path.GetFileName(rel).ToLowerInvariant();

            // Launcher metadata is not loaded as game content.
            if (file == "mod.json" || file == "mods.json" || file == "launcher.json") return true;

            // Documentation and preview assets may legitimately use the same names in every MOD.
            // Check every subfolder, not only the MOD root.
            if (file.StartsWith("readme") || file.StartsWith("license") || file.StartsWith("licence") ||
                file.StartsWith("manual") || file.StartsWith("changelog") || file.StartsWith("changes") ||
                file.StartsWith("preview.") || file.StartsWith("thumbnail.") || file.StartsWith("thumb.")) return true;

            if (file.EndsWith(".log") || file.EndsWith(".bak") || file.EndsWith(".tmp") || file.EndsWith(".launcher_backup")) return true;
            string ext = Path.GetExtension(file).ToLowerInvariant();

            // Installer/utility programs are launched by the user and are not overlaid by DW2.
            if (ext == ".ps1" || ext == ".bat" || ext == ".cmd" || ext == ".exe") return true;

            // Packaged source, debug files, archives and common document formats are not game data.
            if (ext == ".zip" || ext == ".7z" || ext == ".rar" || ext == ".cs" || ext == ".sln" ||
                ext == ".csproj" || ext == ".pdb" || ext == ".pdf" || ext == ".doc" ||
                ext == ".docx" || ext == ".rtf" || ext == ".url" || ext == ".lnk") return true;
            return false;
        }
    }
}
