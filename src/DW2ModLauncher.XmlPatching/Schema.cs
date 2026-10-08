using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml;

namespace DW2ModLauncher.XmlPatching
{
    public enum MemberKind
    {
        Scalar,
        Struct,
        List
    }

    public enum ScalarKind
    {
        /// <summary>Any text (also used for types the patcher does not understand, e.g. dates).</summary>
        String,
        Bool,
        Integer,
        Float,
        Enum
    }

    /// <summary>What a scalar field (or scalar list item) may hold; checked when a patch is validated.</summary>
    public sealed class ScalarSpec
    {
        public ScalarKind Kind { get; set; }
        public long Min { get; set; }
        public long Max { get; set; }
        public string[] EnumNames { get; set; } = Array.Empty<string>();
        public bool IsFlagsEnum { get; set; }

        public static ScalarSpec Text()
        {
            return new ScalarSpec { Kind = ScalarKind.String };
        }

        public static ScalarSpec Boolean()
        {
            return new ScalarSpec { Kind = ScalarKind.Bool };
        }

        public static ScalarSpec Integer(long min, long max)
        {
            return new ScalarSpec { Kind = ScalarKind.Integer, Min = min, Max = max };
        }

        public static ScalarSpec Float()
        {
            return new ScalarSpec { Kind = ScalarKind.Float };
        }

        public static ScalarSpec OfEnum(IEnumerable<string> names, bool flags = false)
        {
            return new ScalarSpec { Kind = ScalarKind.Enum, EnumNames = names.ToArray(), IsFlagsEnum = flags };
        }

        /// <summary>Strings keep their text as written; everything else is compared and stored trimmed.</summary>
        public string Normalize(string text)
        {
            return Kind == ScalarKind.String ? text : (text ?? string.Empty).Trim();
        }

        public bool TryValidate(string text, out string expected)
        {
            expected = Describe();
            string t = Normalize(text);
            switch (Kind)
            {
                case ScalarKind.String:
                    return true;
                case ScalarKind.Bool:
                    try
                    {
                        XmlConvert.ToBoolean(t);
                        return true;
                    }
                    catch (FormatException)
                    {
                        return false;
                    }
                case ScalarKind.Integer:
                    return long.TryParse(t, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long l) && l >= Min && l <= Max;
                case ScalarKind.Float:
                    try
                    {
                        XmlConvert.ToDouble(t);
                        return true;
                    }
                    catch (FormatException)
                    {
                        return false;
                    }
                case ScalarKind.Enum:
                    if (t.Length == 0) return false;
                    string[] parts = IsFlagsEnum ? t.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries) : new[] { t };
                    return parts.Length > 0 && parts.All(p => EnumNames.Contains(p, StringComparer.Ordinal));
                default:
                    return true;
            }
        }

        public string Describe()
        {
            switch (Kind)
            {
                case ScalarKind.Bool: return "true or false";
                case ScalarKind.Integer: return "a whole number from " + Min + " to " + Max;
                case ScalarKind.Float: return "a number";
                case ScalarKind.Enum: return "one of: " + string.Join(", ", EnumNames.Take(12)) + (EnumNames.Length > 12 ? ", ..." : string.Empty);
                default: return "text";
            }
        }

        /// <summary>True when two texts mean the same value (1.50 equals 1.5), so "unchanged" is detected for numbers.</summary>
        public bool SameValue(string a, string b)
        {
            string x = Normalize(a);
            string y = Normalize(b);
            if (string.Equals(x, y, StringComparison.Ordinal)) return true;
            if (Kind == ScalarKind.Integer || Kind == ScalarKind.Float)
            {
                if (double.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out double dx) &&
                    double.TryParse(y, NumberStyles.Float, CultureInfo.InvariantCulture, out double dy))
                    return dx == dy;
            }
            if (Kind == ScalarKind.Bool)
            {
                try
                {
                    return XmlConvert.ToBoolean(x) == XmlConvert.ToBoolean(y);
                }
                catch (FormatException)
                {
                    return false;
                }
            }
            return false;
        }
    }

    /// <summary>One member of an entity or struct: a scalar field, a nested struct, or a list.</summary>
    public sealed class SchemaMember
    {
        public string Name { get; set; }
        public MemberKind Kind { get; set; }

        /// <summary>Scalar member: its value spec. List member: the item spec when the items are scalars (null for lists of structs).</summary>
        public ScalarSpec Scalar { get; set; }

        /// <summary>Struct member: its type. List member: the item type when the items are structs.</summary>
        public SchemaType Type { get; set; }

        /// <summary>List member: the element name of one item (<c>string</c>, <c>ComponentStats</c>, ...).</summary>
        public string ItemName { get; set; }

        public bool IsScalarList
        {
            get { return Kind == MemberKind.List && Scalar != null; }
        }
    }

    /// <summary>The members of an entity or struct, by XML element name.</summary>
    public sealed class SchemaType
    {
        public SchemaType(string name)
        {
            Name = name;
        }

        public string Name { get; }
        public Dictionary<string, SchemaMember> Members { get; } = new Dictionary<string, SchemaMember>(StringComparer.Ordinal);

        public SchemaType Scalar(string name, ScalarSpec spec = null)
        {
            Members[name] = new SchemaMember { Name = name, Kind = MemberKind.Scalar, Scalar = spec ?? ScalarSpec.Text() };
            return this;
        }

        public SchemaType Struct(string name, SchemaType type)
        {
            Members[name] = new SchemaMember { Name = name, Kind = MemberKind.Struct, Type = type };
            return this;
        }

        public SchemaType ListOfScalars(string name, string itemName, ScalarSpec item)
        {
            Members[name] = new SchemaMember { Name = name, Kind = MemberKind.List, ItemName = itemName, Scalar = item };
            return this;
        }

        public SchemaType ListOf(string name, string itemName, SchemaType item)
        {
            Members[name] = new SchemaMember { Name = name, Kind = MemberKind.List, ItemName = itemName, Type = item };
            return this;
        }
    }

    /// <summary>A data file root such as <c>ArrayOfRace</c>, and the entity it holds.</summary>
    public sealed class SchemaRoot
    {
        public string RootElement { get; set; }
        public string EntityElement { get; set; }
        public SchemaType EntityType { get; set; }
    }
}
