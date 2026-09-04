using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>
    /// Parses Steam's ACF ("appworkshop_&lt;appid&gt;.acf") manifest format for per-item update timestamps.
    /// </summary>
    public static class AcfManifest
    {
        public static string FindManifestPath(string workshopRoot, string appId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(workshopRoot)) return null;
                DirectoryInfo contentApp = new DirectoryInfo(workshopRoot);
                DirectoryInfo content = contentApp.Parent;
                DirectoryInfo workshop = content == null ? null : content.Parent;
                if (workshop == null) return null;
                string path = Path.Combine(workshop.FullName, "appworkshop_" + appId + ".acf");
                return File.Exists(path) ? path : null;
            }
            catch { return null; }
        }

        public static string ExtractBlock(string text, string sectionName)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(sectionName)) return null;
            Match m = Regex.Match(text, "\\\"" + Regex.Escape(sectionName) + "\\\"\\s*\\{", RegexOptions.IgnoreCase);
            if (!m.Success) return null;
            int open = text.IndexOf('{', m.Index);
            if (open < 0) return null;
            int depth = 0;
            for (int i = open; i < text.Length; i++)
            {
                if (text[i] == '{') depth++;
                else if (text[i] == '}')
                {
                    depth--;
                    if (depth == 0) return text.Substring(open + 1, i - open - 1);
                }
            }
            return null;
        }

        public static Dictionary<string, long> ParseSectionTimes(string text, string sectionName)
        {
            Dictionary<string, long> result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            string block = ExtractBlock(text, sectionName);
            if (string.IsNullOrEmpty(block)) return result;
            MatchCollection entries = Regex.Matches(block, "\\\"(\\d+)\\\"\\s*\\{([^{}]*)\\}", RegexOptions.Singleline);
            foreach (Match entry in entries)
            {
                Match tm = Regex.Match(entry.Groups[2].Value, "\\\"timeupdated\\\"\\s*\\\"(\\d+)\\\"", RegexOptions.IgnoreCase);
                long value;
                if (tm.Success && long.TryParse(tm.Groups[1].Value, out value)) result[entry.Groups[1].Value] = value;
            }
            return result;
        }
    }
}
