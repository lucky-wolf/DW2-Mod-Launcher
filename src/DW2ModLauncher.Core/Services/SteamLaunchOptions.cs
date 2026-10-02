using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>
    /// The game's launch options as set in the Steam client (Properties > General > Launch Options),
    /// read from Steam's userdata/&lt;steamid&gt;/config/localconfig.vdf. Steam's own form is
    /// "[ENV=value ...] [wrapper ...] %command% [arguments ...]"; this splits it into those parts.
    /// </summary>
    public sealed class SteamLaunchOptions
    {
        private const string CommandToken = "%command%";

        public string Raw { get; private set; } = "";
        /// <summary>KEY=value tokens before %command%.</summary>
        public Dictionary<string, string> Environment { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
        /// <summary>Anything else before %command% (gamescope, mangohud, ...). Not applied by the launcher.</summary>
        public string Wrapper { get; private set; } = "";
        /// <summary>Everything after %command% (or the whole string when there is no %command%), quoting preserved.</summary>
        public string Arguments { get; private set; } = "";

        public static SteamLaunchOptions Parse(string raw)
        {
            SteamLaunchOptions result = new SteamLaunchOptions { Raw = raw ?? "" };
            string text = result.Raw;
            int at = text.IndexOf(CommandToken, StringComparison.Ordinal);
            if (at < 0) { result.Arguments = text.Trim(); return result; }

            result.Arguments = text.Substring(at + CommandToken.Length).Trim();
            List<string> wrapper = new List<string>();
            foreach (string token in Tokenize(text.Substring(0, at)))
            {
                Match m = Regex.Match(token, "^([A-Za-z_][A-Za-z0-9_]*)=(.*)$", RegexOptions.Singleline);
                if (m.Success && wrapper.Count == 0) result.Environment[m.Groups[1].Value] = m.Groups[2].Value;
                else wrapper.Add(token);
            }
            result.Wrapper = string.Join(" ", wrapper);
            return result;
        }

        /// <summary>The launch options for DW2 from the most recently used Steam account, or null when Steam has none set.</summary>
        public static SteamLaunchOptions ReadForApp(string appId)
        {
            List<string> files = new List<string>();
            foreach (string root in SteamLocator.FindSteamRoots())
            {
                try
                {
                    string userdata = Path.Combine(root, "userdata");
                    if (!Directory.Exists(userdata)) continue;
                    foreach (string user in Directory.GetDirectories(userdata))
                    {
                        string f = Path.Combine(user, "config", "localconfig.vdf");
                        if (File.Exists(f)) files.Add(f);
                    }
                }
                catch { }
            }
            foreach (string f in files.OrderByDescending(f => File.GetLastWriteTimeUtc(f)))
            {
                try
                {
                    string value = ExtractLaunchOptions(File.ReadAllText(f), appId);
                    if (!string.IsNullOrWhiteSpace(value)) return Parse(value);
                }
                catch { }
            }
            return null;
        }

        /// <summary>UserLocalConfigStore/Software/Valve/Steam/apps/&lt;appId&gt;/LaunchOptions, or null.</summary>
        internal static string ExtractLaunchOptions(string vdfText, string appId)
        {
            int pos = 0;
            Dictionary<string, object> root = ParseBlock(vdfText ?? "", ref pos, true);
            object node = root;
            foreach (string key in new[] { "UserLocalConfigStore", "Software", "Valve", "Steam", "apps", appId })
            {
                if (node is not Dictionary<string, object> dict || !dict.TryGetValue(key, out node)) return null;
            }
            return node is Dictionary<string, object> app && app.TryGetValue("LaunchOptions", out object v) ? v as string : null;
        }

        // Minimal Valve KeyValues reader: quoted strings, { } blocks, case-insensitive keys, later duplicates win.
        private static Dictionary<string, object> ParseBlock(string s, ref int i, bool top)
        {
            Dictionary<string, object> dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            while (true)
            {
                string key = NextString(s, ref i, out bool closed);
                if (closed) return dict;
                if (key == null) return dict;
                SkipSpace(s, ref i);
                if (i < s.Length && s[i] == '{') { i++; dict[key] = ParseBlock(s, ref i, false); }
                else
                {
                    string value = NextString(s, ref i, out bool closedAfterKey);
                    if (value != null) dict[key] = value;
                    if (closedAfterKey) return dict;
                }
            }
        }

        private static void SkipSpace(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        // Next quoted string; closed is true (and null returned) when a } ends the block, null at end of input.
        private static string NextString(string s, ref int i, out bool closed)
        {
            closed = false;
            while (true)
            {
                SkipSpace(s, ref i);
                if (i >= s.Length) return null;
                if (s[i] == '}') { i++; closed = true; return null; }
                if (s[i] == '/' && i + 1 < s.Length && s[i + 1] == '/') { while (i < s.Length && s[i] != '\n') i++; continue; }
                if (s[i] != '"') { i++; continue; }
                i++;
                StringBuilder sb = new StringBuilder();
                while (i < s.Length && s[i] != '"')
                {
                    if (s[i] == '\\' && i + 1 < s.Length)
                    {
                        i++;
                        sb.Append(s[i] == 'n' ? '\n' : s[i] == 't' ? '\t' : s[i]);
                    }
                    else sb.Append(s[i]);
                    i++;
                }
                i++;
                return sb.ToString();
            }
        }

        // Whitespace-separated tokens, honouring "double" and 'single' quotes (quotes are removed).
        private static List<string> Tokenize(string s)
        {
            List<string> tokens = new List<string>();
            StringBuilder cur = new StringBuilder();
            char quote = '\0';
            bool any = false;
            foreach (char c in s)
            {
                if (quote != '\0') { if (c == quote) quote = '\0'; else cur.Append(c); }
                else if (c == '"' || c == '\'') { quote = c; any = true; }
                else if (char.IsWhiteSpace(c)) { if (any || cur.Length > 0) tokens.Add(cur.ToString()); cur.Clear(); any = false; }
                else cur.Append(c);
            }
            if (any || cur.Length > 0) tokens.Add(cur.ToString());
            return tokens;
        }
    }
}
