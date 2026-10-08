using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace DW2ModLauncher.XmlPatching
{
    // Applying one validated patch file to one data document. Everything structural was checked by Validate; what can still
    // go wrong here depends on the data (no such entity/item, index out of range, ambiguous id) and is reported and skipped.
    public sealed partial class PatchRunner
    {
        private void ApplyFile(PatchFile f, SchemaRoot schema, XDocument target, string targetPath)
        {
            if (!_keys.TryGetKey(schema.EntityElement, out string keyField)) return;

            foreach (XElement e in f.Doc.Root.Elements())
            {
                if (_invalid.Contains(e)) continue;
                string id = Attr(e, "id");
                List<XElement> hits = target.Root.Elements(schema.EntityElement).Where(t => IdEquals(KeyOf(t, keyField), id)).ToList();
                if (hits.Count == 0) continue;
                _matchedEntities.Add(e);

                foreach (XElement t in hits)
                {
                    if (Attr(e, "op") == "remove")
                    {
                        t.Remove();
                        Changed(f, e, Where(e) + ": removed from " + targetPath);
                    }
                    else
                    {
                        ApplyChildren(f, e, t, schema.EntityType);
                    }
                }
            }
        }

        private void ApplyChildren(PatchFile f, XElement pe, XElement te, SchemaType type)
        {
            foreach (XElement c in pe.Elements())
            {
                if (_invalid.Contains(c) || !type.Members.TryGetValue(c.Name.LocalName, out SchemaMember m)) continue;
                string op = Attr(c, "op");
                switch (m.Kind)
                {
                    case MemberKind.Scalar:
                        ApplyScalar(f, c, te, m, op);
                        break;
                    case MemberKind.Struct:
                        ApplyStruct(f, c, te, m, op);
                        break;
                    case MemberKind.List:
                        ApplyList(f, c, te, m, op);
                        break;
                }
            }
        }

        private void ApplyScalar(PatchFile f, XElement c, XElement te, SchemaMember m, string op)
        {
            string name = c.Name.LocalName;
            XElement existing = te.Element(name);
            if (op == "remove")
            {
                List<XElement> all = te.Elements(name).ToList();
                if (all.Count == 0)
                {
                    Unchanged(f, c, Where(c) + ": already absent");
                    return;
                }
                all.ForEach(x => x.Remove());
                Changed(f, c, Where(c) + ": " + all[0].Value + " -> (removed)");
                return;
            }

            string text = m.Scalar.Normalize(c.Value);
            if (existing == null)
            {
                te.Add(new XElement(name, text));
                Changed(f, c, Where(c) + ": (absent) -> " + text);
            }
            else if (m.Scalar.SameValue(existing.Value, text))
            {
                Unchanged(f, c, Where(c) + ": " + existing.Value);
            }
            else
            {
                string old = existing.Value;
                existing.Value = text;
                Changed(f, c, Where(c) + ": " + old + " -> " + text);
            }
        }

        private void ApplyStruct(PatchFile f, XElement c, XElement te, SchemaMember m, string op)
        {
            string name = c.Name.LocalName;
            XElement existing = te.Element(name);
            if (op == "remove")
            {
                if (existing == null) Unchanged(f, c, Where(c) + ": already absent");
                else
                {
                    existing.Remove();
                    Changed(f, c, Where(c) + ": removed");
                }
                return;
            }
            if (op == "replace")
            {
                ReplaceElement(te, existing, CloneLiteral(c));
                Changed(f, c, Where(c) + ": replaced");
                return;
            }

            if (existing != null)
            {
                ApplyChildren(f, c, existing, m.Type);
                return;
            }
            // Absent struct: build it detached so an all-skipped patch does not leave an empty element behind.
            XElement created = new XElement(name);
            ApplyChildren(f, c, created, m.Type);
            if (created.HasElements) te.Add(created);
        }

        private void ApplyList(PatchFile f, XElement c, XElement te, SchemaMember m, string op)
        {
            string name = c.Name.LocalName;
            XElement container = te.Element(name);
            if (op == "remove")
            {
                if (container == null) Unchanged(f, c, Where(c) + ": already absent");
                else
                {
                    container.Remove();
                    Changed(f, c, Where(c) + ": removed (" + container.Elements().Count() + " item(s))");
                }
                return;
            }
            if (op == "replace")
            {
                int oldCount = container?.Elements().Count() ?? 0;
                ReplaceElement(te, container, CloneLiteral(c));
                Changed(f, c, Where(c) + ": list replaced (" + oldCount + " -> " + c.Elements().Count() + " item(s))");
                return;
            }

            bool created = container == null;
            if (created) container = new XElement(name);
            foreach (XElement it in c.Elements())
            {
                if (_invalid.Contains(it)) continue;
                if (m.IsScalarList) ApplyScalarItem(f, it, container, m);
                else ApplyStructItem(f, it, container, m);
            }
            if (created && container.HasElements) te.Add(container);
        }

        private void ApplyStructItem(PatchFile f, XElement it, XElement container, SchemaMember m)
        {
            string op = Attr(it, "op");
            if (op == "add")
            {
                InsertItem(f, it, container, m, CloneLiteral(it));
                return;
            }

            XElement target = Locate(f, it, container, m);
            if (target == null) return;
            Satisfy(it); // found: a patch item that only restates the item is not a failure

            if (op == "remove")
            {
                target.Remove();
                Changed(f, it, Where(it) + ": removed");
            }
            else if (op == "replace")
            {
                target.ReplaceWith(CloneLiteral(it));
                Changed(f, it, Where(it) + ": replaced");
            }
            else
            {
                ApplyChildren(f, it, target, m.Type);
            }
        }

        private void ApplyScalarItem(PatchFile f, XElement it, XElement container, SchemaMember m)
        {
            string op = Attr(it, "op");
            string indexText = Attr(it, "index");
            List<XElement> items = container.Elements(m.ItemName).ToList();
            string text = m.Scalar.Normalize(it.Value);

            if (op == "add")
            {
                InsertItem(f, it, container, m, new XElement(m.ItemName, text));
                return;
            }

            XElement target;
            if (indexText != null)
            {
                int index = int.Parse(indexText);
                if (index > items.Count)
                {
                    Defer(f, it, Where(it) + ": list has " + items.Count + " item(s)" + RangeHint(items.Count));
                    return;
                }
                target = items[index - 1];
            }
            else
            {
                target = items.FirstOrDefault(x => m.Scalar.SameValue(x.Value, text));
                if (target == null)
                {
                    Defer(f, it, Where(it) + ": no item with value '" + text + "' (has: " + Sample(items.Select(x => x.Value)) + ")");
                    return;
                }
            }

            if (op == "remove")
            {
                string old = target.Value;
                target.Remove();
                Changed(f, it, Where(it) + ": removed '" + old + "'");
            }
            else if (m.Scalar.SameValue(target.Value, text))
            {
                Unchanged(f, it, Where(it) + ": " + target.Value);
            }
            else
            {
                string old = target.Value;
                target.Value = text;
                Changed(f, it, Where(it) + ": " + old + " -> " + text);
            }
        }

        /// <summary>op="add": appends, or with index="N" inserts so the new item ends up at position N.</summary>
        private void InsertItem(PatchFile f, XElement it, XElement container, SchemaMember m, XElement newItem)
        {
            List<XElement> items = container.Elements(m.ItemName).ToList();
            string indexText = Attr(it, "index");
            if (indexText == null || int.Parse(indexText) == items.Count + 1)
            {
                container.Add(newItem);
                Changed(f, it, Where(it) + ": added as item " + (items.Count + 1));
                return;
            }
            int index = int.Parse(indexText);
            if (index > items.Count + 1)
            {
                Defer(f, it, Where(it) + ": cannot insert at position " + index + "; the list has " + items.Count + " item(s), so valid positions are 1.." + (items.Count + 1));
                return;
            }
            items[index - 1].AddBeforeSelf(newItem);
            Changed(f, it, Where(it) + ": inserted as item " + index);
        }

        /// <summary>Finds the one existing list item a patch item addresses (by id or index), or reports why not.</summary>
        private XElement Locate(PatchFile f, XElement it, XElement container, SchemaMember m)
        {
            List<XElement> items = container.Elements(m.ItemName).ToList();
            string indexText = Attr(it, "index");
            if (indexText != null)
            {
                int index = int.Parse(indexText);
                if (index > items.Count)
                {
                    Defer(f, it, Where(it) + ": list has " + items.Count + " item(s)" + RangeHint(items.Count));
                    return null;
                }
                return items[index - 1];
            }

            string id = Attr(it, "id");
            _keys.TryGetKey(m.ItemName, out string keyField);
            List<int> hits = new List<int>();
            for (int i = 0; i < items.Count; i++)
            {
                if (IdEquals(KeyOf(items[i], keyField), id)) hits.Add(i + 1);
            }
            if (hits.Count == 1) return items[hits[0] - 1];
            if (hits.Count > 1)
            {
                Defer(f, it, Where(it) + ": id matches items " + string.Join(", ", hits) + "; use index=\"N\" to pick one");
                return null;
            }
            List<string> keys = items.Select(x => KeyOf(x, keyField)).Where(k => k != null).ToList();
            Defer(f, it, Where(it) + ": no such item (has: " + Sample(keys) + ")" + Suggest(id, keys));
            return null;
        }

        private static string RangeHint(int count)
        {
            return count == 0 ? string.Empty : " (valid: 1.." + count + ")";
        }

        private static string Sample(IEnumerable<string> values)
        {
            List<string> list = values.ToList();
            if (list.Count == 0) return "nothing";
            return list.Count <= 10 ? string.Join(", ", list) : string.Join(", ", list.Take(8)) + ", ... (" + list.Count + " in all)";
        }

        private static void ReplaceElement(XElement parent, XElement existing, XElement replacement)
        {
            if (existing != null) existing.ReplaceWith(replacement);
            else parent.Add(replacement);
        }

        /// <summary>A copy of a patch element with patch attributes and comments dropped: what the game will read.</summary>
        private static XElement CloneLiteral(XElement e)
        {
            XElement copy = new XElement(e.Name.LocalName);
            foreach (XNode n in e.Nodes())
            {
                if (n is XElement child) copy.Add(CloneLiteral(child));
                else if (n is XText text) copy.Add(new XText(text.Value));
            }
            return copy;
        }
    }
}
