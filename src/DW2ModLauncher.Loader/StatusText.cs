using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DW2ModLauncher.Loader
{
    // The words the in-game widget shows, built from registry snapshots. Pure functions, so the wording is unit tested and the
    // widget only has to draw it.
    public static class StatusText
    {
        public static StatusLevel Worst(IEnumerable<ModStatusSnapshot> mods)
        {
            StatusLevel worst = StatusLevel.Ok;
            foreach (ModStatusSnapshot m in mods)
            {
                if (m.Level > worst) worst = m.Level;
            }
            return worst;
        }

        // The one line shown while collapsed.
        //   no problems:  DW2 mods: 4 loaded
        //   a problem:    2 mods with problems: DW2FreighterLogistics: FuelFirst failed to install (+1 more)
        public static string Line(IList<ModStatusSnapshot> mods)
        {
            if (mods.Count == 0) return "DW2 mods: none loaded";

            var bad = new List<ModStatusSnapshot>();
            foreach (ModStatusSnapshot m in mods)
            {
                if (m.Level >= StatusLevel.Warn) bad.Add(m);
            }

            if (bad.Count > 0)
            {
                bool anyError = Worst(bad) == StatusLevel.Error;
                string head = bad.Count == 1 && mods.Count == 1
                    ? Heading(bad[0]) + (anyError ? " ERROR: " : " warning: ")
                    : bad.Count == 1
                        ? "1 mod with " + (anyError ? "a problem: " : "a warning: ") + bad[0].Name + ": "
                        : bad.Count + " mods with problems: " + bad[0].Name + ": ";
                string first = Problem(bad[0]);
                int others = Count(bad[0]) - 1;
                string more = others > 0 ? " (+" + others + " more)" : bad.Count > 1 ? " (+" + (bad.Count - 1) + " more mods)" : "";
                return head + Trim(first, 110) + more;
            }

            // nothing is claimed beyond what the loader knows: the mods loaded. Anything more is in the panel, if the mod reports it.
            return "DW2 mods: " + mods.Count + " loaded";
        }

        // The expanded panel: one block per mod. Errors first, then everything else in registration order, so the thing to fix is at
        // the top.
        public static List<PanelRow> Rows(IList<ModStatusSnapshot> mods)
        {
            var rows = new List<PanelRow>();
            foreach (ModStatusSnapshot m in mods)
            {
                rows.Add(new PanelRow { Level = m.Level, Text = Block(m) });
            }
            // OrderBy is stable, List.Sort is not
            return rows.OrderByDescending(r => r.Level).ToList();
        }

        public static string Block(ModStatusSnapshot m)
        {
            var sb = new StringBuilder();
            sb.Append(Heading(m)).Append("  ").Append(State(m));
            if (m.Summary != null) sb.Append("\n  ").Append(m.Summary);
            if (m.LoadError != null) sb.Append("\n  ! failed to load: ").Append(m.LoadError);
            foreach (string e in m.Errors) sb.Append("\n  ! ").Append(e);
            foreach (StatusDetail d in m.Details)
            {
                sb.Append("\n  ").Append(d.Level >= StatusLevel.Warn ? "! " : "").Append(d.Key).Append(": ").Append(d.Text);
            }
            if (m.Features.Count > 0) sb.Append("\n  features: ").Append(string.Join(", ", m.Features));
            if (m.LogPath != null) sb.Append("\n  log: ").Append(m.LogPath);
            return sb.ToString();
        }

        public static string Heading(ModStatusSnapshot m)
        {
            string text = m.Name;
            if (!string.IsNullOrWhiteSpace(m.Version)) text += " " + m.Version;
            if (!string.IsNullOrWhiteSpace(m.Built)) text += " (built " + m.Built + ")";
            return text;
        }

        public static string State(ModStatusSnapshot m)
        {
            if (m.LoadError != null) return "FAILED TO LOAD";
            if (m.Errors.Count > 0) return "ERROR";
            if (m.Level == StatusLevel.Warn) return "warning";
            return "loaded";
        }

        // the most important single message for a mod that is not ok
        static string Problem(ModStatusSnapshot m)
        {
            if (m.LoadError != null) return "failed to load: " + m.LoadError;
            if (m.Errors.Count > 0) return m.Errors[0];
            foreach (StatusDetail d in m.Details)
            {
                if (d.Level >= StatusLevel.Warn) return d.Key + ": " + d.Text;
            }
            return m.Summary ?? "see details";
        }

        // how many separate problems a mod has
        static int Count(ModStatusSnapshot m)
        {
            int n = (m.LoadError != null ? 1 : 0) + m.Errors.Count;
            foreach (StatusDetail d in m.Details)
            {
                if (d.Level >= StatusLevel.Warn) n++;
            }
            return n == 0 ? 1 : n;
        }

        static string Trim(string text, int max) => text.Length > max ? text.Substring(0, max) + "..." : text;
    }

    public sealed class PanelRow
    {
        public StatusLevel Level { get; set; }
        public string Text { get; set; }
    }
}
