using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using DW2ModLauncher.Core.Services;
using Xunit;

namespace DW2ModLauncher.Tests
{
    /// <summary>
    /// The rules for language-pack text (docs/Localization.md): every text is a whole sentence, anything that varies is a named
    /// placeholder, and every pack uses the same placeholders as English.
    /// </summary>
    public class LanguagePackTests
    {
        private static string SrcRoot()
        {
            string dir = AppContext.BaseDirectory;
            while (dir != null && !Directory.Exists(Path.Combine(dir, "src", "DW2ModLauncher.Core"))) dir = Path.GetDirectoryName(dir);
            Assert.NotNull(dir);
            return Path.Combine(dir, "src");
        }

        private static Dictionary<string, string> Pack(string code)
        {
            string path = Path.Combine(SrcRoot(), "DW2ModLauncher.Avalonia", "Languages", code + ".json");
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
        }

        private static IEnumerable<string> Codes() => Directory.GetFiles(Path.Combine(SrcRoot(), "DW2ModLauncher.Avalonia", "Languages"), "*.json")
            .Select(f => Path.GetFileNameWithoutExtension(f));

        [Fact]
        public void NoTextUsesAPositionalPlaceholder()
        {
            foreach (string code in Codes())
                foreach (KeyValuePair<string, string> kv in Pack(code))
                    Assert.False(Regex.IsMatch(kv.Value, @"\{\d+[:}]"), code + "/" + kv.Key + " uses {0}-style placeholders; name them ({Profile})");
        }

        [Fact]
        public void EveryPackUsesTheSamePlaceholdersAsEnglish()
        {
            Dictionary<string, string> en = Pack("en");
            foreach (string code in Codes().Where(c => c != "en"))
                foreach (KeyValuePair<string, string> kv in Pack(code))
                {
                    if (kv.Key.StartsWith("_", StringComparison.Ordinal)) continue;
                    Assert.True(en.ContainsKey(kv.Key), code + " has the key " + kv.Key + ", which English does not");
                    Assert.Equal(NamedText.Names(en[kv.Key]).OrderBy(x => x), NamedText.Names(kv.Value).OrderBy(x => x));
                }
        }

        [Fact]
        public void NoTextEndsWhereTheCodeAppendsTheRest()
        {
            // "Delete this profile?\r\n\r\n" + name: the sentence must hold the name itself, so a language can put it anywhere
            foreach (KeyValuePair<string, string> kv in Pack("en"))
            {
                if (kv.Key.StartsWith("_", StringComparison.Ordinal) || kv.Key.EndsWith("ListSeparator", StringComparison.Ordinal)) continue;
                Assert.False(kv.Value.EndsWith(" ", StringComparison.Ordinal) || kv.Value.EndsWith("\n", StringComparison.Ordinal), kv.Key + " ends with a space or a line break");
                Assert.False(kv.Value.StartsWith(" ", StringComparison.Ordinal) && !kv.Key.StartsWith("WidgetMore", StringComparison.Ordinal), kv.Key + " starts with a space");
            }
        }

        [Fact]
        public void NoCodeAppendsToATranslatedText()
        {
            Regex appended = new Regex(@"\b(T|t)\(""[A-Za-z0-9]+""\)\s*\+|string\.Format\(\s*(main\.T|T|t|L\[|l\[)");
            List<string> found = new List<string>();
            foreach (string file in Directory.GetFiles(SrcRoot(), "*.cs", SearchOption.AllDirectories))
            {
                string normal = file.Replace('\\', '/');
                if (normal.Contains("/obj/") || normal.Contains("/bin/") || normal.Contains("/DW2ModLauncher.Tests/")) continue;
                string[] lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                    if (appended.IsMatch(lines[i])) found.Add(Path.GetFileName(file) + ":" + (i + 1) + "  " + lines[i].Trim());
            }
            Assert.True(found.Count == 0, "Put the value into the text as a named placeholder instead of appending it:\n" + string.Join("\n", found));
        }

        [Fact]
        public void EveryKeyTheCodeAndWindowsAskForExistsInEnglish()
        {
            Dictionary<string, string> en = Pack("en");
            Regex inCode = new Regex(@"\b(?:T|t|Format|F|Get)\(\s*(?:[a-z]+,\s*)?""([A-Z][A-Za-z0-9]+)""");
            Regex inWindows = new Regex(@"L\[([A-Za-z0-9]+)\]");
            List<string> missing = new List<string>();
            foreach (string file in Directory.GetFiles(SrcRoot(), "*.*", SearchOption.AllDirectories).Where(f => f.EndsWith(".cs") || f.EndsWith(".axaml")))
            {
                string normal = file.Replace('\\', '/');
                if (normal.Contains("/obj/") || normal.Contains("/bin/") || normal.Contains("/DW2ModLauncher.Tests/")) continue;
                string text = File.ReadAllText(file);
                IEnumerable<Match> matches = inWindows.Matches(text).Cast<Match>();
                if (file.EndsWith(".cs")) matches = matches.Concat(inCode.Matches(text).Cast<Match>());
                foreach (Match m in matches)
                {
                    string key = m.Groups[1].Value;
                    // "Key" is the parameter name in comments and declarations; a key that only starts a name ("PathProblem" + the problem) is built at run time
                    if (key == "Key" || key == "key" || en.Keys.Any(k => k.StartsWith(key, StringComparison.Ordinal))) continue;
                    missing.Add(Path.GetFileName(file) + ": " + key);
                }
            }
            Assert.True(missing.Count == 0, "Not in en.json:\n" + string.Join("\n", missing.Distinct()));
        }
    }
}
