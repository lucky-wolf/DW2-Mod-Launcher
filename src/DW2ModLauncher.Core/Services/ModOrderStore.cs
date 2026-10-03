using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using DW2ModLauncher.Core.Diagnostics;
using DW2ModLauncher.Core.Models;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>Reads and writes DW2's mods.json, and does the pure order arithmetic. No UI.</summary>
    public static class ModOrderStore
    {
        public static string PathFor(string managedModsRoot, string gameRoot)
        {
            string root = managedModsRoot;
            if (string.IsNullOrWhiteSpace(root) && !string.IsNullOrWhiteSpace(gameRoot)) root = Path.Combine(gameRoot, "mods");
            return string.IsNullOrWhiteSpace(root) ? null : Path.Combine(root, "mods.json");
        }

        /// <summary>Never throws; a missing file yields an empty, not-found state and a bad one sets ReadFailed.</summary>
        public static ModOrderState Read(string path)
        {
            ModOrderState state = new ModOrderState();
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return state;
                ModOrderDocument document = JsonSerializer.Deserialize<ModOrderDocument>(File.ReadAllText(path, Encoding.UTF8));
                if (document == null || document.order == null) { state.ReadFailed = true; return state; }
                state.Order = document.order.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                state.FileFound = true;
            }
            catch (Exception ex) { state.ReadFailed = true; Logger.LogException("Read DW2 mods.json", ex); }
            return state;
        }

        /// <summary>
        /// Writes the order atomically (temp file, timestamped backup, replace). Returns the list actually written, or
        /// null if the mods folder doesn't exist. Throws on I/O failure, after cleaning up the temp file.
        /// </summary>
        public static List<string> Write(string path, IEnumerable<string> order)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(System.IO.Path.GetDirectoryName(path))) return null;
            string temp = path + ".launcher_tmp";
            try
            {
                ModOrderDocument document = new ModOrderDocument();
                document.order = (order ?? new List<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                string output = JsonSerializer.Serialize(document);
                JsonSerializer.Deserialize<ModOrderDocument>(output);
                File.WriteAllText(temp, output, new UTF8Encoding(false));
                if (File.Exists(path))
                {
                    BackUp(path, output);
                    try { File.Replace(temp, path, null, true); }
                    catch { File.Copy(temp, path, true); File.Delete(temp); }
                }
                else File.Move(temp, path);
                return document.order;
            }
            catch
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                throw;
            }
        }

        private const int BackupsToKeep = 20;

        /// <summary>
        /// Copies the current file to "mods.json.yyyyMMdd-HHmmss-fff.launcher_backup" and keeps only the newest few,
        /// so one bad write can never be the only copy of the previous state. Skipped when the write changes nothing.
        /// </summary>
        private static void BackUp(string path, string newContent)
        {
            try
            {
                if (File.ReadAllText(path, Encoding.UTF8) == newContent) return;
                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", System.Globalization.CultureInfo.InvariantCulture);
                // Two writes inside the same millisecond must not share a name, or the second would overwrite the first backup.
                string backup = path + "." + stamp + ".launcher_backup";
                for (int n = 2; File.Exists(backup); n++) backup = path + "." + stamp + "-" + n + ".launcher_backup";
                File.Copy(path, backup, false);
                string folder = System.IO.Path.GetDirectoryName(path);
                string[] old = Directory.GetFiles(folder, System.IO.Path.GetFileName(path) + ".*.launcher_backup");
                Array.Sort(old, StringComparer.Ordinal);
                for (int i = 0; i < old.Length - BackupsToKeep; i++)
                    try { File.Delete(old[i]); } catch { }
            }
            catch (Exception ex) { Logger.LogException("Back up DW2 mods.json", ex); }
        }

        /// <summary>The order with the token removed and, if enabling, appended at the end.</summary>
        public static List<string> WithEnabled(IEnumerable<string> current, string token, bool enabled)
        {
            List<string> next = new List<string>(current ?? new List<string>());
            next.RemoveAll(x => x.Equals(token, StringComparison.OrdinalIgnoreCase));
            if (enabled) next.Add(token);
            return next;
        }

        /// <summary>
        /// Applies a new relative order for a subset of mods (e.g. the visible list, top to bottom) to the full
        /// order: the subset's slots are refilled in the new sequence and every other entry keeps its place.
        /// </summary>
        public static List<string> Reorder(IEnumerable<string> current, IList<string> orderedSubset)
        {
            HashSet<string> category = new HashSet<string>(orderedSubset, StringComparer.OrdinalIgnoreCase);
            List<string> next = new List<string>();
            int replacement = 0;
            foreach (string token in current ?? new List<string>())
            {
                if (category.Contains(token))
                {
                    if (replacement < orderedSubset.Count) next.Add(orderedSubset[replacement++]);
                }
                else next.Add(token);
            }
            while (replacement < orderedSubset.Count) next.Add(orderedSubset[replacement++]);
            return next;
        }
    }
}
