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

        /// <summary>
        /// The entries (the "- " lines, without the dash) that the {changes} placeholder lists: the Unreleased ones, which are what a publish
        /// is about to send as the change note; with none, the section of <paramref name="version"/>; with none, the newest released section.
        /// Empty when the changelog holds no entries at all.
        /// </summary>
        public static List<string> LatestChanges(string contentRoot, string version)
        {
            List<Section> sections = Parse(ReadFile(contentRoot));
            List<string> entries = sections.Where(IsUnreleased).Select(s => Entries(Body(s))).FirstOrDefault(e => e.Count > 0);
            if (entries == null) entries = sections.Where(s => HeadingVersion(s) == version).Select(s => Entries(Body(s))).FirstOrDefault(e => e.Count > 0);
            if (entries == null) entries = sections.Where(s => s.Heading != null && !IsUnreleased(s)).Select(s => Entries(Body(s))).FirstOrDefault(e => e.Count > 0);
            return entries ?? new List<string>();
        }

        /// <summary>The "- " lines of some changelog text, each without its dash and trimmed; other lines are ignored.</summary>
        public static List<string> Entries(string body)
        {
            return (body ?? "").Replace("\r\n", "\n").Split('\n').Select(l => l.Trim())
                .Where(l => l.StartsWith("- ", StringComparison.Ordinal) && l.Substring(2).Trim().Length > 0)
                .Select(l => l.Substring(2).Trim()).ToList();
        }

        /// <summary>
        /// The section the change notes of a publish as <paramref name="version"/> live in: Unreleased when it has lines; otherwise the section
        /// of that version when a publish stamped it (it is shown for reading) or when Unreleased has no lines (a changelog whose newest section is the version about to go out has no Unreleased one); otherwise Unreleased
        /// (null when there is not even that). The same choice <see cref="ChangeNote"/> makes.
        /// </summary>
        private static Section NotesTarget(List<Section> sections, string version)
        {
            // A version a publish already stamped shows (and edits) its own published notes, so choosing it by hand is a way to read them.
            Section stamped = string.IsNullOrWhiteSpace(version) ? null : sections.FirstOrDefault(s => IsStampedFor(s, version));
            if (stamped != null) return stamped;
            Section unreleased = sections.FirstOrDefault(IsUnreleased);
            if (unreleased != null && Body(unreleased).Length > 0) return unreleased;
            Section ofVersion = string.IsNullOrWhiteSpace(version) ? null : sections.FirstOrDefault(s => HeadingVersion(s) == version.Trim());
            return ofVersion ?? unreleased;
        }

        /// <summary>The lines of the section a publish as <paramref name="version"/> sends as its change note (see <see cref="NotesTarget"/>); "" when there are none.</summary>
        public static string NotesText(string contentRoot, string version)
        {
            Section target = NotesTarget(Parse(ReadFile(contentRoot)), version);
            return target == null ? "" : Body(target);
        }

        /// <summary>Replaces the lines of that same section (creating an Unreleased one at the top when there is no section to take them). Blank text empties them.</summary>
        public static void SetNotes(string contentRoot, string version, string text)
        {
            List<Section> sections = Parse(ReadFile(contentRoot));
            Section target = NotesTarget(sections, version);
            if (target == null)
            {
                target = new Section { Heading = UnreleasedHeading };
                sections.Insert(1, target);
            }
            target.Lines = (text ?? "").Replace("\r\n", "\n").Split('\n').ToList();
            Write(contentRoot, Render(sections));
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

        /// <summary>
        /// The changelog text with line endings as LF and any "## Unreleased" heading that has no lines under it removed, so the empty heading a
        /// publish leaves behind (or its absence) does not make two otherwise identical changelogs differ. Everything else is untouched.
        /// </summary>
        public static string WithoutEmptyUnreleased(string text)
        {
            List<string> lines = (text ?? "").Replace("\r\n", "\n").Split('\n').ToList();
            for (int i = 0; i < lines.Count; i++)
            {
                if (!lines[i].StartsWith("## ", StringComparison.Ordinal) || !lines[i].Substring(3).Trim().Equals(UnreleasedHeading, StringComparison.OrdinalIgnoreCase)) continue;
                int end = i + 1;
                while (end < lines.Count && !lines[end].StartsWith("## ", StringComparison.Ordinal) && string.IsNullOrWhiteSpace(lines[end])) end++;
                if (end < lines.Count && !lines[end].StartsWith("## ", StringComparison.Ordinal)) continue;
                lines.RemoveRange(i, end - i);
                i--;
            }
            return string.Join("\n", lines);
        }

        /// <summary>A heading with a date ("1.0.12 (2026-10-22)") is what a publish stamps: that version went out.</summary>
        private static bool IsStampedFor(Section s, string version)
        {
            return HeadingVersion(s) == version.Trim() && s.Heading.Contains("(");
        }

        /// <summary>Whether a publish already stamped a section for <paramref name="version"/>, meaning that version is out and the next change is a new one.</summary>
        public static bool IsStamped(string contentRoot, string version)
        {
            return !string.IsNullOrWhiteSpace(version) && Parse(ReadFile(contentRoot)).Any(s => IsStampedFor(s, version));
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
