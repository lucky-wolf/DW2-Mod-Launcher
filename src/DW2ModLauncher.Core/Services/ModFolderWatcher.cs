using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using DW2ModLauncher.Core.Diagnostics;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>
    /// Tells when anything inside the mod folders (local and Workshop) has changed since the last scan, so the launcher can rescan by itself.
    /// A <see cref="FileSystemWatcher"/> per folder only says "something happened"; every wake-up (and every window activation) is then checked
    /// against a fingerprint of the mod folders' contents, so bursts of writes, a missed event (network or exFAT mounts, inotify limits) and
    /// writes the scan doesn't care about (the <c>.git</c> folders of mods under development) all end up the right way.
    /// </summary>
    public sealed class ModFolderWatcher : IDisposable
    {
        private readonly TimeSpan localDebounce;
        private readonly TimeSpan workshopDebounce;
        private readonly object gate = new object();
        private readonly List<FileSystemWatcher> watchers = new List<FileSystemWatcher>();
        private readonly Timer timer;
        private string localRoot;
        private string workshopRoot;
        private string baseline;
        private int generation;
        private bool disposed;

        /// <summary>Raised on a thread-pool thread when the fingerprint differs from the last scan. The new fingerprint is already the baseline, so one change raises it once.</summary>
        public event Action Changed;

        /// <summary>Raised on a thread-pool thread with a player-readable message when watching or fingerprinting fails.</summary>
        public event Action<string> Failed;

        /// <param name="localDebounce">Quiet time after the last event in the local mods folder before it is checked.</param>
        /// <param name="workshopDebounce">The same for the Workshop folder, which Steam fills in bursts while an update downloads.</param>
        public ModFolderWatcher(TimeSpan localDebounce, TimeSpan workshopDebounce)
        {
            this.localDebounce = localDebounce;
            this.workshopDebounce = workshopDebounce;
            timer = new Timer(delegate { Check(); }, null, Timeout.Infinite, Timeout.Infinite);
        }

        /// <summary>
        /// Starts watching these folders (a blank or missing folder is not watched, matching how the scan treats it) and records what they hold now as the
        /// baseline. Call it after every scan: anything changed from here on is what <see cref="Changed"/> reports.
        /// </summary>
        public void Watch(string localMods, string workshopMods)
        {
            int mine;
            lock (gate)
            {
                if (disposed) return;
                mine = ++generation;
                if (!string.Equals(localMods, localRoot, StringComparison.Ordinal) || !string.Equals(workshopMods, workshopRoot, StringComparison.Ordinal))
                {
                    StopWatchers();
                    localRoot = localMods;
                    workshopRoot = workshopMods;
                    StartWatcher(localRoot, localDebounce);
                    StartWatcher(workshopRoot, workshopDebounce);
                }
            }
            // Off the caller's thread: a Workshop folder can hold thousands of files.
            ThreadPool.QueueUserWorkItem(delegate
            {
                string now = Fingerprint(out string error);
                if (error != null) { Fail(error); return; }
                lock (gate) { if (mine == generation) baseline = now; }
            });
        }

        /// <summary>Checks right away, for when the launcher's window comes back to the front: a change made while it was in the background shows up as <see cref="Changed"/>.</summary>
        public void CheckNow()
        {
            ThreadPool.QueueUserWorkItem(delegate { Check(); });
        }

        private void StartWatcher(string root, TimeSpan debounce)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return;
            try
            {
                FileSystemWatcher watcher = new FileSystemWatcher(root);
                watcher.IncludeSubdirectories = true;
                watcher.InternalBufferSize = 64 * 1024;
                watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size;
                FileSystemEventHandler touched = delegate { Schedule(debounce); };
                watcher.Created += touched;
                watcher.Changed += touched;
                watcher.Deleted += touched;
                watcher.Renamed += delegate { Schedule(debounce); };
                // An overflowed buffer lost events, which the fingerprint makes good; anything else is reported.
                watcher.Error += delegate (object sender, ErrorEventArgs e)
                {
                    if (e.GetException() is InternalBufferOverflowException) { Schedule(debounce); return; }
                    Fail("Changes in " + root + " can no longer be watched: " + e.GetException().Message);
                };
                watcher.EnableRaisingEvents = true;
                watchers.Add(watcher);
            }
            catch (Exception ex) when (ex is IOException || ex is ArgumentException || ex is UnauthorizedAccessException || ex is PlatformNotSupportedException)
            {
                Logger.LogException("Watch mod folder: " + root, ex);
                Fail("Changes in " + root + " cannot be watched, so it is only rescanned when this window is brought to the front or Refresh is pressed.\n" + ex.Message);
            }
        }

        private void StopWatchers()
        {
            foreach (FileSystemWatcher watcher in watchers) watcher.Dispose();
            watchers.Clear();
        }

        private void Schedule(TimeSpan debounce)
        {
            lock (gate)
            {
                if (disposed) return;
                timer.Change(debounce, Timeout.InfiniteTimeSpan);
            }
        }

        private void Check()
        {
            int mine;
            lock (gate)
            {
                if (disposed || baseline == null) return;
                mine = generation;
            }
            string now = Fingerprint(out string error);
            if (error != null) { Fail(error); return; }
            lock (gate)
            {
                // A scan that started meanwhile re-baselines on its own.
                if (disposed || mine != generation || now == baseline) return;
                baseline = now;
            }
            Changed?.Invoke();
        }

        private void Fail(string message)
        {
            Logger.Log("ModFolderWatcher", message);
            Failed?.Invoke(message);
        }

        private string Fingerprint(out string error)
        {
            string local, workshop;
            lock (gate) { local = localRoot; workshop = workshopRoot; }
            try
            {
                error = null;
                return ModFolderFingerprint.Compute(new[] { local, workshop });
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Logger.LogException("Fingerprint mod folders", ex);
                error = "The mod folders could not be checked for changes: " + ex.Message;
                return null;
            }
        }

        public void Dispose()
        {
            lock (gate)
            {
                disposed = true;
                StopWatchers();
            }
            timer.Dispose();
        }
    }

    /// <summary>A hash of every file's relative path, size and modified time under each mod folder of the given roots.</summary>
    public static class ModFolderFingerprint
    {
        /// <summary>Folders whose churn never changes how a mod is listed: a mod under development is often a git checkout.</summary>
        private static readonly string[] Ignored = { ".git" };

        /// <exception cref="IOException">A folder cannot be listed (other than by vanishing while it was being read).</exception>
        public static string Compute(IEnumerable<string> roots)
        {
            using (IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                foreach (string root in roots)
                {
                    Append(hash, "root:" + (root ?? ""));
                    if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;
                    try { Walk(hash, new DirectoryInfo(root), ""); }
                    catch (Exception ex) when (ex is DirectoryNotFoundException || ex is FileNotFoundException)
                    {
                        // Something was deleted while this was reading (a Steam update, a build): the folder is changing, so this reading
                        // can never equal a settled one. The debounce checks again once it is quiet.
                        Append(hash, "unsettled:" + Guid.NewGuid());
                    }
                }
                return Convert.ToHexString(hash.GetHashAndReset());
            }
        }

        private static void Walk(IncrementalHash hash, DirectoryInfo dir, string relative)
        {
            FileSystemInfo[] entries = dir.GetFileSystemInfos();
            Array.Sort(entries, (a, b) => string.CompareOrdinal(a.Name, b.Name));
            foreach (FileSystemInfo entry in entries)
            {
                string path = relative + "/" + entry.Name;
                if (entry is DirectoryInfo sub)
                {
                    if (Ignored.Contains(entry.Name, StringComparer.OrdinalIgnoreCase)) continue;
                    Append(hash, "d:" + path);
                    // A link or junction is listed but not followed, so a mod folder that links back into the tree cannot loop.
                    if ((entry.Attributes & FileAttributes.ReparsePoint) == 0) Walk(hash, sub, path);
                }
                // Files straight in a root are not mods (the launcher writes mods.json there), so only the mod folders count.
                else if (entry is FileInfo file && relative.Length > 0)
                {
                    Append(hash, "f:" + path + "|" + file.Length + "|" + file.LastWriteTimeUtc.Ticks);
                }
            }
        }

        private static void Append(IncrementalHash hash, string text)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(text + "\n"));
        }
    }
}
