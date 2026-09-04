using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>
    /// Reads and writes the simple "key=value" INI files used by managed MODs (comments start with # or ;).
    /// </summary>
    public static class IniFile
    {
        public static Dictionary<string, string> Read(string path)
        {
            Dictionary<string, string> d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return d;
            try
            {
                foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    d[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }
            }
            catch { }
            return d;
        }

        public static bool GetBool(Dictionary<string, string> d, string key, bool fallback)
        {
            string v;
            if (!d.TryGetValue(key, out v)) return fallback;
            return v.Equals("true", StringComparison.OrdinalIgnoreCase) || v == "1" || v.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }

        public static string GetValue(Dictionary<string, string> d, string key, string fallback)
        {
            string v;
            return d.TryGetValue(key, out v) ? v : fallback;
        }

        /// <summary>Updates existing key=value lines in place and appends any keys not already present.</summary>
        public static void Write(string path, Dictionary<string, string> values)
        {
            List<string> lines = File.Exists(path) ? File.ReadAllLines(path, Encoding.UTF8).ToList() : new List<string>();
            HashSet<string> done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < lines.Count; i++)
            {
                string trimmed = lines[i].Trim();
                if (trimmed.StartsWith("#") || trimmed.StartsWith(";")) continue;
                int eq = trimmed.IndexOf('=');
                if (eq <= 0) continue;
                string key = trimmed.Substring(0, eq).Trim();
                string value;
                if (values.TryGetValue(key, out value))
                {
                    lines[i] = key + "=" + value;
                    done.Add(key);
                }
            }
            foreach (KeyValuePair<string, string> kv in values)
                if (!done.Contains(kv.Key)) lines.Add(kv.Key + "=" + kv.Value);
            File.WriteAllLines(path, lines.ToArray(), new UTF8Encoding(true));
        }
    }
}
