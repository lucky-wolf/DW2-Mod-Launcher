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
        /// <summary>What the in-game widget shows for this entry: nothing for a change that applied (and for an unapplied item at missing=ok), yellow for a warning, red for an error.</summary>
        public MissingLevel Level { get; set; }
        /// <summary>The item was not applied because its target (entity, list item or data file) is missing; <see cref="Level"/> then comes from the missing setting.</summary>
        public bool Unapplied { get; set; }

        public override string ToString()
        {
            string kind = Unapplied ? " (not applied, missing=" + MissingPolicy.Name(Level) + ")" : string.Empty;
            return (Line > 0 ? File + ":" + Line : File) + ": " + Severity.ToString().ToLowerInvariant() + kind + ": " + Message;
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

        /// <summary>How many entries show on the widget at <paramref name="level"/> (Warn = yellow, Error = red).</summary>
        public int CountAt(MissingLevel level)
        {
            return level == MissingLevel.Ok ? 0 : Entries.Count(e => e.Level == level);
        }

        /// <summary>The developer override: every entry that would show on the widget is raised to at least this level. Null = off.</summary>
        public MissingLevel? Floor { get; set; }

        /// <summary>
        /// A change that applied (Info), or a problem of the patch itself (Warning/Error: bad XML, unknown field, ...). Problems show
        /// amber on the widget, as they always have, unless the developer override asks for more.
        /// </summary>
        public void Add(Severity severity, string file, int line, string message)
        {
            MissingLevel level = severity == Severity.Info ? MissingLevel.Ok : MissingPolicy.Escalate(MissingLevel.Warn, Floor);
            Entries.Add(new PatchDiagnostic { Severity = severity, File = file, Line = line, Message = message, Level = level });
        }

        /// <summary>
        /// An item that could not be applied because its target is missing. <paramref name="declared"/> is the level the patch, its mod
        /// or the default asks for; the developer override can raise it. The entry is always recorded (and so logged).
        /// </summary>
        public void AddUnapplied(MissingLevel declared, string file, int line, string message)
        {
            MissingLevel level = MissingPolicy.Escalate(declared, Floor);
            Severity severity = level == MissingLevel.Error ? Severity.Error : level == MissingLevel.Warn ? Severity.Warning : Severity.Info;
            Entries.Add(new PatchDiagnostic { Severity = severity, File = file, Line = line, Message = message, Level = level, Unapplied = true });
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
