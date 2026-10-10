using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using DW2ModLauncher.Core.Models;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>File-collision analysis across enabled mods. No UI.</summary>
    public static class ConflictAnalyzer
    {
        /// <summary>The mod's conflict-relevant files, relative to its content root and lowercased (cached on the mod).</summary>
        public static IEnumerable<string> EnumeratePaths(ModInfo mod)
        {
            if (mod == null) return new string[0];
            if (mod.ConflictPathCache == null) mod.ConflictPathCache = ScanPaths(mod);
            return mod.ConflictPathCache;
        }

        private static List<string> ScanPaths(ModInfo mod)
        {
            List<string> cache = new List<string>();
            string root = RootOf(mod);
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return cache;
            string[] files;
            try { files = Directory.GetFiles(root, "*", SearchOption.AllDirectories); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // The mod's files are unknown, so conflicts involving it cannot be detected: record that on the mod.
                Diagnostics.Logger.LogException("Scan mod files: " + root, ex);
                mod.ScanError = ex.Message;
                return cache;
            }
            foreach (string file in files)
            {
                string rel;
                try
                {
                    rel = file.Substring(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length)
                              .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                              .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
                }
                catch (ArgumentException ex)
                {
                    Diagnostics.Logger.LogException("Conflict path: " + file, ex);
                    mod.ScanError = ex.Message;
                    continue;
                }
                if (ConflictRules.IsIgnored(rel)) continue;
                cache.Add(rel.ToLowerInvariant());
            }
            return cache;
        }

        private static string RootOf(ModInfo mod)
        {
            return !string.IsNullOrWhiteSpace(mod.ContentRoot) ? mod.ContentRoot : mod.Folder;
        }

        /// <summary>
        /// Recomputes every mod's conflict counters and returns the colliding files (lowercased relative path ->
        /// the enabled mods that ship a differing copy). Byte-identical copies are counted but not reported.
        /// </summary>
        /// <summary>
        /// Flags enabled mods that are the same Workshop item (a published local mod and its subscribed copy, say).
        /// Identity is the Workshop item id only - never the name - so an unpublished local mod is never flagged.
        /// </summary>
        public static void AnalyzeEnabledCopies(IEnumerable<ModInfo> mods, Func<ModInfo, bool> isEnabled)
        {
            List<ModInfo> all = mods.Where(m => m != null).ToList();
            foreach (ModInfo mod in all) mod.EnabledCopyCount = 0;
            foreach (IGrouping<string, ModInfo> group in all.Where(isEnabled)
                         .Where(m => ModDetails.WorkshopId(m) != null)
                         .GroupBy(m => ModDetails.WorkshopId(m), StringComparer.OrdinalIgnoreCase))
            {
                List<ModInfo> copies = group.GroupBy(m => m.Key, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
                if (copies.Count < 2) continue;
                foreach (ModInfo mod in copies) mod.EnabledCopyCount = copies.Count - 1;
            }
        }

        public static Dictionary<string, List<ModInfo>> Analyze(IEnumerable<ModInfo> mods, Func<ModInfo, bool> isEnabled)
        {
            Dictionary<string, List<ModInfo>> collisions = new Dictionary<string, List<ModInfo>>(StringComparer.OrdinalIgnoreCase);
            List<ModInfo> all = mods.Where(m => m != null).ToList();
            foreach (ModInfo mod in all)
            {
                mod.ConflictCount = 0;
                if (mod.ConflictFiles == null) mod.ConflictFiles = new List<string>(); else mod.ConflictFiles.Clear();
                if (mod.ConflictMods == null) mod.ConflictMods = new List<string>(); else mod.ConflictMods.Clear();
                mod.IdenticalFileCount = 0;
                mod.LowRiskConflictCount = 0;
                mod.HighRiskConflictCount = 0;
            }

            Dictionary<string, List<ModInfo>> owners = new Dictionary<string, List<ModInfo>>(StringComparer.OrdinalIgnoreCase);
            foreach (ModInfo mod in all.Where(isEnabled))
            {
                HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string rel in EnumeratePaths(mod))
                {
                    if (!seen.Add(rel)) continue;
                    List<ModInfo> list;
                    if (!owners.TryGetValue(rel, out list))
                    {
                        list = new List<ModInfo>();
                        owners[rel] = list;
                    }
                    list.Add(mod);
                }
            }

            foreach (KeyValuePair<string, List<ModInfo>> kv in owners)
            {
                List<ModInfo> unique = kv.Value
                    .Where(isEnabled)
                    .GroupBy(m => m.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .ToList();
                if (unique.Count < 2) continue;
                List<string> fileHashes = unique.Select(m => FileHash(m, kv.Key)).ToList();
                List<string> hashes = fileHashes.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (hashes.Count == 1 && fileHashes.All(x => !string.IsNullOrWhiteSpace(x)))
                {
                    foreach (ModInfo mod in unique) mod.IdenticalFileCount++;
                    continue;
                }
                collisions[kv.Key] = unique;
                string extension = Path.GetExtension(kv.Key).ToLowerInvariant();
                bool highRisk = extension == ".xml";
                foreach (ModInfo mod in unique)
                {
                    mod.ConflictFiles.Add(kv.Key);
                    if (highRisk) mod.HighRiskConflictCount++; else mod.LowRiskConflictCount++;
                    foreach (ModInfo other in unique)
                    {
                        if (other.Key == mod.Key) continue;
                        string otherName = other.DisplayName ?? other.Id ?? "Unknown";
                        if (!mod.ConflictMods.Contains(otherName)) mod.ConflictMods.Add(otherName);
                    }
                }
            }

            foreach (ModInfo mod in all) mod.ConflictCount = mod.ConflictFiles == null ? 0 : mod.ConflictFiles.Count;
            return collisions;
        }

        private static string FileHash(ModInfo mod, string relativePath)
        {
            try
            {
                string file = ResolveFile(RootOf(mod), relativePath);
                if (file == null) return "";
                using (SHA256 sha = SHA256.Create())
                using (FileStream stream = File.OpenRead(file)) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // "" makes the file count as differing, so the conflict is still reported; the reason goes to the log.
                Diagnostics.Logger.LogException("Hash file for conflict check: " + relativePath, ex);
                return "";
            }
        }

        /// <summary>
        /// Finds the real file for a lowercased relative path. Paths are lowercased for comparison, which is
        /// fine on Windows but does not name the file on a case-sensitive filesystem, so match each path
        /// segment case-insensitively when the direct path doesn't exist.
        /// </summary>
        internal static string ResolveFile(string root, string relativePath)
        {
            string[] parts = relativePath.Split(new char[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            string direct = Path.Combine(root, Path.Combine(parts));
            if (File.Exists(direct)) return direct;

            string current = root;
            for (int i = 0; i < parts.Length; i++)
            {
                bool last = i == parts.Length - 1;
                IEnumerable<string> candidates = last ? Directory.EnumerateFiles(current) : Directory.EnumerateDirectories(current);
                string match = candidates.FirstOrDefault(c => string.Equals(Path.GetFileName(c), parts[i], StringComparison.OrdinalIgnoreCase));
                if (match == null) return null;
                current = match;
            }
            return current;
        }
    }
}
