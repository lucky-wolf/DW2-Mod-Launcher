using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using DW2ModLauncher.Core.Services.Updates;

namespace DW2ModLauncher.Core.Services.Catalog
{
    /// <summary>Why a catalog mod could not be installed. The message is shown to the player.</summary>
    public class CatalogInstallException : Exception
    {
        public CatalogInstallException(string message) : base(message) { }
    }

    /// <summary>
    /// Downloads a catalog entry's zip, checks it against the index (size and SHA-256), and installs it into the game's mods folder.
    /// Nothing in the mods folder changes until the zip has been downloaded, verified, unpacked and found to be a mod.
    /// </summary>
    public static class CatalogInstaller
    {
        /// <summary>Folders in the mods folder that start with this are the launcher's own work areas (a mod being installed): the mod scanner never lists them.</summary>
        public const string WorkPrefix = ".dw2ml-";

        /// <summary>Downloads the entry's zip into <paramref name="workDir"/> and returns its path. Throws <see cref="CatalogInstallException"/> if the launcher is too old for the mod or the file does not match the index.</summary>
        public static async Task<string> DownloadAsync(CatalogEntry entry, string launcherVersion, string workDir, IProgress<UpdateProgress> progress, CancellationToken token)
        {
            if (entry.MinLauncherVersion != null && UpdateChecker.IsNewer(entry.MinLauncherVersion, launcherVersion))
                throw new CatalogInstallException("This mod needs launcher version " + entry.MinLauncherVersion + " or newer; this is " + launcherVersion + ".");
            Directory.CreateDirectory(workDir);
            string zip = Path.Combine(workDir, entry.Id + "-" + entry.Version + ".zip");
            try
            {
                string hash;
                long done = 0;
                using (HttpResponseMessage response = await UpdateChecker.Client.GetAsync(entry.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, token))
                {
                    response.EnsureSuccessStatusCode();
                    using (Stream source = await response.Content.ReadAsStreamAsync(token))
                    using (FileStream target = File.Create(zip))
                    using (IncrementalHash sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
                    {
                        byte[] buffer = new byte[81920];
                        int read;
                        while ((read = await source.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
                        {
                            await target.WriteAsync(buffer, 0, read, token);
                            sha.AppendData(buffer, 0, read);
                            done += read;
                            progress?.Report(new UpdateProgress { Done = done, Total = entry.Size });
                        }
                        hash = Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
                    }
                }
                Verify(entry, done, hash);
                return zip;
            }
            catch
            {
                if (File.Exists(zip)) File.Delete(zip);
                throw;
            }
        }

        /// <summary>Throws when the downloaded file is not the one the index describes.</summary>
        public static void Verify(CatalogEntry entry, long size, string sha256)
        {
            if (size != entry.Size)
                throw new CatalogInstallException("The download is " + size + " bytes but the catalog says " + entry.Size + ".");
            if (!string.Equals(sha256, entry.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new CatalogInstallException("The download does not match the catalog's checksum (got " + sha256 + ", expected " + entry.Sha256 + ").");
        }

        /// <summary>
        /// Unpacks <paramref name="zip"/> and puts the mod in <paramref name="modsRoot"/>. The zip may hold the mod's files directly, or inside one folder of any name
        /// (a version may be part of it); either way they end up in a folder named <paramref name="modName"/>, or in <paramref name="existingFolder"/> when replacing
        /// an installed copy. The new mod is first copied completely into a work folder beside the target; only then does the old folder go to the recycle bin
        /// (so no old file is left behind) and the work folder is renamed into its place. Any failure before that leaves the installed copy untouched.
        /// The mod's own mod.json must be at the top.
        /// Returns the mod's folder. With <paramref name="entry"/>, the unpacked mod must not contradict it (see <see cref="VerifyIdentity"/>), checked before anything in the mods folder is touched.
        /// </summary>
        public static string Install(string zip, string modName, string modsRoot, string existingFolder, string stageDir, CatalogEntry entry = null)
        {
            if (string.IsNullOrWhiteSpace(modName) || modName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || modName == "." || modName == "..")
                throw new CatalogInstallException("\"" + modName + "\" cannot be used as a folder name.");
            string target = existingFolder ?? Path.Combine(modsRoot, modName);
            if (existingFolder == null && Directory.Exists(target))
                throw new CatalogInstallException("The folder " + target + " already exists and is not this mod, so it was left alone.");
            if (existingFolder != null && !Directory.Exists(existingFolder))
                throw new CatalogInstallException("The installed copy " + existingFolder + " is gone.");

            if (Directory.Exists(stageDir)) Directory.Delete(stageDir, true);
            Directory.CreateDirectory(stageDir);
            string work = null;
            try
            {
                Extract(zip, stageDir);
                if (entry != null) VerifyIdentity(entry, stageDir);
                // Copy the whole mod beside the target first (same volume, so the last step is a rename); nothing the game or launcher reads changes yet.
                work = Path.Combine(modsRoot, WorkPrefix + "install-" + Guid.NewGuid().ToString("N"));
                CopyTree(stageDir, work);
                if (existingFolder != null) RecycleBin.SendFolder(existingFolder);
                else if (Directory.Exists(target))
                    throw new CatalogInstallException("The folder " + target + " appeared while installing and is not this mod, so it was left alone.");
                Directory.Move(work, target);
                work = null;
                return target;
            }
            finally
            {
                if (work != null && Directory.Exists(work)) Directory.Delete(work, true);
                if (Directory.Exists(stageDir)) Directory.Delete(stageDir, true);
            }
        }

        /// <summary>
        /// Throws when the unpacked mod's own dw2modlauncher.json names a <c>uid</c> or an <c>author</c> that differs from the catalog entry's: a zip that
        /// is not the mod the index described. A mod that names neither (released before identities existed) is accepted. An unreadable file is an error too.
        /// </summary>
        public static void VerifyIdentity(CatalogEntry entry, string folder)
        {
            string path = Path.Combine(folder, "dw2modlauncher.json");
            if (!File.Exists(path)) return;
            string uid = null;
            string author = null;
            try
            {
                using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }))
                {
                    if (document.RootElement.ValueKind != JsonValueKind.Object) throw new CatalogInstallException("The mod's dw2modlauncher.json is not a JSON object.");
                    foreach (JsonProperty property in document.RootElement.EnumerateObject())
                    {
                        if (property.Name.Equals("uid", StringComparison.OrdinalIgnoreCase)) uid = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : property.Value.ToString();
                        else if (property.Name.Equals("author", StringComparison.OrdinalIgnoreCase)) author = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : property.Value.ToString();
                    }
                }
            }
            catch (JsonException error)
            {
                throw new CatalogInstallException("The mod's dw2modlauncher.json cannot be read: " + error.Message);
            }
            if (!string.IsNullOrWhiteSpace(uid) && !ModUid.Same(ModUid.Normalize(uid), entry.Uid))
                throw new CatalogInstallException("The download is not the mod the catalog lists: it names the identity " + uid.Trim() + " but the catalog says " + entry.Uid + ". Nothing was installed.");
            if (!string.IsNullOrWhiteSpace(author) && !string.Equals(author.Trim(), entry.Author?.Trim(), StringComparison.OrdinalIgnoreCase))
                throw new CatalogInstallException("The download is not the mod the catalog lists: it names the author \"" + author.Trim() + "\" but the catalog says \"" + entry.Author + "\". Nothing was installed.");
        }

        /// <summary>Unpacks the mod's files into <paramref name="destination"/> (without any wrapping folder), refusing anything that would land outside it.</summary>
        public static void Extract(string zip, string destination)
        {
            string root = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            using (ZipArchive archive = ZipFile.OpenRead(zip))
            {
                List<(ZipArchiveEntry Entry, string[] Parts)> files = new List<(ZipArchiveEntry, string[])>();
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    string name = entry.FullName;
                    if (name.Contains('\\')) throw new CatalogInstallException("The zip has an entry with a backslash in its name (" + name + ").");
                    if (name.EndsWith("/", StringComparison.Ordinal)) continue; // a folder; the files create theirs
                    string[] parts = name.Split('/');
                    if (name.StartsWith("/", StringComparison.Ordinal) || parts.Any(p => p.Length == 0 || p == ".." || p == "." || p.Contains(':')))
                        throw new CatalogInstallException("The zip has an unsafe entry name (" + name + ").");
                    files.Add((entry, parts));
                }

                int strip;
                if (files.Any(f => f.Parts.Length == 1 && string.Equals(f.Parts[0], "mod.json", StringComparison.OrdinalIgnoreCase))) strip = 0;
                else
                {
                    List<string> tops = files.Where(f => f.Parts.Length > 1).Select(f => f.Parts[0]).Distinct(StringComparer.Ordinal).ToList();
                    if (files.Any(f => f.Parts.Length == 1) || tops.Count != 1 || !files.Any(f => f.Parts.Length == 2 && string.Equals(f.Parts[1], "mod.json", StringComparison.OrdinalIgnoreCase)))
                        throw new CatalogInstallException("The zip is not a mod: there is no mod.json at its top, or inside its one folder.");
                    strip = 1;
                }

                foreach ((ZipArchiveEntry entry, string[] parts) in files)
                {
                    string path = Path.GetFullPath(Path.Combine(new[] { root }.Concat(parts.Skip(strip)).ToArray()));
                    if (!path.StartsWith(root, StringComparison.Ordinal))
                        throw new CatalogInstallException("The zip has an entry that would land outside the mod's folder (" + entry.FullName + ").");
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    entry.ExtractToFile(path, false);
                }
            }
        }

        private static void CopyTree(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (string dir in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir)));
            foreach (string file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
                File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)), false);
        }
    }
}
