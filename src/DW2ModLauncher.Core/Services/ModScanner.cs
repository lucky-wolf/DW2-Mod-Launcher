using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DW2ModLauncher.Core.Diagnostics;
using DW2ModLauncher.Core.Models;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>
    /// Scans a Mod folder (managed or Workshop) and reads each Mod's mod.json into a ModInfo.
    /// </summary>
    public static class ModScanner
    {
        /// <param name="t">Localization key lookup, matching MainForm.T.</param>
        public static List<ModInfo> ScanMods(string root, bool workshop, Func<string, string> t)
        {
            List<ModInfo> result = new List<ModInfo>();
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return result;
            string[] dirs;
            try { dirs = Directory.GetDirectories(root); }
            catch { return result; }

            foreach (string dir in dirs)
            {
                try
                {
                    string modJson = FindModJson(dir, workshop);
                    if (string.IsNullOrWhiteSpace(modJson)) continue;
                    ModInfo mod = ReadModInfo(dir, modJson, workshop, t);
                    result.Add(mod);
                }
                catch (Exception ex) { Logger.LogException("Read Mod: " + dir, ex); }
            }
            return result.OrderBy(m => m.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        public static string FindModJson(string dir, bool workshop)
        {
            string direct = FindDirectModJson(dir);
            if (!string.IsNullOrWhiteSpace(direct) || !workshop) return direct;
            try
            {
                foreach (string child in Directory.GetDirectories(dir))
                {
                    string nested = FindDirectModJson(child);
                    if (!string.IsNullOrWhiteSpace(nested)) return nested;
                    foreach (string grandchild in Directory.GetDirectories(child))
                    {
                        nested = FindDirectModJson(grandchild);
                        if (!string.IsNullOrWhiteSpace(nested)) return nested;
                    }
                }
            }
            catch { }
            return null;
        }

        public static string FindDirectModJson(string dir)
        {
            try
            {
                foreach (string f in Directory.GetFiles(dir, "*", SearchOption.TopDirectoryOnly))
                    if (Path.GetFileName(f).Equals("mod.json", StringComparison.OrdinalIgnoreCase)) return f;
            }
            catch { }
            return null;
        }

        public static ModInfo ReadModInfo(string dir, string modJson, bool workshop, Func<string, string> t)
        {
            ModInfo m = new ModInfo();
            m.Id = Path.GetFileName(dir);
            m.DisplayName = Path.GetFileName(dir);
            m.Description = "";
            m.Version = "";
            m.Folder = dir;
            m.IsWorkshop = workshop;
            m.SourceName = workshop ? "Steam Workshop" : t("GameModFolder");
            m.ActiveToken = workshop ? "steam/" + m.Id : "mods/" + Path.GetFileName(dir);
            m.ContentRoot = dir;
            m.UpdateState = workshop ? "unknown" : "na";
            m.ConflictFiles = new List<string>();
            m.ConflictMods = new List<string>();
            m.RequiredMods = new List<string>();
            m.OptionalMods = new List<string>();
            m.IncompatibleMods = new List<string>();
            m.LoadBefore = new List<string>();
            m.LoadAfter = new List<string>();
            m.IncludedTools = new List<string>();
            m.IncludedDocuments = new List<string>();
            m.ModJsonPath = modJson;

            string preview = null;
            bool previewNamed = false;
            string descriptionFile = "";
            if (!string.IsNullOrEmpty(modJson) && File.Exists(modJson))
            {
                string text = File.ReadAllText(modJson, Encoding.UTF8);
                try
                {
                    object rootObj = LooseJson.Parse(text);
                    Dictionary<string, object> d = rootObj as Dictionary<string, object>;
                    if (d != null)
                    {
                        m.DisplayName = LooseJson.GetString(d, new string[] { "displayName", "name", "title" }, m.DisplayName);
                        m.Description = LooseJson.GetString(d, new string[] { "description", "shortDescription", "summary" }, "");
                        descriptionFile = LooseJson.GetString(d, new string[] { "descriptionFile" }, "");
                        m.Version = LooseJson.GetString(d, new string[] { "version", "modVersion" }, "");
                        preview = LooseJson.GetString(d, new string[] { "previewImage", "preview", "thumbnail", "icon" }, "");
                        string wid = LooseJson.GetString(d, new string[] { "workshopId", "workshopID" }, "");
                        if (!string.IsNullOrWhiteSpace(wid)) m.WorkshopId = wid;
                        if (workshop && !Regex.IsMatch(m.Id ?? "", "^\\d+$") && !string.IsNullOrWhiteSpace(wid)) m.Id = wid;
                        m.RequiredMods = LooseJson.GetStringList(d, new string[] { "Required", "required", "requires" });
                        m.OptionalMods = LooseJson.GetStringList(d, new string[] { "Optional", "optional" });
                        m.IncompatibleMods = LooseJson.GetStringList(d, new string[] { "Incompatible", "incompatible", "conflicts" });
                        m.LoadBefore = LooseJson.GetStringList(d, new string[] { "LoadBefore", "loadBefore" });
                        m.LoadAfter = LooseJson.GetStringList(d, new string[] { "LoadAfter", "loadAfter" });
                    }
                }
                catch
                {
                    m.DisplayName = LooseJson.ReadJsonStringLoose(text, "displayName", m.DisplayName);
                    m.Description = LooseJson.ReadJsonStringLoose(text, "description", LooseJson.ReadJsonStringLoose(text, "shortDescription", ""));
                    descriptionFile = LooseJson.ReadJsonStringLoose(text, "descriptionFile", "");
                    m.Version = LooseJson.ReadJsonStringLoose(text, "version", "");
                    preview = LooseJson.ReadJsonStringLoose(text, "previewImage", "");
                }
                if (!string.IsNullOrWhiteSpace(preview))
                {
                    string baseDir = Path.GetDirectoryName(modJson);
                    string p = Path.Combine(baseDir, preview.Replace('/', Path.DirectorySeparatorChar));
                    // The mod named a preview: either it is there, or the mod has none. Never show some other image instead.
                    previewNamed = true;
                    if (File.Exists(p)) m.PreviewImage = p;
                }
            }
            if (!string.IsNullOrEmpty(modJson) && File.Exists(modJson))
            {
                m.ContentRoot = Path.GetDirectoryName(modJson);
                if (workshop) m.Folder = m.ContentRoot;
            }
            if (workshop) m.ActiveToken = "steam/" + m.Id;
            // Only a mod that names no preview at all gets the by-convention image (preview/thumb/icon, else any top-level image).
            if (!previewNamed) m.PreviewImage = FindFallbackImage(m.ContentRoot);
            // The file mod.json names in "descriptionFile" is the long description; without one, description.bbcode in the mod root is by convention.
            m.DescriptionOverride = ReadDescriptionFile(m.ContentRoot, descriptionFile) ?? ReadDescriptionFile(m.ContentRoot, "description.bbcode") ?? ReadDescriptionFile(m.ContentRoot, "description.txt");
            m.IncludedTools = FindIncludedTools(m.ContentRoot);
            m.IncludedDocuments = FindIncludedDocuments(m.ContentRoot);
            m.MinLauncherVersion = LauncherMetaReader.Read(m)?.minLauncherVersion?.Trim();
            return m;
        }

        /// <summary>
        /// The trimmed text of the file mod.json names in "descriptionFile" (a path inside the mod folder), or null when the field is
        /// empty, the file is missing, blank, or would resolve outside the mod folder.
        /// </summary>
        public static string ReadDescriptionFile(string root, string descriptionFile)
        {
            if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(descriptionFile) || !Directory.Exists(root)) return null;
            try
            {
                string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string file = Path.GetFullPath(Path.Combine(root, descriptionFile.Trim().Replace('/', Path.DirectorySeparatorChar)));
                StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                if (!file.StartsWith(fullRoot, comparison) || !File.Exists(file)) return null;
                string text = File.ReadAllText(file, Encoding.UTF8).Trim();
                return text.Length == 0 ? null : text;
            }
            catch { return null; }
        }

        public static List<string> FindIncludedDocuments(string root)
        {
            List<string> result = new List<string>();
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return result;
            try
            {
                foreach (string file in Directory.GetFiles(root, "*.*", SearchOption.AllDirectories))
                {
                    string extension = Path.GetExtension(file).ToLowerInvariant();
                    if (extension != ".txt" && extension != ".md" && extension != ".pdf" && extension != ".html" && extension != ".htm" && extension != ".doc" && extension != ".docx") continue;
                    string name = Path.GetFileNameWithoutExtension(file);
                    if (name.IndexOf("README", StringComparison.OrdinalIgnoreCase) < 0 &&
                        name.IndexOf("MANUAL", StringComparison.OrdinalIgnoreCase) < 0 &&
                        name.IndexOf("GUIDE", StringComparison.OrdinalIgnoreCase) < 0 &&
                        name.IndexOf("INSTRUCTION", StringComparison.OrdinalIgnoreCase) < 0 &&
                        name.IndexOf("説明", StringComparison.OrdinalIgnoreCase) < 0 &&
                        name.IndexOf("マニュアル", StringComparison.OrdinalIgnoreCase) < 0 &&
                        name.IndexOf("導入", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    string relative = file.Substring(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length)
                        .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    result.Add(relative);
                }
            }
            catch { }
            return result.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        public static List<string> FindIncludedTools(string root)
        {
            List<string> result = new List<string>();
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return result;
            try
            {
                foreach (string file in Directory.GetFiles(root, "*.*", SearchOption.AllDirectories))
                {
                    string extension = Path.GetExtension(file);
                    if (!extension.Equals(".bat", StringComparison.OrdinalIgnoreCase) &&
                        !extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                    string relative = file.Substring(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length)
                        .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    result.Add(relative);
                }
            }
            catch { }
            return result.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        public static string FindFallbackImage(string dir)
        {
            try
            {
                string[] files = Directory.GetFiles(dir, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(f =>
                    {
                        string e = Path.GetExtension(f).ToLowerInvariant();
                        return e == ".jpg" || e == ".jpeg" || e == ".png";
                    }).ToArray();
                string preferred = files.FirstOrDefault(f =>
                {
                    string n = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                    return n.Contains("preview") || n.Contains("thumb") || n.Contains("icon");
                });
                return preferred ?? files.FirstOrDefault();
            }
            catch { return null; }
        }
    }
}
