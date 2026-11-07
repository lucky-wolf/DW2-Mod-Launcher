using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using DW2ModLauncher.Core.Services;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class BugReportTests : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "dw2-bugreport-" + Guid.NewGuid().ToString("N"));

        public BugReportTests() { Directory.CreateDirectory(root); }

        public void Dispose() { Directory.Delete(root, true); }

        private string Write(string name, params string[] lines)
        {
            string path = Path.GetFullPath(Path.Combine(root, name));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllLines(path, lines);
            return path;
        }

        [Fact]
        public void LogFolderIsTheSettingElseTheGamesLogs()
        {
            Assert.Equal(@"D:\mine", BugReport.LogFolder(@"D:\mine", @"C:\game"));
            Assert.Equal(Path.Combine(@"C:\game", "data", "Logs"), BugReport.LogFolder("", @"C:\game"));
            Assert.Null(BugReport.LogFolder("", ""));
        }

        [Fact]
        public void SummaryCountsLinesThatDifferOnlyInNumbers()
        {
            string log = Write("mod.log", "[10:00:01] Error: ship 12 has no hangar", "[10:00:02] all fine", "[10:00:09] Error: ship 99 has no hangar", "[10:00:10] Warning: slow");
            string text = BugReport.Summary(new[] { "header line" }, new[] { new BugReport.Source { Path = log, EntryName = "Logs/mod.log" } }, null);
            Assert.StartsWith("header line", text);
            Assert.Contains("(x2)", text);
            Assert.Contains("Warning: slow", text);
            Assert.DoesNotContain("all fine", text);
            Assert.Contains("2 different", text);
        }

        [Fact]
        public void SummaryHidesTheUserProfileFolder()
        {
            string log = Write("mod.log", @"Failed to open C:\Users\Someone\Documents\x.json");
            string text = BugReport.Summary(new[] { @"Game folder: C:\Users\Someone\Steam" }, new[] { new BugReport.Source { Path = log, EntryName = "Logs/mod.log" } }, @"C:\Users\Someone");
            Assert.DoesNotContain("Someone", text);
            Assert.Contains(@"~\Documents\x.json", text);
        }

        [Fact]
        public void SummaryStaysWithinItsLimit()
        {
            string[] lines = Enumerable.Range(0, 400).Select(i => "Error: distinct problem " + new string((char)('a' + i % 26), 1) + new string('x', i % 200) + (char)('A' + i % 26) + "q").ToArray();
            string log = Write("big.log", lines);
            string text = BugReport.Summary(new[] { "h" }, new[] { new BugReport.Source { Path = log, EntryName = "Logs/big.log" } }, null);
            Assert.True(text.Length <= BugReport.SummaryLimit + 200, text.Length.ToString());
            Assert.Contains("more are in the zip", text);
        }

        [Fact]
        public void OldLogsAreLeftOutAndTheNewestSessionFolderIsIn()
        {
            string logs = Path.Combine(root, "Logs");
            string fresh = Write("Logs/fresh.log", "x");
            string old = Write("Logs/old.log", "x");
            File.SetLastWriteTime(old, DateTime.Now.AddDays(-30));
            Write("Logs/2026-11-01_10-00-00/mod.log", "x");
            var sources = BugReport.LogSources(logs, null, DateTime.Now);
            Assert.Contains(sources, s => s.Path == fresh);
            Assert.DoesNotContain(sources, s => s.Path == old);
            Assert.Contains(sources, s => s.EntryName == "Logs/2026-11-01_10-00-00/mod.log");
        }

        [Fact]
        public void ZipHoldsTheReportAndEverySource()
        {
            string log = Write("a.log", "Error: boom");
            string zipPath = Path.Combine(root, "out.zip");
            long size = BugReport.WriteZip(zipPath, "the summary", new[] { new BugReport.Source { Path = log, EntryName = "Logs/a.log" } });
            Assert.Equal(new FileInfo(zipPath).Length, size);
            using (ZipArchive zip = ZipFile.OpenRead(zipPath))
            {
                Assert.Equal(new[] { "Logs/a.log", "report.txt" }, zip.Entries.Select(e => e.FullName).OrderBy(n => n, StringComparer.Ordinal).ToArray());
                using (StreamReader reader = new StreamReader(zip.GetEntry("report.txt").Open())) Assert.Equal("the summary", reader.ReadToEnd());
            }
        }

        [Fact]
        public void ZipReadsAFileAnotherProgramHasOpen()
        {
            string log = Path.Combine(root, "open.log");
            using (FileStream held = new FileStream(log, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
            {
                held.Write(new byte[] { 65, 66, 67 }, 0, 3);
                held.Flush();
                string zipPath = Path.Combine(root, "open.zip");
                BugReport.WriteZip(zipPath, "s", new[] { new BugReport.Source { Path = log, EntryName = "Logs/open.log" } });
                using (ZipArchive zip = ZipFile.OpenRead(zipPath))
                using (StreamReader reader = new StreamReader(zip.GetEntry("Logs/open.log").Open())) Assert.Equal("ABC", reader.ReadToEnd());
            }
        }
    }
}
