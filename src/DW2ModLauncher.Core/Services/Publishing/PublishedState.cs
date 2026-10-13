using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DW2ModLauncher.Core.Models;

namespace DW2ModLauncher.Core.Services.Publishing
{
    /// <summary>One file of the content folder as it was when last published.</summary>
    public class PublishedFile
    {
        public long Size { get; set; }
        /// <summary>Last-write time (UTC ticks) seen at publish. Equal size and time means "unchanged" without hashing.</summary>
        public long ModifiedUtcTicks { get; set; }
        public string Hash { get; set; }
    }

    /// <summary>Per-file record of a mod's content folder at its last publish.</summary>
    public class PublishedManifest
    {
        public DateTime PublishedUtc { get; set; }
        /// <summary>Keyed by path relative to the content folder, with '/' separators.</summary>
        public Dictionary<string, PublishedFile> Files { get; set; } = new Dictionary<string, PublishedFile>(StringComparer.Ordinal);
    }

    /// <summary>What differs between a content folder and its last published manifest. Lists are relative paths, sorted.</summary>
    public class PublishedChanges
    {
        public List<string> Added { get; } = new List<string>();
        public List<string> Removed { get; } = new List<string>();
        public List<string> Modified { get; } = new List<string>();

        public int Count { get { return Added.Count + Removed.Count + Modified.Count; } }
        public bool IsClean { get { return Count == 0; } }

        /// <summary>Every changed path, each marked + (added), - (removed) or ~ (modified), in path order.</summary>
        public IEnumerable<string> Describe()
        {
            return Added.Select(p => "+ " + p).Concat(Removed.Select(p => "- " + p)).Concat(Modified.Select(p => "~ " + p))
                .OrderBy(s => s.Substring(2), StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Records what a publish uploaded and later compares the folder against it, so the launcher can say whether a local mod
    /// has changed since its last publish. Records live under UserDataRoot()\PublishedState\&lt;workshop id&gt;.json - never in
    /// the mod's folder, where they would change what they measure and a Workshop re-sync could overwrite them.
    /// </summary>
    public static class PublishedState
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions { WriteIndented = true };

        public static string GetRecordPath(string workshopId, string dataRoot = null)
        {
            string token = workshopId ?? throw new ArgumentNullException(nameof(workshopId));
            foreach (char c in Path.GetInvalidFileNameChars()) token = token.Replace(c, '_');
            return Path.Combine(dataRoot ?? UserDataRoot.Get(), "PublishedState", token + ".json");
        }

        /// <summary>Reads every file under the folder (hashing all of them). Used right after a publish, when everything is "as uploaded".</summary>
        public static PublishedManifest Capture(string contentFolder)
        {
            PublishedManifest manifest = new PublishedManifest { PublishedUtc = DateTime.UtcNow };
            foreach (string file in EnumerateFiles(contentFolder))
                manifest.Files[file] = Describe(contentFolder, file);
            return manifest;
        }

        /// <summary>
        /// Compares the folder with the manifest. A file whose size and time match the record is unchanged; any other is hashed, so a
        /// touch or a rebuild with identical output does not count as a change.
        /// </summary>
        public static PublishedChanges Compare(string contentFolder, PublishedManifest manifest)
        {
            PublishedChanges changes = new PublishedChanges();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string file in EnumerateFiles(contentFolder))
            {
                seen.Add(file);
                if (!manifest.Files.TryGetValue(file, out PublishedFile recorded)) { changes.Added.Add(file); continue; }
                FileInfo info = new FileInfo(Path.Combine(contentFolder, file));
                if (info.Length == recorded.Size && info.LastWriteTimeUtc.Ticks == recorded.ModifiedUtcTicks) continue;
                if (info.Length != recorded.Size || HashFile(info.FullName) != recorded.Hash) changes.Modified.Add(file);
            }
            foreach (string file in manifest.Files.Keys)
                if (!seen.Contains(file)) changes.Removed.Add(file);
            changes.Added.Sort(StringComparer.OrdinalIgnoreCase);
            changes.Removed.Sort(StringComparer.OrdinalIgnoreCase);
            changes.Modified.Sort(StringComparer.OrdinalIgnoreCase);
            return changes;
        }

        /// <summary>
        /// Sets <see cref="ModInfo.PublishStatus"/> and <see cref="ModInfo.UnpublishedChanges"/> from the mod's folder and its stored
        /// record. Throws if the record or the folder cannot be read; the caller decides how to report that.
        /// </summary>
        public static PublishStatus Evaluate(ModInfo mod, string dataRoot = null)
        {
            mod.UnpublishedChanges = new List<string>();
            mod.PublishedUtc = null;
            string id = ModDetails.WorkshopId(mod);
            if (mod.IsWorkshop) return mod.PublishStatus = PublishStatus.NotApplicable;
            if (id == null) return mod.PublishStatus = PublishStatus.Unpublished;
            PublishedManifest manifest = Load(id, dataRoot);
            if (manifest == null) return mod.PublishStatus = PublishStatus.Unknown;
            mod.PublishedUtc = manifest.PublishedUtc;
            PublishedChanges changes = Compare(mod.ContentRoot ?? mod.Folder, manifest);
            mod.UnpublishedChanges = changes.Describe().ToList();
            return mod.PublishStatus = changes.IsClean ? PublishStatus.Current : PublishStatus.Changed;
        }

        /// <summary>The record for a Workshop item, or null when none was ever taken (a mod published before this existed).</summary>
        public static PublishedManifest Load(string workshopId, string dataRoot = null)
        {
            string path = GetRecordPath(workshopId, dataRoot);
            if (!File.Exists(path)) return null;
            PublishedManifest manifest;
            try { manifest = JsonSerializer.Deserialize<PublishedManifest>(File.ReadAllText(path, Encoding.UTF8)); }
            catch (JsonException ex) { throw new InvalidDataException("The published-state record is not valid JSON: " + path + "\n" + ex.Message, ex); }
            if (manifest?.Files == null) throw new InvalidDataException("The published-state record has no file list: " + path);
            manifest.Files = new Dictionary<string, PublishedFile>(manifest.Files, StringComparer.Ordinal);
            return manifest;
        }

        public static void Save(string workshopId, PublishedManifest manifest, string dataRoot = null)
        {
            string path = GetRecordPath(workshopId, dataRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonSerializer.Serialize(manifest, Options), new UTF8Encoding(false));
        }

        /// <summary>Captures the folder as it is now and stores it as the item's published state.</summary>
        public static void Record(string workshopId, string contentFolder, string dataRoot = null)
        {
            Save(workshopId, Capture(contentFolder), dataRoot);
        }

        private static PublishedFile Describe(string contentFolder, string relativePath)
        {
            FileInfo info = new FileInfo(Path.Combine(contentFolder, relativePath));
            return new PublishedFile { Size = info.Length, ModifiedUtcTicks = info.LastWriteTimeUtc.Ticks, Hash = HashFile(info.FullName) };
        }

        private static IEnumerable<string> EnumerateFiles(string contentFolder)
        {
            string root = Path.GetFullPath(contentFolder);
            return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'));
        }

        private static string HashFile(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            using (SHA256 sha = SHA256.Create())
                return Convert.ToHexString(sha.ComputeHash(stream));
        }
    }
}
