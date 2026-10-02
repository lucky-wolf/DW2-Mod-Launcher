using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using DW2ModLauncher.Core.Models;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>File-collision and duplicate-install analysis across installed mods. No UI.</summary>
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
            catch { return cache; }
            foreach (string file in files)
            {
                string rel;
                try
                {
                    rel = file.Substring(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length)
                              .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                              .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
                }
                catch { continue; }
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
                bool highRisk = extension == ".dll" || extension == ".exe" || extension == ".xml" || extension == ".json" || extension == ".bin";
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
            catch { return ""; }
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

        public static void AnalyzeDuplicates(IEnumerable<ModInfo> mods)
        {
            List<ModInfo> all = mods.Where(m => m != null).ToList();
            foreach (ModInfo mod in all)
            {
                mod.DuplicateCount = 0;
                if (mod.DuplicateLocations == null) mod.DuplicateLocations = new List<string>();
                else mod.DuplicateLocations.Clear();
            }
            Dictionary<string, List<ModInfo>> groups = new Dictionary<string, List<ModInfo>>(StringComparer.OrdinalIgnoreCase);
            foreach (ModInfo mod in all)
            {
                string identity = Regex.Replace((mod.DisplayName ?? mod.Id ?? Path.GetFileName(mod.Folder) ?? "").Trim().ToLowerInvariant(), "[^a-z0-9ぁ-んァ-ン一-龯]+", "");
                if (identity.Length < 3) continue;
                List<ModInfo> list;
                if (!groups.TryGetValue(identity, out list)) { list = new List<ModInfo>(); groups[identity] = list; }
                list.Add(mod);
            }
            foreach (List<ModInfo> group in groups.Values.Where(g => g.Select(x => x.Folder).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1))
            {
                foreach (ModInfo mod in group)
                {
                    foreach (ModInfo other in group.Where(x => !string.Equals(x.Folder, mod.Folder, StringComparison.OrdinalIgnoreCase)))
                    {
                        string location = (other.SourceName ?? "") + " | " + (other.Version ?? "") + " | " + (other.Folder ?? "");
                        if (!mod.DuplicateLocations.Contains(location)) mod.DuplicateLocations.Add(location);
                    }
                    mod.DuplicateCount = mod.DuplicateLocations.Count;
                }
            }
        }
    }
}
