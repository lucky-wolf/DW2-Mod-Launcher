using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace DW2ModLauncher.XmlPatching
{
    /// <summary>
    /// Collects every player-visible text of the game's XML data and writes it as patch files, one per source data file
    /// (<c>Races.xml</c>, <c>Races_Atuuk.xml</c>, ...), so a translator only has to edit the text in place. Only fields on the
    /// display allow-list are collected: identifiers are never touched (a GameEvent's Name is its key, and GeneratedItemName
    /// / ActionLocationItemName / VariableName refer to other entities by name).
    /// </summary>
    public sealed class LocalizationPatchGenerator
    {
        /// <summary>Fields that hold text the player reads.</summary>
        private static readonly HashSet<string> DisplayFields = new HashSet<string>(StringComparer.Ordinal)
        {
            "Name", "Description", "MessageTitle", "ChoiceButtonText", "MarkupText", "StepTitle", "LeaderTitle",
            "MarketingName", "MarketingDescription", "DescriptionBonuses", "DescriptionObjective", "CabinetTitle"
        };

        /// <summary>Lists of plain strings (&lt;string&gt;) that hold text the player reads; written as a whole list with op="replace".</summary>
        private static readonly HashSet<string> TextLists = new HashSet<string>(StringComparer.Ordinal)
        {
            "FeatureExplanations", "Descriptions", "RuinLocationDescriptions", "EmpireNameNouns", "EmpireNameAdjectives",
            "EmpireNameMiddles", "DesignNames", "CharacterFirstNames", "CharacterLastNames"
        };

        /// <summary>Elements whose Name is an internal label rather than display text.</summary>
        private static readonly HashSet<string> InternalNameOwners = new HashSet<string>(StringComparer.Ordinal)
        {
            "CharacterRoom", "FixedStructureDefinition", "EmpireDirection"
        };

        private readonly KeyMap _keys;
        private readonly List<SourceFile> _files = new List<SourceFile>();
        private readonly Dictionary<string, Entry> _latest = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private readonly HashSet<string> _skippedRoots = new HashSet<string>(StringComparer.Ordinal);

        public LocalizationPatchGenerator(KeyMap keys = null)
        {
            _keys = keys ?? KeyMap.Default;
        }

        /// <summary>Roots that were seen but have no key in the map, so their text cannot be addressed.</summary>
        public IReadOnlyCollection<string> SkippedRoots
        {
            get { return _skippedRoots; }
        }

        /// <summary>
        /// Adds one data file. Call in game load order: an entity defined again later replaces the earlier one, as in the game, and is
        /// then written only to the file of its last definition. <paramref name="source"/> names the output file (relative path);
        /// without it the entity type's name is used.
        /// </summary>
        public void AddData(XDocument doc, string source = null)
        {
            XElement root = doc?.Root;
            if (root == null || !root.Name.LocalName.StartsWith("ArrayOf", StringComparison.Ordinal)) return;
            SourceFile file = null;
            foreach (XElement entity in root.Elements())
            {
                string name = entity.Name.LocalName;
                if (!_keys.TryGetKey(name, out string keyField))
                {
                    _skippedRoots.Add(root.Name.LocalName);
                    break;
                }
                string key = entity.Element(keyField)?.Value.Trim();
                if (string.IsNullOrEmpty(key)) continue;
                if (file == null)
                {
                    string label = source ?? name + ".xml";
                    file = _files.FirstOrDefault(f => f.Name == label);
                    if (file == null)
                    {
                        file = new SourceFile { Name = label, Root = root.Name.LocalName };
                        _files.Add(file);
                    }
                }
                Entry entry = new Entry { Element = entity, KeyField = keyField };
                string identity = name + "\n" + key;
                if (_latest.TryGetValue(identity, out Entry earlier)) earlier.Replaced = true;
                _latest[identity] = entry;
                file.Entries.Add(entry);
            }
        }

        /// <summary>The patch documents by output file name (the source data file's name). Files without any text are left out.</summary>
        public Dictionary<string, LocalizationFile> Generate()
        {
            Dictionary<string, LocalizationFile> result = new Dictionary<string, LocalizationFile>(StringComparer.Ordinal);
            foreach (SourceFile file in _files)
            {
                XElement root = new XElement(file.Root);
                int strings = 0;
                foreach (Entry entry in file.Entries)
                {
                    if (entry.Replaced) continue; // a later file defines it again; that definition's text is the one in the game
                    XElement patch = Collect(entry.Element, entry.KeyField, ref strings);
                    if (patch == null) continue;
                    patch.SetAttributeValue("id", entry.Element.Element(entry.KeyField).Value.Trim());
                    root.Add(patch);
                }
                if (strings == 0) continue;
                XDocument doc = new XDocument(
                    new XComment(" Translate the text of the fields below in place. Do not change id=\"...\" or index=\"...\": they say which entity or list item the text belongs to. "),
                    root);
                result[file.Name] = new LocalizationFile { Document = doc, Strings = strings, Entities = root.Elements().Count() };
            }
            return result;
        }

        // Copies the display-text fields of one element (an entity, a struct or a list item), keeping the path of structs and
        // list items that leads to them. Returns null when there is no text below.
        private XElement Collect(XElement source, string keyField, ref int strings)
        {
            XElement copy = new XElement(source.Name);
            foreach (XElement c in source.Elements())
            {
                string name = c.Name.LocalName;
                if (!c.HasElements)
                {
                    if (!DisplayFields.Contains(name) || name == keyField || string.IsNullOrWhiteSpace(c.Value)) continue;
                    if (name == "Name" && InternalNameOwners.Contains(source.Name.LocalName)) continue;
                    copy.Add(new XElement(c.Name, c.Value));
                    strings++;
                    continue;
                }

                List<XElement> children = c.Elements().ToList();
                bool isList = children.All(x => x.Name == children[0].Name);
                if (isList && children[0].Name.LocalName == "string" && TextLists.Contains(name))
                {
                    XElement texts = new XElement(c.Name, new XAttribute("op", "replace"));
                    foreach (XElement s in children.Where(x => !string.IsNullOrWhiteSpace(x.Value)))
                    {
                        texts.Add(new XElement(s.Name, s.Value));
                        strings++;
                    }
                    if (texts.HasElements) copy.Add(texts);
                    continue;
                }
                if (!isList)
                {
                    XElement sub = Collect(c, null, ref strings);
                    if (sub != null) copy.Add(sub);
                    continue;
                }

                bool keyed = _keys.TryGetKey(children[0].Name.LocalName, out string itemKey) && UniqueKeys(children, itemKey);
                XElement container = new XElement(c.Name);
                for (int i = 0; i < children.Count; i++)
                {
                    XElement item = Collect(children[i], keyed ? itemKey : null, ref strings);
                    if (item == null) continue;
                    if (keyed) item.SetAttributeValue("id", children[i].Element(itemKey).Value.Trim());
                    else item.SetAttributeValue("index", i + 1);
                    container.Add(item);
                }
                if (container.HasElements) copy.Add(container);
            }
            return copy.HasElements ? copy : null;
        }

        private static bool UniqueKeys(List<XElement> items, string keyField)
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (XElement item in items)
            {
                string k = item.Element(keyField)?.Value.Trim();
                if (string.IsNullOrEmpty(k) || !seen.Add(k)) return false;
            }
            return true;
        }

        private sealed class SourceFile
        {
            public string Name;
            public string Root;
            public readonly List<Entry> Entries = new List<Entry>();
        }

        private sealed class Entry
        {
            public XElement Element;
            public string KeyField;
            public bool Replaced;
        }
    }

    public sealed class LocalizationFile
    {
        public XDocument Document { get; set; }
        public int Strings { get; set; }
        public int Entities { get; set; }
    }
}
