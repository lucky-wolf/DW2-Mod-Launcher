using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>
    /// DW2's own named mod profiles, which live next to mods.json: "currentProfile.txt" holds the active profile's
    /// name and "mods.&lt;escaped name&gt;.json" holds each profile's order (same format as mods.json). See
    /// docs/plans/mod-profiles.md. Never throws on missing or bad files, except the write helpers (I/O failure).
    /// </summary>
    public class GameProfileStore
    {
        private const string CurrentFile = "currentProfile.txt";
        private static readonly Regex Escaped = new Regex("_([0-9A-Fa-f]{2})_", RegexOptions.Compiled);

        private readonly string modsFolder;

        /// <param name="modsFolder">The folder holding mods.json (null/missing yields no profiles).</param>
        public GameProfileStore(string modsFolder)
        {
            this.modsFolder = modsFolder;
        }

        /// <summary>The game writes a space as "_20_"; any other character outside a safe set is escaped the same way.</summary>
        public static string Escape(string name)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in name ?? "")
            {
                if (char.IsAsciiLetterOrDigit(c) || c == '(' || c == ')' || c == '-') sb.Append(c);
                else if (c < 256) sb.Append('_').Append(((int)c).ToString("X2")).Append('_');
                else sb.Append(c);
            }
            return sb.ToString();
        }

        public static string Unescape(string escaped)
        {
            return Escaped.Replace(escaped ?? "", m => ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString());
        }

        /// <summary>
        /// The profile's file. An existing file whose unescaped name matches (ignoring case) wins, so a profile the game
        /// escaped differently than we would is still found and overwritten rather than duplicated; otherwise the
        /// path we would create.
        /// </summary>
        public string PathFor(string name)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(modsFolder) && Directory.Exists(modsFolder))
                    foreach (string path in Directory.GetFiles(modsFolder, "mods.*.json", SearchOption.TopDirectoryOnly))
                    {
                        string file = Path.GetFileName(path);
                        string escaped = file.Substring("mods.".Length, file.Length - "mods.".Length - ".json".Length);
                        if (string.Equals(Unescape(escaped), name, StringComparison.OrdinalIgnoreCase)) return path;
                    }
            }
            catch { }
            return Path.Combine(modsFolder ?? "", "mods." + Escape(name) + ".json");
        }


        public bool Exists(string name) { return !string.IsNullOrWhiteSpace(modsFolder) && File.Exists(PathFor(name)); }

        /// <summary>The name as the profile is already listed (names compare ignoring case, like the Windows file system), else as given.</summary>
        public string CanonicalName(string name)
        {
            return ListNames().Find(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) ?? name;
        }

        /// <summary>Unescaped profile names, sorted.</summary>
        public List<string> ListNames()
        {
            List<string> names = new List<string>();
            try
            {
                if (string.IsNullOrWhiteSpace(modsFolder) || !Directory.Exists(modsFolder)) return names;
                foreach (string path in Directory.GetFiles(modsFolder, "mods.*.json", SearchOption.TopDirectoryOnly))
                {
                    string file = Path.GetFileName(path);
                    string escaped = file.Substring("mods.".Length, file.Length - "mods.".Length - ".json".Length);
                    if (escaped.Length > 0) names.Add(Unescape(escaped));
                }
            }
            catch { }
            names.Sort(StringComparer.CurrentCultureIgnoreCase);
            return names;
        }

        /// <summary>The active profile's name, or "" when there is none.</summary>
        public string ReadCurrent()
        {
            try
            {
                string path = Path.Combine(modsFolder ?? "", CurrentFile);
                return !string.IsNullOrWhiteSpace(modsFolder) && File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8).Trim() : "";
            }
            catch { return ""; }
        }

        public void WriteCurrent(string name)
        {
            File.WriteAllText(Path.Combine(modsFolder, CurrentFile), name ?? "", new UTF8Encoding(false));
        }

        /// <summary>The profile's order; FileFound is false when the profile has no file.</summary>
        public ModOrderState Read(string name) { return ModOrderStore.Read(PathFor(name)); }

        /// <summary>Writes the order into the profile's file (atomic, backed up). Throws on I/O failure.</summary>
        public List<string> Write(string name, IEnumerable<string> order) { return ModOrderStore.Write(PathFor(name), order); }

        public void Delete(string name)
        {
            string path = PathFor(name);
            if (File.Exists(path)) File.Delete(path);
        }

        /// <summary>
        /// Renames a profile. If the new name is another existing profile, that profile is overwritten (callers confirm
        /// first). A change of case only renames the file in place. Throws on I/O failure.
        /// </summary>
        public void Rename(string oldName, string newName)
        {
            string source = PathFor(oldName);
            string target = PathFor(newName);
            if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
            {
                string wanted = Path.Combine(modsFolder, "mods." + Escape(newName) + ".json");
                string hop = source + ".launcher_tmp";
                File.Move(source, hop, true);
                File.Move(hop, wanted, true);
                return;
            }
            ModOrderStore.Write(target, ModOrderStore.Read(source).Order);
            File.Delete(source);
        }
    }
}
