using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>
    /// What a player sends when something is wrong: a short text summary (versions, mods, the most telling log lines) that fits in a chat message,
    /// and a zip with the recent logs behind it. Both are built from the same files, so the summary is always a true excerpt of the zip.
    /// </summary>
    public static class BugReport
    {
        /// <summary>Log files older than this are left out (a crash dump from a month ago is not about today's problem).</summary>
        public static readonly TimeSpan RecentLogs = TimeSpan.FromDays(7);

        /// <summary>Discord's upload limit for people without a subscription; a bigger zip is reported rather than trimmed.</summary>
        public const long DiscordLimitBytes = 10L * 1000 * 1000;

        /// <summary>The Distant Worlds 2 Discord invite (the #mods channel is where bug reports go).</summary>
        public const string DiscordInvite = "https://discord.gg/9e94cFruHs";

        /// <summary>Longest summary in characters. A Steam discussion post takes about 8,000.</summary>
        public const int SummaryLimit = 7000;

        private const int LineLimit = 220;
        private const int FindingLimit = 30;

        private static readonly Regex Salient = new Regex(@"\b(error|exception|failed|failure|fatal|crash\w*|warn(ing)?)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex Digits = new Regex(@"\d+", RegexOptions.Compiled);

        /// <summary>The folder the game and the loader write logs to: the launcher's log folder setting, else the game's data/Logs. Null when neither is known.</summary>
        public static string LogFolder(string configuredLogFolder, string gameRoot)
        {
            if (!string.IsNullOrWhiteSpace(configuredLogFolder)) return configuredLogFolder;
            return string.IsNullOrWhiteSpace(gameRoot) ? null : Path.Combine(gameRoot, "data", "Logs");
        }

        /// <summary>A log file to read or zip: where it is, and the name it gets in the zip.</summary>
        public sealed class Source
        {
            public string Path { get; set; }
            public string EntryName { get; set; }
        }

        /// <summary>
        /// The log files that belong in a report: every file directly in the log folder modified within <see cref="RecentLogs"/>, the files of its newest
        /// session subfolder (mod authors' sessions), and the launcher's own log when it exists. The log folder is read, never written.
        /// </summary>
        public static List<Source> LogSources(string logFolder, string launcherLog, DateTime now)
        {
            List<Source> sources = new List<Source>();
            if (!string.IsNullOrEmpty(logFolder) && Directory.Exists(logFolder))
            {
                foreach (string file in Directory.GetFiles(logFolder).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                    if (now - File.GetLastWriteTime(file) <= RecentLogs) sources.Add(new Source { Path = file, EntryName = "Logs/" + System.IO.Path.GetFileName(file) });
                string newest = Directory.GetDirectories(logFolder).OrderByDescending(d => Directory.GetLastWriteTime(d)).FirstOrDefault();
                if (newest != null && now - Directory.GetLastWriteTime(newest) <= RecentLogs)
                    foreach (string file in Directory.GetFiles(newest).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                        sources.Add(new Source { Path = file, EntryName = "Logs/" + System.IO.Path.GetFileName(newest) + "/" + System.IO.Path.GetFileName(file) });
            }
            if (!string.IsNullOrEmpty(launcherLog) && File.Exists(launcherLog)) sources.Add(new Source { Path = launcherLog, EntryName = "Launcher/" + System.IO.Path.GetFileName(launcherLog) });
            return sources;
        }

        /// <summary>
        /// The summary text: header lines (<paramref name="header"/>, one per line), then the distinct warning and error lines from <paramref name="sources"/>
        /// with how often each occurred. Lines that differ only in their numbers count as one. Your user name never appears: the user profile folder is
        /// shown as ~.
        /// </summary>
        public static string Summary(IEnumerable<string> header, IEnumerable<Source> sources, string userProfile)
        {
            StringBuilder text = new StringBuilder();
            foreach (string line in header) text.AppendLine(Scrub(line, userProfile));

            List<Finding> findings = new List<Finding>();
            Dictionary<string, Finding> byKey = new Dictionary<string, Finding>();
            foreach (Source source in sources)
            {
                foreach (string raw in ReadLinesShared(source.Path))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || !Salient.IsMatch(line)) continue;
                    string key = source.EntryName.Split('/').Last() + "|" + Digits.Replace(line, "#");
                    if (byKey.TryGetValue(key, out Finding existing)) { existing.Count++; continue; }
                    Finding finding = new Finding { File = source.EntryName.Split('/').Last(), Line = line, Count = 1 };
                    byKey[key] = finding;
                    findings.Add(finding);
                }
            }

            text.AppendLine();
            if (findings.Count == 0)
            {
                text.AppendLine("No warnings or errors in the recent logs.");
                return text.ToString();
            }
            text.AppendLine("Warnings and errors in the recent logs (" + findings.Count + " different):");
            int shown = 0;
            foreach (Finding f in findings.OrderByDescending(f => f.Count).Take(FindingLimit))
            {
                string line = Scrub(f.Line, userProfile);
                if (line.Length > LineLimit) line = line.Substring(0, LineLimit) + "...";
                string entry = f.File + ": " + line + (f.Count > 1 ? "  (x" + f.Count + ")" : "");
                if (text.Length + entry.Length + 2 > SummaryLimit) break;
                text.AppendLine(entry);
                shown++;
            }
            if (shown < findings.Count) text.AppendLine("(" + (findings.Count - shown) + " more are in the zip)");
            return text.ToString();
        }

        /// <summary>
        /// Writes the report zip to <paramref name="zipPath"/>: <paramref name="summaryText"/> as report.txt, then every source (the logs, and
        /// the mod list so the load order is known). Files the game still has open are read shared. Returns the zip's size in bytes.
        /// </summary>
        public static long WriteZip(string zipPath, string summaryText, IEnumerable<Source> sources)
        {
            using (FileStream stream = new FileStream(zipPath, FileMode.Create, FileAccess.Write))
            using (ZipArchive zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                ZipArchiveEntry report = zip.CreateEntry("report.txt", CompressionLevel.Optimal);
                using (StreamWriter writer = new StreamWriter(report.Open(), new UTF8Encoding(false))) writer.Write(summaryText);
                foreach (Source source in sources)
                {
                    ZipArchiveEntry entry = zip.CreateEntry(source.EntryName, CompressionLevel.Optimal);
                    using (Stream target = entry.Open())
                    using (FileStream file = new FileStream(source.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                        file.CopyTo(target);
                }
            }
            return new FileInfo(zipPath).Length;
        }

        /// <summary>"DW2-bug-report-2026-11-06_17-51-10.zip".</summary>
        public static string ZipName(DateTime now) { return "DW2-bug-report-" + now.ToString("yyyy-MM-dd_HH-mm-ss") + ".zip"; }

        private static string Scrub(string line, string userProfile)
        {
            if (string.IsNullOrEmpty(userProfile)) return line;
            return line.Replace(userProfile, "~", StringComparison.OrdinalIgnoreCase).Replace(userProfile.Replace('\\', '/'), "~", StringComparison.OrdinalIgnoreCase);
        }

        // the game and the loader keep their logs open while they run, so read without asking for exclusive access
        private static IEnumerable<string> ReadLinesShared(string path)
        {
            using (FileStream file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (StreamReader reader = new StreamReader(file, Encoding.UTF8))
            {
                string line;
                while ((line = reader.ReadLine()) != null) yield return line;
            }
        }

        private sealed class Finding
        {
            public string File;
            public string Line;
            public int Count;
        }
    }
}
