using System;
using System.Collections.Generic;
using System.Linq;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>
    /// The launcher's "Game monitor" choice: the drop-down lists the launcher's own "Automatic" (leave the game where it puts itself) and then one entry per
    /// monitor, "monitor name, its size, on video card name" (a second identical one is numbered). The stored value is that text. At launch <see cref="Find"/> turns it
    /// back into a monitor: a choice that no longer matches any monitor is reported, never replaced by another one.
    /// </summary>
    public static class DisplayChoices
    {
        /// <summary>The label of every monitor, in the order <see cref="DisplayEnumerator"/> lists them (the primary one is not moved to the front, so the order stays stable).</summary>
        public static List<string> Labels(IList<DisplayInfo> displays)
        {
            List<string> labels = new List<string>();
            Dictionary<string, int> seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (DisplayInfo display in displays)
            {
                string monitor = string.IsNullOrWhiteSpace(display.MonitorName) ? "Monitor" : display.MonitorName.Trim();
                // the size tells apart monitors a driver names alike ("Generic PnP Monitor")
                if (display.Width > 0 && display.Height > 0) monitor += " " + display.Width + "x" + display.Height;
                string label = string.IsNullOrWhiteSpace(display.AdapterName) ? monitor : monitor + " on " + display.AdapterName.Trim();
                seen.TryGetValue(label, out int count);
                seen[label] = count + 1;
                labels.Add(count == 0 ? label : label + " (" + (count + 1) + ")");
            }
            return labels;
        }

        /// <summary>The schema's own options followed by the monitors on this machine.</summary>
        public static List<string> Options(IEnumerable<string> own, IList<DisplayInfo> displays)
        {
            List<string> options = new List<string>(own ?? Enumerable.Empty<string>());
            options.AddRange(Labels(displays));
            return options;
        }

        /// <summary>
        /// What the loader needs for a stored choice: the monitor's rectangle ("x,y,width,height" in true pixels), its video card and its own refresh rate (never the main monitor's). Blank choice (Automatic): both blank, no problem.
        /// A choice that names no connected monitor gives blank values and the problem text, which the launcher reports; no other monitor is used instead.
        /// </summary>
        public static string Resolve(string choice, IList<DisplayInfo> displays, out string rect, out string adapter, out int refreshRate)
        {
            rect = "";
            adapter = "";
            refreshRate = 0;
            if (string.IsNullOrWhiteSpace(choice)) return null;
            DisplayInfo display = Find(choice, displays);
            if (display == null) return "The monitor \"" + choice.Trim() + "\" chosen for the game is not connected, so the game starts where it normally does.";
            rect = display.X + "," + display.Y + "," + display.Width + "," + display.Height;
            adapter = display.AdapterName ?? "";
            refreshRate = display.RefreshRate;
            return null;
        }

        /// <summary>The monitor a stored choice names, or null when it names none (compared without regard to case).</summary>
        public static DisplayInfo Find(string choice, IList<DisplayInfo> displays)
        {
            if (string.IsNullOrWhiteSpace(choice)) return null;
            List<string> labels = Labels(displays);
            int at = labels.FindIndex(l => string.Equals(l, choice.Trim(), StringComparison.OrdinalIgnoreCase));
            return at < 0 ? null : displays[at];
        }
    }
}
