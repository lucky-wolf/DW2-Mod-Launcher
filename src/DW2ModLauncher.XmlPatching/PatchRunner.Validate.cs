using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace DW2ModLauncher.XmlPatching
{
    // Static checks: everything that can be decided from the patch file, the game's types (schema) and the key map alone,
    // without looking at the data. An element that fails is reported and marked invalid, so Apply skips it and its subtree.
    public sealed partial class PatchRunner
    {
        private void ValidateFile(PatchFile f, SchemaRoot schema)
        {
            if (!_keys.TryGetKey(schema.EntityElement, out string keyField))
            {
                foreach (XElement e in f.Doc.Root.Elements())
                    Fail(f, e, "no key is known for <" + schema.EntityElement + ">, so it cannot be selected with id (add it to the key map)");
                return;
            }

            foreach (XElement e in f.Doc.Root.Elements())
            {
                if (e.Name.LocalName != schema.EntityElement)
                {
                    Fail(f, e, "<" + e.Name.LocalName + "> is not an entity of <" + f.Doc.Root.Name.LocalName + ">; expected <" + schema.EntityElement + ">" + Suggest(e.Name.LocalName, new[] { schema.EntityElement }));
                    continue;
                }
                if (!CheckAttributes(f, e)) continue;

                string op = Attr(e, "op");
                if (Attr(e, "id") == null)
                {
                    Fail(f, e, "<" + e.Name.LocalName + "> needs id=\"...\" (its " + keyField + ")");
                    continue;
                }
                if (e.Attribute("index") != null)
                {
                    Fail(f, e, "<" + e.Name.LocalName + "> is an entity; index applies to list items only");
                    continue;
                }
                if (op != null && op != "remove")
                {
                    Fail(f, e, "op=\"" + op + "\" is not allowed on an entity (only remove; a normal data file replaces a whole entity)");
                    continue;
                }
                if (op == "remove") continue;
                ValidateChildren(f, e, schema.EntityType);
            }
        }

        /// <summary>Attribute names and op/index values on one element; false (after reporting) if anything is off.</summary>
        private bool CheckAttributes(PatchFile f, XElement e)
        {
            foreach (XAttribute a in e.Attributes())
            {
                if (a.IsNamespaceDeclaration) continue;
                string name = a.Name.LocalName;
                if (!KnownAttributes.Contains(name))
                {
                    Fail(f, e, "unknown attribute '" + name + "' on <" + e.Name.LocalName + "> (known: id, index, op)" + Suggest(name, KnownAttributes));
                    return false;
                }
            }
            string op = Attr(e, "op");
            if (op != null && op != "add" && op != "remove" && op != "replace")
            {
                Fail(f, e, "op=\"" + op + "\" is not valid (use add, remove or replace)" + Suggest(op, new[] { "add", "remove", "replace" }));
                return false;
            }
            string index = Attr(e, "index");
            if (index != null && !(int.TryParse(index, out int n) && n >= 1))
            {
                Fail(f, e, "index=\"" + index + "\" is not valid: indexes are whole numbers starting at 1 (the first item is index=\"1\")");
                return false;
            }
            return true;
        }

        private void ValidateChildren(PatchFile f, XElement el, SchemaType type)
        {
            foreach (XElement c in el.Elements())
            {
                string name = c.Name.LocalName;
                if (!type.Members.TryGetValue(name, out SchemaMember m))
                {
                    Fail(f, c, "<" + type.Name + "> has no field '" + name + "'" + Suggest(name, type.Members.Keys));
                    continue;
                }
                if (!CheckAttributes(f, c)) continue;

                string op = Attr(c, "op");
                if (Attr(c, "id") != null || c.Attribute("index") != null)
                {
                    Fail(f, c, "<" + name + "> is a " + (m.Kind == MemberKind.List ? "list; select the items inside it" : m.Kind == MemberKind.Struct ? "struct" : "field") + ", not a list item: id/index do not apply here");
                    continue;
                }
                if (op == "add")
                {
                    Fail(f, c, "op=\"add\" adds a list item; <" + name + "> is " + (m.Kind == MemberKind.List ? "the list itself (put op=\"add\" on an item inside it, or use op=\"replace\" on the list)" : "a field (just write its value)"));
                    continue;
                }

                switch (m.Kind)
                {
                    case MemberKind.Scalar:
                        if (op != "remove") ValidateScalarText(f, c, m.Scalar);
                        break;
                    case MemberKind.Struct:
                        if (op == "replace") ValidateLiteral(f, c, m.Type);
                        else if (op == null) ValidateChildren(f, c, m.Type);
                        break;
                    case MemberKind.List:
                        if (op == "replace") ValidateLiteralList(f, c, m);
                        else if (op == null) ValidateItems(f, c, m);
                        break;
                }
            }
        }

        private void ValidateScalarText(PatchFile f, XElement c, ScalarSpec spec)
        {
            if (c.HasElements)
            {
                Fail(f, c, "<" + c.Name.LocalName + "> is a field and takes text, not child elements");
                return;
            }
            if (!spec.TryValidate(c.Value, out string expected))
                Fail(f, c, "'" + c.Value + "' is not valid for <" + c.Name.LocalName + ">: expected " + expected);
        }

        private void ValidateItems(PatchFile f, XElement list, SchemaMember m)
        {
            foreach (XElement it in list.Elements())
            {
                if (it.Name.LocalName != m.ItemName)
                {
                    Fail(f, it, "<" + list.Name.LocalName + "> holds <" + m.ItemName + "> items, not <" + it.Name.LocalName + ">" + Suggest(it.Name.LocalName, new[] { m.ItemName }));
                    continue;
                }
                if (!CheckAttributes(f, it)) continue;
                if (m.IsScalarList) ValidateScalarItem(f, it, m);
                else ValidateStructItem(f, it, m, list);
            }
        }

        private void ValidateScalarItem(PatchFile f, XElement it, SchemaMember m)
        {
            string op = Attr(it, "op");
            bool hasIndex = it.Attribute("index") != null;
            if (it.HasElements)
            {
                Fail(f, it, "<" + m.ItemName + "> holds a value, not child elements");
                return;
            }
            if (Attr(it, "id") != null)
            {
                Fail(f, it, "<" + m.ItemName + "> items have no id; select by value (op=\"remove\") or by index");
                return;
            }
            if (op == "replace")
            {
                Fail(f, it, "op=\"replace\" does not apply to a <" + m.ItemName + "> item; set it with index=\"N\", or replace the whole list with op=\"replace\" on the list");
                return;
            }
            if (op == null && !hasIndex)
            {
                Fail(f, it, "<" + m.ItemName + "> is a list item: say what to do with it: op=\"add\", op=\"remove\" (by value) or index=\"N\" (set the N-th)");
                return;
            }
            if (op == "remove" && hasIndex) return; // by position: the text is ignored
            ValidateScalarText(f, it, m.Scalar);
        }

        private void ValidateStructItem(PatchFile f, XElement it, SchemaMember m, XElement list)
        {
            string op = Attr(it, "op");
            bool hasId = Attr(it, "id") != null;
            bool hasIndex = it.Attribute("index") != null;

            if (op == "add")
            {
                if (hasId)
                {
                    Fail(f, it, "op=\"add\" creates a new <" + m.ItemName + ">; it cannot have id (write its key as an element). index=\"N\" would insert it at position N");
                    return;
                }
                ValidateLiteral(f, it, m.Type);
                return;
            }

            if (!hasId && !hasIndex)
            {
                bool keyed = _keys.TryGetKey(m.ItemName, out string keyField);
                Fail(f, it, "<" + m.ItemName + "> is a list item of <" + list.Name.LocalName + ">; say which one: " + (keyed ? "id=\"" + keyField + " value\", " : string.Empty) + "index=\"N\" or op=\"add\"");
                return;
            }
            if (hasId && hasIndex)
            {
                Fail(f, it, "use either id or index on <" + m.ItemName + ">, not both");
                return;
            }
            if (hasId && !_keys.TryGetKey(m.ItemName, out _))
            {
                Fail(f, it, "<" + m.ItemName + "> has no id (it is not keyed); use index=\"N\"");
                return;
            }

            if (op == "replace") ValidateLiteral(f, it, m.Type);
            else if (op == null) ValidateChildren(f, it, m.Type);
        }

        // Literal content (op="add"/"replace"): plain data, no patch attributes anywhere inside.

        private void ValidateLiteral(PatchFile f, XElement el, SchemaType type)
        {
            foreach (XElement c in el.Elements())
            {
                string name = c.Name.LocalName;
                if (!type.Members.TryGetValue(name, out SchemaMember m))
                {
                    Fail(f, c, "<" + type.Name + "> has no field '" + name + "'" + Suggest(name, type.Members.Keys));
                    continue;
                }
                if (!NoPatchAttributes(f, c)) continue;
                switch (m.Kind)
                {
                    case MemberKind.Scalar:
                        ValidateScalarText(f, c, m.Scalar);
                        break;
                    case MemberKind.Struct:
                        ValidateLiteral(f, c, m.Type);
                        break;
                    case MemberKind.List:
                        ValidateLiteralList(f, c, m);
                        break;
                }
            }
        }

        private void ValidateLiteralList(PatchFile f, XElement list, SchemaMember m)
        {
            if (!NoPatchAttributes(f, list, allowOpOnSelf: true)) return;
            foreach (XElement it in list.Elements())
            {
                if (it.Name.LocalName != m.ItemName)
                {
                    Fail(f, it, "<" + list.Name.LocalName + "> holds <" + m.ItemName + "> items, not <" + it.Name.LocalName + ">" + Suggest(it.Name.LocalName, new[] { m.ItemName }));
                    continue;
                }
                if (!NoPatchAttributes(f, it)) continue;
                if (m.IsScalarList) ValidateScalarText(f, it, m.Scalar);
                else ValidateLiteral(f, it, m.Type);
            }
        }

        private bool NoPatchAttributes(PatchFile f, XElement e, bool allowOpOnSelf = false)
        {
            foreach (XAttribute a in e.Attributes())
            {
                if (a.IsNamespaceDeclaration) continue;
                if (allowOpOnSelf && a.Name.LocalName == "op") continue;
                Fail(f, e, "'" + a.Name.LocalName + "' is not allowed inside a replaced or added element: its content is plain data");
                return false;
            }
            return true;
        }
    }
}
