using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace DW2ModLauncher.Core.Services.Publishing
{
    /// <summary>
    /// The optional changelog.txt in a mod's content root, newest release first:
    ///   ## Unreleased
    ///   - what has changed since the last publish
    ///
    ///   ## 1.36.0 (2026-10-02)
    ///   - ...
    /// A publish sends the Unreleased lines as the Steam change note, then stamps that heading with the published version and date and
    /// puts a fresh, empty Unreleased above it. Nothing is ever deleted.
    /// </summary>
    public static class ModChangelog
    {
        public const string FileName = "changelog.txt";
        public const string UnreleasedHeading = "Unreleased";

        private class Section
        {
            public string Heading;          // text after "## ", null for the preamble before the first heading
            public List<string> Lines = new List<string>();
        }

        public static string PathFor(string contentRoot)
        {
            return Path.Combine(contentRoot, FileName);
        }

        /// <summary>The lines under the Unreleased heading, trimmed; "" when the file, the heading or its lines are missing.</summary>
        public static string UnreleasedText(string contentRoot)
        {
            Section unreleased = Parse(ReadFile(contentRoot)).FirstOrDefault(IsUnreleased);
            return unreleased == null ? "" : Body(unreleased);
        }

        /// <summary>Replaces the lines under the Unreleased heading (creating the file, or the heading at the top, when missing). Blank text empties them.</summary>
        public static void SetUnreleased(string contentRoot, string text)
        {
            List<Section> sections = Parse(ReadFile(contentRoot));
            Section unreleased = sections.FirstOrDefault(IsUnreleased);
            if (unreleased == null)
            {
                unreleased = new Section { Heading = UnreleasedHeading };
                sections.Insert(1, unreleased);
            }
            unreleased.Lines = (text ?? "").Replace("\r\n", "\n").Split('\n').ToList();
            Write(contentRoot, Render(sections));
        }

        /// <summary>
        /// The Steam change note for publishing <paramref name="version"/>: "Version X", then the Unreleased lines. With nothing unreleased,
        /// a republish of a version the changelog already holds sends that section again. Never blank.
        /// </summary>
        public static string ChangeNote(string contentRoot, string version)
        {
            List<Section> sections = Parse(ReadFile(contentRoot));
            string body = sections.Where(IsUnreleased).Select(Body).FirstOrDefault(b => b.Length > 0);
            if (body == null) body = sections.Where(s => HeadingVersion(s) == version).Select(Body).FirstOrDefault() ?? "";
            string head = "Version " + version;
            return body.Length == 0 ? head : head + "\n\n" + body;
        }

        /// <summary>
        /// After a successful publish: Unreleased becomes "## version (date)" and a new empty Unreleased goes above it. When the changelog already
        /// has a section for this version (a republish), the unreleased lines are added to the top of that section instead of a second heading.
        /// Does nothing, and returns false, when there is no file or nothing unreleased.
        /// </summary>
        public static bool Stamp(string contentRoot, string version, DateTime date)
        {
            string text = ReadFile(contentRoot);
            if (text == null) return false;
            List<Section> sections = Parse(text);
            Section unreleased = sections.FirstOrDefault(IsUnreleased);
            if (unreleased == null || Body(unreleased).Length == 0) return false;

            Section existing = sections.FirstOrDefault(s => HeadingVersion(s) == version);
            if (existing != null)
            {
                existing.Lines.InsertRange(0, TrimBlank(unreleased.Lines).Concat(new[] { "" }));
                unreleased.Lines.Clear();
            }
            else
            {
                unreleased.Heading = version + " (" + date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) + ")";
                sections.Insert(sections.IndexOf(unreleased), new Section { Heading = UnreleasedHeading });
            }
            Write(contentRoot, Render(sections));
            return true;
        }

        private static bool IsUnreleased(Section s)
        {
            return s.Heading != null && s.Heading.Trim().Equals(UnreleasedHeading, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The version a heading names ("1.36.0 (2026-10-02)" or "1.36.0"), or null for any other heading.</summary>
        private static string HeadingVersion(Section s)
        {
            if (s.Heading == null || IsUnreleased(s)) return null;
            string first = s.Heading.Trim().Split(new[] { ' ', '\t' }, 2)[0];
            return first.Length == 0 ? null : first;
        }

        private static string Body(Section s)
        {
            return string.Join("\n", TrimBlank(s.Lines));
        }

        private static List<string> TrimBlank(List<string> lines)
        {
            int start = 0, end = lines.Count;
            while (start < end && string.IsNullOrWhiteSpace(lines[start])) start++;
            while (end > start && string.IsNullOrWhiteSpace(lines[end - 1])) end--;
            return lines.GetRange(start, end - start).Select(l => l.TrimEnd()).ToList();
        }

        private static List<Section> Parse(string text)
        {
            List<Section> sections = new List<Section> { new Section() };
            if (string.IsNullOrEmpty(text)) return sections;
            foreach (string line in text.Replace("\r\n", "\n").Split('\n'))
            {
                if (line.StartsWith("## ", StringComparison.Ordinal)) sections.Add(new Section { Heading = line.Substring(3).Trim() });
                else sections[sections.Count - 1].Lines.Add(line);
            }
            return sections;
        }

        private static string Render(List<Section> sections)
        {
            StringBuilder b = new StringBuilder();
            foreach (Section s in sections)
            {
                List<string> lines = TrimBlank(s.Lines);
                if (s.Heading == null)
                {
                    if (lines.Count > 0) b.Append(string.Join("\n", lines)).Append("\n\n");
                    continue;
                }
                b.Append("## ").Append(s.Heading).Append('\n');
                if (lines.Count > 0) b.Append(string.Join("\n", lines)).Append('\n');
                b.Append('\n');
            }
            return b.ToString().TrimEnd('\n') + "\n";
        }

        private static string ReadFile(string contentRoot)
        {
            if (string.IsNullOrWhiteSpace(contentRoot)) return null;
            string path = PathFor(contentRoot);
            return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;
        }

        private static void Write(string contentRoot, string text)
        {
            File.WriteAllText(PathFor(contentRoot), text, new UTF8Encoding(false));
        }
    }
}
