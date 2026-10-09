using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace DW2ModLauncher.XmlPatching
{
    /// <summary>One patch file, parsed. <see cref="Path"/> is what diagnostics print.</summary>
    public sealed class PatchFile
    {
        public string Label { get; set; }
        public string Path { get; set; }
        public XDocument Doc { get; set; }
        internal bool Validated { get; set; }
        /// <summary>Position of the owning mod in the load order. The patch only applies to data from this mod or from before it (see <see cref="PatchRunner.Apply"/>).</summary>
        public int Order { get; set; } = int.MaxValue;
    }

    /// <summary>
    /// Applies patch files (see docs/plans/xml-patching.md) to data documents. Usage: <see cref="AddFile"/> each patch file in
    /// load order, call <see cref="Apply"/> for every data file that is about to be loaded (any number of files per root
    /// type, in any order), then <see cref="Finish"/> once everything is loaded to report patches that never found their target.
    /// Nothing here throws on bad patches: problems end up in <see cref="Report"/> and the offending item is skipped.
    /// </summary>
    public sealed partial class PatchRunner
    {
        private static readonly string[] KnownAttributes = { "id", "index", "op" };

        private readonly Func<string, SchemaRoot> _schemaFor;
        private readonly KeyMap _keys;
        private readonly object _lock = new object();
        private readonly List<PatchFile> _files = new List<PatchFile>();
        private readonly Dictionary<string, SchemaRoot> _schemas = new Dictionary<string, SchemaRoot>();
        private readonly HashSet<XElement> _invalid = new HashSet<XElement>();
        private readonly HashSet<XElement> _matchedEntities = new HashSet<XElement>();
        private readonly HashSet<PatchFile> _orderSkipped = new HashSet<PatchFile>();

        // Data-dependent failures are only reported at Finish, and only for items that never applied in any file of their group:
        // an entity that several files define can lack an item in one of them and have it in another.
        private readonly Dictionary<XElement, KeyValuePair<PatchFile, string>> _pending = new Dictionary<XElement, KeyValuePair<PatchFile, string>>();
        private readonly List<XElement> _pendingOrder = new List<XElement>();
        private readonly HashSet<XElement> _satisfied = new HashSet<XElement>();
        private readonly Dictionary<string, List<string>> _knownIds = new Dictionary<string, List<string>>();
        private readonly HashSet<string> _rootsApplied = new HashSet<string>();
        private readonly HashSet<string> _rootsWithoutSchema = new HashSet<string>();
        private int _changes;

        /// <summary>Roots whose data file the game opens outside the main load pass (the tour items), so a patch for them is not "unused" at the end of the pass.</summary>
        public HashSet<string> LateRoots { get; } = new HashSet<string>(StringComparer.Ordinal);

        public PatchRunner(Func<string, SchemaRoot> schemaFor, KeyMap keys)
        {
            _schemaFor = schemaFor;
            _keys = keys ?? KeyMap.Default;
        }

        public PatchReport Report { get; } = new PatchReport();

        public IReadOnlyList<PatchFile> Files
        {
            get { return _files; }
        }

        /// <summary>Parses one patch file and queues it. Returns false (and reports why) when the file is not usable at all.</summary>
        public bool AddFile(string label, string displayPath, string xml, int order = int.MaxValue)
        {
            lock (_lock)
            {
                XDocument doc;
                try
                {
                    using (System.IO.StringReader reader = new System.IO.StringReader(xml))
                        doc = XDocument.Load(reader, LoadOptions.SetLineInfo);
                }
                catch (XmlException ex)
                {
                    Report.Add(Severity.Error, displayPath, ex.LineNumber, "not well-formed XML: " + ex.Message);
                    return false;
                }
                if (doc.Root == null || !doc.Root.Name.LocalName.StartsWith("ArrayOf", StringComparison.Ordinal))
                {
                    Report.Add(Severity.Error, displayPath, 1, "the root element must be the one of the data it patches, e.g. <ArrayOfRace>");
                    return false;
                }
                _files.Add(new PatchFile { Label = label, Path = displayPath, Doc = doc, Order = order });
                return true;
            }
        }

        public bool HasPatchesFor(string rootElement)
        {
            lock (_lock)
                return _files.Any(f => f.Doc.Root.Name.LocalName == rootElement);
        }

        /// <summary>The root element names that have at least one patch file.</summary>
        public IReadOnlyList<string> Roots()
        {
            lock (_lock)
                return _files.Select(f => f.Doc.Root.Name.LocalName).Distinct().ToList();
        }

        /// <summary>Validates every patch file without touching any data (used by "Check patches"); returns the number of rejected items.</summary>
        public int ValidateAll()
        {
            lock (_lock)
            {
                int before = _invalid.Count;
                foreach (PatchFile f in _files)
                {
                    SchemaRoot schema = SchemaFor(f.Doc.Root.Name.LocalName);
                    if (schema != null) EnsureValidated(f, schema);
                }
                return _invalid.Count - before;
            }
        }

        /// <summary>
        /// Patches one data document in place; <paramref name="targetPath"/> is only used in messages. Returns the number of changes.
        /// <paramref name="targetOrder"/> is the load-order position of the mod that owns the document (the game's own data comes before
        /// every mod): a patch applies only to data of its own mod or of mods before it, because the data of a later mod overrides
        /// the patch's result, the same as it overrides the earlier mod's own data. The default applies every patch.
        /// </summary>
        public int Apply(XDocument target, string targetPath, int targetOrder = int.MinValue)
        {
            lock (_lock)
            {
                if (target == null || target.Root == null) return 0;
                string rootName = target.Root.Name.LocalName;
                List<PatchFile> files = _files.Where(f => f.Doc.Root.Name.LocalName == rootName).ToList();
                if (files.Count == 0) return 0;

                SchemaRoot schema = SchemaFor(rootName);
                if (schema == null)
                {
                    if (_rootsWithoutSchema.Add(rootName))
                    {
                        foreach (PatchFile f in files)
                            Report.Add(Severity.Error, f.Path, 1, "the game's type for <" + rootName + "> could not be examined, so its patches are not applied");
                    }
                    return 0;
                }

                _rootsApplied.Add(rootName);
                RememberIds(rootName, schema, target);

                int before = _changes;
                foreach (PatchFile f in files)
                {
                    if (f.Order < targetOrder)
                    {
                        _orderSkipped.Add(f);
                        continue;
                    }
                    EnsureValidated(f, schema);
                    ApplyFile(f, schema, target, targetPath);
                }
                return _changes - before;
            }
        }

        /// <summary>
        /// Starts a fresh load pass (the game may load its static data more than once per run): forgets what was matched or
        /// deferred and zeroes the per-file tallies, but keeps the parsed and validated patch files.
        /// </summary>
        public void BeginPass()
        {
            lock (_lock)
            {
                _matchedEntities.Clear();
                _orderSkipped.Clear();
                _pending.Clear();
                _pendingOrder.Clear();
                _satisfied.Clear();
                _rootsApplied.Clear();
                _knownIds.Clear();
                Report.ResetTallies();
            }
        }

        /// <summary>Call once all data is loaded: reports patched entities that no data file defined, and the per-file tallies.</summary>
        public void Finish()
        {
            lock (_lock)
            {
                foreach (PatchFile f in _files)
                {
                    string rootName = f.Doc.Root.Name.LocalName;
                    if (_rootsWithoutSchema.Contains(rootName)) continue;
                    if (!_rootsApplied.Contains(rootName) && LateRoots.Contains(rootName)) continue; // opened after the pass, and warned about then
                    if (!_rootsApplied.Contains(rootName))
                    {
                        Report.Add(Severity.Warning, f.Path, 1, "no data file with root <" + rootName + "> was loaded, so this patch did nothing");
                        continue;
                    }
                    SchemaRoot schema = SchemaFor(rootName);
                    if (schema == null) continue;
                    foreach (XElement e in f.Doc.Root.Elements())
                    {
                        if (_invalid.Contains(e) || _matchedEntities.Contains(e)) continue;
                        string id = Attr(e, "id");
                        Skipped(f, e, schema.EntityElement + " id=" + id + " not found in any " + rootName + " file" + KnownIdsHint(rootName, id)
                            + (_orderSkipped.Contains(f) ? " (data of mods loaded after this one is not patched)" : string.Empty));
                    }
                }

                foreach (XElement e in _pendingOrder)
                {
                    if (_satisfied.Contains(e)) continue;
                    KeyValuePair<PatchFile, string> p = _pending[e];
                    Skipped(p.Key, e, p.Value);
                }
                _pendingOrder.Clear();
                _pending.Clear();
            }
        }

        private SchemaRoot SchemaFor(string rootName)
        {
            if (!_schemas.TryGetValue(rootName, out SchemaRoot schema))
            {
                try
                {
                    schema = _schemaFor(rootName);
                }
                catch (Exception ex)
                {
                    Report.Add(Severity.Error, rootName, 0, "building the schema failed: " + ex.Message);
                    schema = null;
                }
                _schemas[rootName] = schema;
            }
            return schema;
        }

        private void EnsureValidated(PatchFile f, SchemaRoot schema)
        {
            if (f.Validated) return;
            f.Validated = true;
            ValidateFile(f, schema);
        }

        // ---- small shared helpers ----

        private static string Attr(XElement e, string name)
        {
            return e.Attribute(name)?.Value;
        }

        private static int LineOf(XElement e)
        {
            return e is IXmlLineInfo info && info.HasLineInfo() ? info.LineNumber : 0;
        }

        /// <summary>"Race id=0 > Values > ComponentStats index=2": where in the patch an element is, for messages.</summary>
        private static string Where(XElement e)
        {
            List<string> parts = new List<string>();
            for (XElement cur = e; cur != null && cur.Parent != null; cur = cur.Parent)
            {
                string s = cur.Name.LocalName;
                string id = Attr(cur, "id");
                string index = Attr(cur, "index");
                string op = Attr(cur, "op");
                if (id != null) s += " id=" + id;
                if (index != null) s += " index=" + index;
                if (op != null) s += " op=" + op;
                parts.Add(s);
            }
            parts.Reverse();
            return string.Join(" > ", parts);
        }

        private static bool IdEquals(string a, string b)
        {
            a = a?.Trim();
            b = b?.Trim();
            if (string.Equals(a, b, StringComparison.Ordinal)) return true;
            return decimal.TryParse(a, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal x) &&
                   decimal.TryParse(b, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal y) && x == y;
        }

        private static string KeyOf(XElement item, string keyField)
        {
            return item.Element(keyField)?.Value.Trim();
        }

        private void RememberIds(string rootName, SchemaRoot schema, XDocument target)
        {
            if (!_keys.TryGetKey(schema.EntityElement, out string keyField)) return;
            if (!_knownIds.TryGetValue(rootName, out List<string> ids))
            {
                ids = new List<string>();
                _knownIds[rootName] = ids;
            }
            foreach (XElement t in target.Root.Elements(schema.EntityElement))
            {
                string k = KeyOf(t, keyField);
                if (k != null && !ids.Contains(k)) ids.Add(k);
            }
        }

        private string KnownIdsHint(string rootName, string id)
        {
            if (!_knownIds.TryGetValue(rootName, out List<string> ids) || ids.Count == 0) return string.Empty;
            string suggestion = Suggest(id, ids);
            string sample = ids.Count <= 10 ? string.Join(", ", ids) : string.Join(", ", ids.Take(8)) + ", ... (" + ids.Count + " in all)";
            return " (has: " + sample + ")" + suggestion;
        }

        /// <summary>" - did you mean 'x'?" when a candidate is close to the name, else empty.</summary>
        private static string Suggest(string name, IEnumerable<string> candidates)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            string best = null;
            int bestDistance = int.MaxValue;
            foreach (string c in candidates)
            {
                int d = Distance(name, c);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = c;
                }
            }
            return best != null && bestDistance > 0 && bestDistance <= Math.Max(1, name.Length / 3) ? " - did you mean '" + best + "'?" : string.Empty;
        }

        /// <summary>Edit distance where swapping two adjacent letters (the usual typo) costs one.</summary>
        private static int Distance(string a, string b)
        {
            int[,] d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) d[0, j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                for (int j = 1; j <= b.Length; j++)
                {
                    char x = char.ToLowerInvariant(a[i - 1]);
                    char y = char.ToLowerInvariant(b[j - 1]);
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (x == y ? 0 : 1));
                    if (i > 1 && j > 1 && x == char.ToLowerInvariant(b[j - 2]) && char.ToLowerInvariant(a[i - 2]) == y)
                        d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
                }
            }
            return d[a.Length, b.Length];
        }

        /// <summary>Reports an error for a patch element and marks it (and so its subtree) as not to be applied.</summary>
        private void Fail(PatchFile f, XElement e, string message)
        {
            _invalid.Add(e);
            Report.Add(Severity.Error, f.Path, LineOf(e), message);
            Report.CountSkipped(f.Path);
        }

        /// <summary>Reports an item that could not be applied in this pass; unlike <see cref="Fail"/> it does not disable the item for later passes.</summary>
        private void Skipped(PatchFile f, XElement e, string message)
        {
            Report.Add(Severity.Error, f.Path, LineOf(e), message);
            Report.CountSkipped(f.Path);
        }

        /// <summary>A failure that depends on the data; reported at Finish unless the same item applies in another file.</summary>
        private void Defer(PatchFile f, XElement e, string message)
        {
            if (_pending.ContainsKey(e)) return;
            _pending[e] = new KeyValuePair<PatchFile, string>(f, message);
            _pendingOrder.Add(e);
        }

        /// <summary>An element, and the items and entity around it, count as applied once something inside them applied.</summary>
        private void Satisfy(XElement e)
        {
            for (XElement x = e; x != null; x = x.Parent) _satisfied.Add(x);
        }

        private void Changed(PatchFile f, XElement e, string message)
        {
            _changes++;
            Satisfy(e);
            Report.Add(Severity.Info, f.Path, LineOf(e), message);
            Report.CountApplied(f.Path);
        }

        private void Unchanged(PatchFile f, XElement e, string message)
        {
            Satisfy(e);
            Report.Add(Severity.Info, f.Path, LineOf(e), message + " (unchanged)");
            Report.CountUnchanged(f.Path);
        }
    }
}
