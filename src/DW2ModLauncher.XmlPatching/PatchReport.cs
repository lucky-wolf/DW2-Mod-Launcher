using System.Collections.Generic;
using System.Linq;

namespace DW2ModLauncher.XmlPatching
{
    public enum Severity
    {
        /// <summary>A change that was applied (old -> new).</summary>
        Info,
        Warning,
        /// <summary>A patch item that was rejected or could not be applied; the rest of the patch still applies.</summary>
        Error
    }

    public sealed class PatchDiagnostic
    {
        public Severity Severity { get; set; }
        public string File { get; set; }
        public int Line { get; set; }
        public string Message { get; set; }

        public override string ToString()
        {
            return (Line > 0 ? File + ":" + Line : File) + ": " + Severity.ToString().ToLowerInvariant() + ": " + Message;
        }
    }

    /// <summary>Everything the patcher did or refused to do, in order, plus a per-patch-file tally.</summary>
    public sealed class PatchReport
    {
        private readonly Dictionary<string, int[]> _tally = new Dictionary<string, int[]>(); // applied, unchanged, skipped

        public List<PatchDiagnostic> Entries { get; } = new List<PatchDiagnostic>();

        public bool HasErrors
        {
            get { return Entries.Any(e => e.Severity == Severity.Error); }
        }

        public void Add(Severity severity, string file, int line, string message)
        {
            Entries.Add(new PatchDiagnostic { Severity = severity, File = file, Line = line, Message = message });
        }

        public void ResetTallies()
        {
            _tally.Clear();
        }

        public void CountApplied(string file)
        {
            Tally(file)[0]++;
        }

        public void CountUnchanged(string file)
        {
            Tally(file)[1]++;
        }

        public void CountSkipped(string file)
        {
            Tally(file)[2]++;
        }

        public int Applied(string file)
        {
            return Tally(file)[0];
        }

        public int Unchanged(string file)
        {
            return Tally(file)[1];
        }

        public int Skipped(string file)
        {
            return Tally(file)[2];
        }

        /// <summary>One line per patch file: "patches/rail.xml: 6 applied, 1 unchanged, 2 skipped".</summary>
        public IEnumerable<string> SummaryLines()
        {
            foreach (KeyValuePair<string, int[]> kv in _tally)
                yield return kv.Key + ": " + kv.Value[0] + " applied, " + kv.Value[1] + " unchanged, " + kv.Value[2] + " skipped";
        }

        private int[] Tally(string file)
        {
            if (!_tally.TryGetValue(file, out int[] t))
            {
                t = new int[3];
                _tally[file] = t;
            }
            return t;
        }
    }
}
