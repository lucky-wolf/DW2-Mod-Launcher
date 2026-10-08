using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using DW2ModLauncher.Core.Models;
using DW2ModLauncher.XmlPatching;

namespace DW2ModLauncher.Core.Services
{
    public sealed class LocalizationResult
    {
        public int Files { get; set; }
        public int Entities { get; set; }
        public int Strings { get; set; }
        /// <summary>Plain-text files (GameText.txt, ...) copied into the Mod.</summary>
        public int TextFiles { get; set; }
        /// <summary>Data files that could not be read (not well-formed XML); their text is missing.</summary>
        public List<string> Unreadable { get; } = new List<string>();
    }

    public sealed class LocalizationTextFile
    {
        public string Source { get; set; }
        /// <summary>Path inside the Mod folder, with forward slashes ("GameText.txt", "dialog/zenox.txt").</summary>
        public string Relative { get; set; }
    }

    /// <summary>
    /// "Localization mod" shortcut: gathers the player-visible text of the game's data (and of the enabled Mods' data) and writes it
    /// as XML patch files into the new Mod's patches folder, ready to be translated in place. See docs/xml-patching.md.
    /// </summary>
    public static class LocalizationModBuilder
    {
        /// <summary>
        /// The data files in the order the game merges them: by file name (case-insensitive); for the same name the game's own file
        /// comes first, then the Mods in load order. Only the top level of each folder is read, as the game does.
        /// </summary>
        public static List<string> CollectDataFiles(string gameRoot, IEnumerable<ModInfo> orderedEnabledMods)
        {
            List<string> files = new List<string>();
            string data = Path.Combine(gameRoot ?? "", "data");
            if (Directory.Exists(data)) files.AddRange(Directory.GetFiles(data, "*.xml", SearchOption.TopDirectoryOnly));
            foreach (ModInfo mod in orderedEnabledMods ?? Enumerable.Empty<ModInfo>())
            {
                string root = mod.ContentRoot ?? mod.Folder;
                if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
                    files.AddRange(Directory.GetFiles(root, "*.xml", SearchOption.TopDirectoryOnly));
            }
            // OrderBy is stable, so equal names keep the game-first, then mod-load order they were added in.
            return files.OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// Only one Mod's own data files (top level of its content folder), for translating that Mod instead of the base game.
        /// Its patches folder is not read: patches only change the text of data that is already there.
        /// </summary>
        public static List<string> CollectModDataFiles(ModInfo mod)
        {
            string root = mod?.ContentRoot ?? mod?.Folder;
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return new List<string>();
            return Directory.GetFiles(root, "*.xml", SearchOption.TopDirectoryOnly)
                .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase).ToList();
        }

        // Plain-text files the game reads display text from: "KEY ;text" lines, replaced as a whole file (the last Mod with that path wins).
        private static bool IsTextFile(string relative)
        {
            string[] parts = relative.Split('/');
            if (!relative.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) return false;
            if (parts.Length == 1)
                return relative.Equals("GameText.txt", StringComparison.OrdinalIgnoreCase) || relative.Equals("Hints.txt", StringComparison.OrdinalIgnoreCase)
                    || relative.Equals("SystemNames.txt", StringComparison.OrdinalIgnoreCase);
            return parts[0].Equals("dialog", StringComparison.OrdinalIgnoreCase) || parts[0].Equals("Galactopedia", StringComparison.OrdinalIgnoreCase);
        }

        private static void AddTextFiles(string folder, Dictionary<string, LocalizationTextFile> found)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return;
            foreach (string file in Directory.GetFiles(folder, "*.txt", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(folder, file).Replace('\\', '/');
                if (!IsTextFile(relative)) continue;
                found[relative.ToLowerInvariant()] = new LocalizationTextFile { Source = file, Relative = relative }; // later folder replaces earlier, as in the game
            }
        }

        /// <summary>The game's text files (GameText.txt, Hints.txt, SystemNames.txt, dialog and Galactopedia texts), with the enabled Mods' versions replacing the game's.</summary>
        public static List<LocalizationTextFile> CollectTextFiles(string gameRoot, IEnumerable<ModInfo> orderedEnabledMods)
        {
            Dictionary<string, LocalizationTextFile> found = new Dictionary<string, LocalizationTextFile>(StringComparer.Ordinal);
            AddTextFiles(Path.Combine(gameRoot ?? "", "data"), found);
            foreach (ModInfo mod in orderedEnabledMods ?? Enumerable.Empty<ModInfo>())
                AddTextFiles(mod.ContentRoot ?? mod.Folder, found);
            return found.Values.OrderBy(f => f.Relative, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>The text files of one Mod only.</summary>
        public static List<LocalizationTextFile> CollectModTextFiles(ModInfo mod)
        {
            Dictionary<string, LocalizationTextFile> found = new Dictionary<string, LocalizationTextFile>(StringComparer.Ordinal);
            AddTextFiles(mod?.ContentRoot ?? mod?.Folder, found);
            return found.Values.OrderBy(f => f.Relative, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// Writes one patch file per data file that has text into <paramref name="modFolder"/>/patches, named like the data file (Races_Atuuk.xml).
        /// When two folders contribute a file of the same name (a Mod overriding Races.xml) each goes into a subfolder named after its folder.
        /// The text files are copied into the Mod folder at the same relative path, ready to be translated.
        /// </summary>
        public static LocalizationResult Build(string modFolder, IEnumerable<string> dataFilesInLoadOrder, IEnumerable<LocalizationTextFile> textFiles = null)
        {
            LocalizationResult result = new LocalizationResult();
            foreach (LocalizationTextFile text in textFiles ?? Enumerable.Empty<LocalizationTextFile>())
            {
                string target = Path.Combine(modFolder, text.Relative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(text.Source, target, true);
                result.TextFiles++;
            }
            LocalizationPatchGenerator generator = new LocalizationPatchGenerator();
            List<string> files = dataFilesInLoadOrder.ToList();
            HashSet<string> clashing = new HashSet<string>(
                files.GroupBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase).Where(g => g.Select(x => Path.GetDirectoryName(x)).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1).Select(g => g.Key),
                StringComparer.OrdinalIgnoreCase);
            foreach (string file in files)
            {
                try
                {
                    string name = Path.GetFileName(file);
                    string label = clashing.Contains(name) ? OutputFolderName(file) + "/" + name : name;
                    generator.AddData(XDocument.Load(file), label);
                }
                catch (XmlException)
                {
                    result.Unreadable.Add(file);
                }
            }

            string patches = Path.Combine(modFolder, "patches");
            Directory.CreateDirectory(patches);
            XmlWriterSettings settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "  ",
                NewLineHandling = NewLineHandling.None, // keep line breaks inside descriptions exactly as they are
                Encoding = new System.Text.UTF8Encoding(false)
            };
            foreach (KeyValuePair<string, LocalizationFile> kv in generator.Generate())
            {
                string target = Path.Combine(patches, kv.Key.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                using (XmlWriter writer = XmlWriter.Create(target, settings))
                    kv.Value.Document.Save(writer);
                result.Files++;
                result.Entities += kv.Value.Entities;
                result.Strings += kv.Value.Strings;
            }
            return result;
        }

        // The folder a data file comes from, as a safe subfolder name ("data" for the game's own data folder).
        private static string OutputFolderName(string file)
        {
            string name = Path.GetFileName(Path.GetDirectoryName(file) ?? "");
            return LocalModManager.FolderNameFor(string.IsNullOrWhiteSpace(name) ? "data" : name);
        }
    }
}
