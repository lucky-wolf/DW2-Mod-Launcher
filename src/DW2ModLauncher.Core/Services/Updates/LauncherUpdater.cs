using System;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace DW2ModLauncher.Core.Services.Updates
{
    /// <summary>What <see cref="LauncherUpdater.DownloadAndStageAsync"/> is doing, for the progress box.</summary>
    public struct UpdateProgress
    {
        public bool Extracting;
        public long Done;
        public long Total;
    }

    /// <summary>
    /// Self-update. The launcher downloads and unpacks the new release into a work folder, then re-runs a copy of
    /// itself from there with <see cref="Flag"/> (see <see cref="UpdateApplier"/>): a running exe can't overwrite its
    /// own files, so the copy waits for this process to exit, copies the new files over the install folder and
    /// starts the new launcher. Nothing in the install folder changes until that last step, so a cancel before it
    /// just deletes the work folder.
    /// </summary>
    public static class LauncherUpdater
    {
        public const string Flag = "--apply-update";

        private static string InstallDir { get { return Path.GetDirectoryName(Environment.ProcessPath); } }

        /// <summary>False for dev builds and for `dotnet run` (the process is the dotnet host, not the launcher).</summary>
        public static bool CanSelfUpdate
        {
            get
            {
                string exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe)) return false;
                if (string.Equals(Path.GetFileNameWithoutExtension(exe), "dotnet", StringComparison.OrdinalIgnoreCase)) return false;
                return !AppVersion.Current.EndsWith("-dev", StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>Downloads the release package and unpacks it. Returns the folder holding the new launcher files; cleans up after itself on failure or cancel.</summary>
        public static async Task<string> DownloadAndStageAsync(LauncherRelease release, string workDir, IProgress<UpdateProgress> progress, CancellationToken token)
        {
            CleanUp(workDir);
            Directory.CreateDirectory(workDir);
            try
            {
                string archive = Path.Combine(workDir, release.AssetName);
                using (HttpResponseMessage response = await UpdateChecker.Client.GetAsync(release.AssetUrl, HttpCompletionOption.ResponseHeadersRead, token))
                {
                    response.EnsureSuccessStatusCode();
                    long total = response.Content.Headers.ContentLength ?? release.AssetSize;
                    using (Stream source = await response.Content.ReadAsStreamAsync(token))
                    using (FileStream target = File.Create(archive))
                    {
                        byte[] buffer = new byte[81920];
                        long done = 0;
                        int read;
                        while ((read = await source.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
                        {
                            await target.WriteAsync(buffer, 0, read, token);
                            done += read;
                            progress.Report(new UpdateProgress { Done = done, Total = total });
                        }
                    }
                }

                progress.Report(new UpdateProgress { Extracting = true });
                string unpacked = Path.Combine(workDir, "new");
                await Task.Run(() => Extract(archive, unpacked), token);
                token.ThrowIfCancellationRequested();

                string root = PackageRoot(unpacked);
                string exeName = Path.GetFileName(Environment.ProcessPath);
                if (!File.Exists(Path.Combine(root, exeName)))
                    throw new InvalidDataException("The downloaded package does not contain " + exeName + ".");
                File.Delete(archive);
                return root;
            }
            catch
            {
                CleanUp(workDir);
                throw;
            }
        }

        /// <summary>
        /// Starts the helper copy that installs <paramref name="stagedRoot"/> once this process has exited. The caller
        /// must exit right after this returns.
        /// </summary>
        public static void StartInstall(string stagedRoot, string workDir)
        {
            string exeName = Path.GetFileName(Environment.ProcessPath);
            string helperDir = Path.Combine(workDir, "helper");
            // A single-file build is one exe; otherwise (Linux) the helper needs the whole install folder around it.
            if (File.Exists(Path.Combine(InstallDir, "DW2ModLauncher.dll"))) CopyTree(InstallDir, helperDir);
            else
            {
                Directory.CreateDirectory(helperDir);
                File.Copy(Environment.ProcessPath, Path.Combine(helperDir, exeName), true);
            }

            ProcessStartInfo start = new ProcessStartInfo(Path.Combine(helperDir, exeName))
            {
                UseShellExecute = false,
                WorkingDirectory = helperDir,
                CreateNoWindow = true
            };
            start.ArgumentList.Add(Flag);
            start.ArgumentList.Add(Environment.ProcessId.ToString());
            start.ArgumentList.Add(stagedRoot);
            start.ArgumentList.Add(InstallDir);
            start.ArgumentList.Add(exeName);
            Process.Start(start);
        }

        /// <summary>Removes a work folder, including the leftovers of an earlier update (the helper copy can't delete itself). Best effort.</summary>
        public static void CleanUp(string workDir)
        {
            try { if (Directory.Exists(workDir)) Directory.Delete(workDir, true); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
        }

        private static void Extract(string archive, string destination)
        {
            if (archive.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                ZipFile.ExtractToDirectory(archive, destination);
                return;
            }
            using (FileStream file = File.OpenRead(archive))
            using (GZipStream gzip = new GZipStream(file, CompressionMode.Decompress))
                TarFile.ExtractToDirectory(gzip, destination, true);
        }

        /// <summary>The release packages wrap everything in one folder; step into it.</summary>
        private static string PackageRoot(string unpacked)
        {
            string[] dirs = Directory.GetDirectories(unpacked);
            return dirs.Length == 1 && Directory.GetFiles(unpacked).Length == 0 ? dirs[0] : unpacked;
        }

        private static void CopyTree(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (string dir in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir)));
            foreach (string file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
                File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)), true);
        }
    }
}
