using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DW2ModLauncher.Core.Models;
using DW2ModLauncher.Core.Services.Catalog;

namespace DW2ModLauncher.Core.Services.Publishing
{
    /// <summary>Everything one GitHub release of a mod uploads, written to disk.</summary>
    public class ReleasePackage
    {
        public string Tag { get; set; }
        public string Title { get; set; }
        public string Version { get; set; }
        public string ZipPath { get; set; }
        public string Sha256Path { get; set; }
        public string ManifestPath { get; set; }
        /// <summary>The preview image copy, or null when the mod has none (see <see cref="Warnings"/> when it should have had one).</summary>
        public string PreviewPath { get; set; }
        public string NotesPath { get; set; }
        public long Size { get; set; }
        /// <summary>The files to upload: the zip, its checksum, the manifest and, when there is one, the preview.</summary>
        public List<string> Files { get; } = new List<string>();
        /// <summary>Things the author should know that did not stop the release (a preview left out).</summary>
        public List<string> Warnings { get; } = new List<string>();
    }

    /// <summary>
    /// Builds the files of a mod's GitHub release (docs/plans/mod-catalog.md, section 2): the zip with the mod's published files under one
    /// root folder, its checksum, the manifest the catalog reads, the preview image and the release notes. This is the one place
    /// the release format is defined, and the catalog reads it. Anything that would make the catalog reject
    /// the release is refused here, naming what is wrong; nothing is guessed or filled in.
    /// </summary>
    public static class ReleasePackager
    {
        public const string HooksFile = HookFiles.ManifestFileName;
        /// <summary>GitHub's limit for one release file.</summary>
        public const long MaxAssetBytes = 2L * 1024 * 1024 * 1024;
        /// <summary>The launcher refuses a bigger preview image.</summary>
        public const long MaxPreviewBytes = 8L * 1024 * 1024;

        /// <summary>The release tag: the folder name with other characters as dashes, then "-v" and the version. For example Diplomatic-Corps-v1.0.15.</summary>
        public static string TagFor(string folderName, string version)
        {
            string name = Regex.Replace(folderName ?? "", "[^A-Za-z0-9.]+", "-").Trim('-');
            if (name.Length == 0) throw new InvalidDataException("The mod folder's name has no letters or digits, so there is nothing to build a release tag from.");
            string trimmed = (version ?? "").Trim();
            if (trimmed.Length == 0 || Regex.IsMatch(trimmed, @"[\s/\\:]")) throw new InvalidDataException("The mod's version (" + version + ") cannot be part of a release tag.");
            return name + "-v" + trimmed;
        }

        /// <summary>
        /// Why the catalog would not list this mod, or null: it needs an author and a uid (a UUID) in dw2modlauncher.json, and each dependency
        /// needs a mod name. The launcher writes author and uid where a mod is made or published.
        /// </summary>
        public static string IdentityProblem(LauncherMeta meta)
        {
            if (string.IsNullOrWhiteSpace(meta?.uid)) return "has no uid in dw2modlauncher.json (publishing from the launcher gives it one)";
            if (ModUid.Normalize(meta.uid) == null) return "has a uid (" + meta.uid + ") that is not a UUID";
            if (string.IsNullOrWhiteSpace(meta.author)) return "names no author in dw2modlauncher.json";
            int number = 0;
            foreach (LauncherDependency dep in meta.dependencies ?? new List<LauncherDependency>())
            {
                number++;
                if (!ModDependencies.NamesAMod(dep)) return "has dependency " + number + " that names no mod";
                if (string.IsNullOrWhiteSpace(dep.mod)) return "has dependency " + number + " with no \"mod\" name (the catalog lists dependencies by name)";
                if (!string.IsNullOrWhiteSpace(dep.uid) && ModUid.Normalize(dep.uid) == null) return "has dependency " + dep.mod + " with a uid (" + dep.uid + ") that is not a UUID";
            }
            return null;
        }

        /// <summary>What the mod holds, from its files: code (a .dll beside mod.json), xml (any .xml), translation (a language, or a translations/ folder) and font. A mod may be several; one that is none cannot be listed.</summary>
        public static List<string> KindsOf(IEnumerable<string> files, LauncherMeta meta)
        {
            List<string> list = files.ToList();
            List<string> kinds = new List<string>();
            if (list.Any(f => !f.Contains('/') && f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))) kinds.Add("code");
            if (list.Any(f => f.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))) kinds.Add("xml");
            if (!string.IsNullOrWhiteSpace(meta?.language) || list.Any(f => f.StartsWith("translations/", StringComparison.OrdinalIgnoreCase))) kinds.Add("translation");
            if (!string.IsNullOrWhiteSpace(meta?.font)) kinds.Add("font");
            return kinds;
        }

        /// <summary>
        /// Writes the release files into <paramref name="outFolder"/>/&lt;tag&gt;/. <paramref name="contentFolder"/> is the mod's folder as it is
        /// now (what the Steam upload just sent); the zip's root folder carries its name. Throws <see cref="InvalidDataException"/> naming the
        /// problem when the catalog would reject the mod or GitHub would refuse the zip.
        /// </summary>
        public static ReleasePackage Package(string contentFolder, string outFolder, ModPublishMetadata metadata, LauncherMeta meta, string workshopId, DateTime publishedUtc)
        {
            string root = Path.GetFullPath(contentFolder);
            string name = new DirectoryInfo(root).Name;
            string problem = IdentityProblem(meta);
            if (problem != null) throw new InvalidDataException(name + " " + problem + ", so it cannot be released.");

            string version = (metadata.Version ?? "").Trim();
            string tag = TagFor(name, version);
            List<string> files = PublishedState.ContentFiles(root).OrderBy(f => f, StringComparer.Ordinal).ToList();
            if (File.Exists(Path.Combine(root, HooksFile)) && !files.Contains(HooksFile)) files.Add(HooksFile);
            List<string> kinds = KindsOf(files, meta);
            if (kinds.Count == 0) throw new InvalidDataException(name + " holds no code, XML, translation or font, so it cannot be classified for the catalog.");

            ReleasePackage package = new ReleasePackage { Tag = tag, Version = version, Title = (metadata.DisplayName ?? name) + " " + version };
            string dir = Path.Combine(outFolder, tag);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);

            package.ZipPath = Path.Combine(dir, tag + ".zip");
            using (FileStream stream = File.Create(package.ZipPath))
            using (ZipArchive zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                foreach (string rel in files)
                    zip.CreateEntryFromFile(Path.Combine(root, rel), name + "/" + rel, CompressionLevel.SmallestSize);
            }
            package.Size = new FileInfo(package.ZipPath).Length;
            if (package.Size > MaxAssetBytes)
                throw new InvalidDataException(name + " " + version + ": the zip is " + (package.Size / 1073741824.0).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " GiB, over GitHub's 2 GiB limit for one release file.");
            string digest = Hex(SHA256.HashData(File.ReadAllBytes(package.ZipPath)));
            package.Sha256Path = package.ZipPath + ".sha256";
            WriteText(package.Sha256Path, digest + "  " + Path.GetFileName(package.ZipPath) + "\n");

            package.PreviewPath = WritePreview(root, metadata, tag, dir, package.Warnings);
            package.ManifestPath = Path.Combine(dir, tag + ".manifest.json");
            WriteText(package.ManifestPath, Manifest(name, tag, version, kinds, metadata, meta, workshopId, Path.GetFileName(package.ZipPath), package.Size, digest, publishedUtc));

            package.NotesPath = Path.Combine(dir, tag + ".notes.md");
            WriteText(package.NotesPath, Notes(root, version));

            package.Files.Add(package.ZipPath);
            package.Files.Add(package.Sha256Path);
            package.Files.Add(package.ManifestPath);
            if (package.PreviewPath != null) package.Files.Add(package.PreviewPath);
            return package;
        }

        /// <summary>The release notes: the "- " lines of the version's changelog section (published notes are stamped under the version), or empty. Never invented.</summary>
        public static string Notes(string contentFolder, string version)
        {
            List<string> entries = ModChangelog.Entries(ModChangelog.NotesText(contentFolder, version));
            return entries.Count == 0 ? "" : string.Join("\n", entries.Select(e => "- " + e)) + "\n";
        }

        private static string WritePreview(string root, ModPublishMetadata metadata, string tag, string dir, List<string> warnings)
        {
            string source;
            if (!string.IsNullOrWhiteSpace(metadata.PreviewImage))
            {
                // The mod named one: either it is there or the mod has none, never some other image.
                source = Path.Combine(root, metadata.PreviewImage.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(source))
                {
                    warnings.Add("mod.json names the preview image " + metadata.PreviewImage + ", which is not there, so the release has no preview.");
                    return null;
                }
            }
            else source = ModScanner.FindFallbackImage(root);
            if (source == null) return null;
            if (new FileInfo(source).Length > MaxPreviewBytes)
            {
                warnings.Add("The preview image " + Path.GetFileName(source) + " is over 8 MiB, so the release has no preview.");
                return null;
            }
            string target = Path.Combine(dir, tag + ".preview" + Path.GetExtension(source).ToLowerInvariant());
            File.Copy(source, target);
            return target;
        }

        private static string Manifest(string name, string tag, string version, List<string> kinds, ModPublishMetadata metadata, LauncherMeta meta, string workshopId, string zipName, long size, string sha256, DateTime publishedUtc)
        {
            JsonArray dependencies = new JsonArray();
            foreach (LauncherDependency dep in meta.dependencies ?? new List<LauncherDependency>())
            {
                dependencies.Add(new JsonObject
                {
                    ["mod"] = dep.mod.Trim(),
                    ["uid"] = string.IsNullOrWhiteSpace(dep.uid) ? null : ModUid.Normalize(dep.uid),
                    ["workshopId"] = dep.workshopId.HasValue ? JsonValue.Create(dep.workshopId.Value) : null,
                    ["required"] = dep.required
                });
            }
            JsonObject manifest = new JsonObject
            {
                ["schema"] = 1,
                ["id"] = tag.Substring(0, tag.LastIndexOf("-v", StringComparison.Ordinal)),
                ["uid"] = ModUid.Normalize(meta.uid),
                ["name"] = name,
                ["kinds"] = new JsonArray(kinds.Select(k => (JsonNode)JsonValue.Create(k)).ToArray()),
                ["version"] = version,
                ["tag"] = tag,
                ["author"] = meta.author.Trim(),
                ["shortDescription"] = metadata.ShortDescription ?? "",
                ["workshopId"] = long.TryParse(workshopId, out long id) ? JsonValue.Create(id) : null,
                ["minLauncherVersion"] = meta.minLauncherVersion ?? "",
                ["tags"] = new JsonArray(CatalogTags.Normalize(meta.tags).Select(t => (JsonNode)JsonValue.Create(t)).ToArray()),
                ["dependencies"] = dependencies,
                ["zip"] = zipName,
                ["size"] = size,
                ["sha256"] = sha256,
                ["publishedUtc"] = publishedUtc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture)
            };
            return manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n";
        }

        /// <summary>How a released zip differs from the folder it should hold, file by file ("+ path" missing from the zip, "- path" extra in the zip, "≠ path" different bytes). Zips are compared by content, not hash, since they carry file times.</summary>
        public static List<string> Differences(string zipPath, string contentFolder)
        {
            string root = Path.GetFullPath(contentFolder);
            string name = new DirectoryInfo(root).Name;
            List<string> expected = PublishedState.ContentFiles(root).ToList();
            List<string> problems = new List<string>();
            using (ZipArchive zip = ZipFile.OpenRead(zipPath))
            {
                Dictionary<string, ZipArchiveEntry> entries = zip.Entries.Where(e => e.FullName.Length > 0 && !e.FullName.EndsWith("/", StringComparison.Ordinal))
                    .Where(e => e.FullName != name + "/" + HooksFile) // metadata that changes without the mod changing
                    .ToDictionary(e => e.FullName, StringComparer.Ordinal);
                foreach (string rel in expected.OrderBy(f => f, StringComparer.Ordinal))
                {
                    if (!entries.Remove(name + "/" + rel, out ZipArchiveEntry entry)) { problems.Add("+ " + rel); continue; }
                    using (Stream s = entry.Open())
                    using (MemoryStream copy = new MemoryStream())
                    {
                        s.CopyTo(copy);
                        if (!copy.ToArray().AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(root, rel)))) problems.Add("≠ " + rel);
                    }
                }
                problems.AddRange(entries.Keys.OrderBy(k => k, StringComparer.Ordinal).Select(k => "- " + k.Substring(name.Length + 1)));
            }
            return problems;
        }

        private static string Hex(byte[] bytes) { return Convert.ToHexString(bytes).ToLowerInvariant(); }

        private static void WriteText(string path, string text)
        {
            File.WriteAllText(path, text, new UTF8Encoding(false));
        }
    }
}
