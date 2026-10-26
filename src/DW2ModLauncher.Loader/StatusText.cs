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
            if (mods.Count == 0) return StatusStrings.Get("WidgetNoneLoaded");

            var bad = new List<ModStatusSnapshot>();
            foreach (ModStatusSnapshot m in mods)
            {
                if (m.Level >= StatusLevel.Warn) bad.Add(m);
            }

            if (bad.Count > 0)
            {
                bool anyError = Worst(bad) == StatusLevel.Error;
                string key = bad.Count == 1 && mods.Count == 1
                    ? (anyError ? "WidgetSingleError" : "WidgetSingleWarning")
                    : bad.Count == 1
                        ? (anyError ? "WidgetOneModProblem" : "WidgetOneModWarning")
                        : "WidgetManyModsProblems";
                string mod = bad.Count == 1 && mods.Count == 1 ? Heading(bad[0]) : bad[0].Name;
                int others = Count(bad[0]) - 1;
                string more = others > 0 ? StatusStrings.Get("WidgetMoreProblems", ("Count", others)) : bad.Count > 1 ? StatusStrings.Get("WidgetMoreMods", ("Count", bad.Count - 1)) : "";
                return StatusStrings.Get(key, ("Count", bad.Count), ("Mod", mod), ("Problem", Trim(Problem(bad[0]), 110)), ("More", more));
            }

            // nothing is claimed beyond what the loader knows: the mods loaded. Anything more is in the panel, if the mod reports it.
            return StatusStrings.Get("WidgetLoadedCount", ("Count", mods.Count));
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

        // The mod's one row in the list the flyout opens from: the heading and its state, nothing more.
        public static string Entry(ModStatusSnapshot m) => StatusStrings.Get(StateKey(m), ("Heading", Heading(m)));

        // Mods in list order: the ones to fix first, then registration order.
        public static List<ModStatusSnapshot> Ordered(IEnumerable<ModStatusSnapshot> mods)
            => mods.OrderByDescending(m => m.Level).ToList();

        public static string Block(ModStatusSnapshot m)
        {
            var sb = new StringBuilder();
            sb.Append(StatusStrings.Get(StateKey(m), ("Heading", Heading(m))));
            if (m.Summary != null) sb.Append("\n  ").Append(m.Summary);
            if (m.LoadError != null) sb.Append("\n  ! ").Append(StatusStrings.Get("WidgetFailedToLoad", ("Problem", m.LoadError)));
            foreach (string e in m.Errors) sb.Append("\n  ! ").Append(e);
            foreach (StatusDetail d in m.Details)
            {
                sb.Append("\n  ").Append(d.Level >= StatusLevel.Warn ? "! " : "").Append(d.Key).Append(": ").Append(d.Text);
            }
            if (m.Features.Count > 0) sb.Append("\n  ").Append(StatusStrings.Get("WidgetFeatures", ("Features", string.Join(", ", m.Features))));
            if (m.LogPath != null) sb.Append("\n  ").Append(StatusStrings.Get("WidgetLog", ("File", m.LogPath)));
            return sb.ToString();
        }

        public static string Heading(ModStatusSnapshot m)
        {
            bool version = !string.IsNullOrWhiteSpace(m.Version);
            bool built = !string.IsNullOrWhiteSpace(m.Built);
            string key = version ? (built ? "WidgetHeadingVersionBuilt" : "WidgetHeadingVersion") : (built ? "WidgetHeadingBuilt" : "WidgetHeadingPlain");
            return StatusStrings.Get(key, ("Mod", m.Name), ("Version", m.Version), ("Built", m.Built));
        }

        // the key of the block's first line, which gives the heading and the state in one text
        static string StateKey(ModStatusSnapshot m)
        {
            if (m.LoadError != null) return "WidgetBlockFailed";
            if (m.Errors.Count > 0) return "WidgetBlockError";
            if (m.Level == StatusLevel.Warn) return "WidgetBlockWarning";
            return "WidgetBlockLoaded";
        }

        // the most important single message for a mod that is not ok
        static string Problem(ModStatusSnapshot m)
        {
            if (m.LoadError != null) return StatusStrings.Get("WidgetFailedToLoad", ("Problem", m.LoadError));
            if (m.Errors.Count > 0) return m.Errors[0];
            foreach (StatusDetail d in m.Details)
            {
                if (d.Level >= StatusLevel.Warn) return d.Key + ": " + d.Text;
            }
            return m.Summary ?? StatusStrings.Get("WidgetSeeDetails");
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
        // The mod a list row stands for; null for the header and the action rows.
        public string Id { get; set; }
    }
}
