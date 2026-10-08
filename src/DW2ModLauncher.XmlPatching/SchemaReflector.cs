using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml.Serialization;

namespace DW2ModLauncher.XmlPatching
{
    /// <summary>
    /// Builds a <see cref="SchemaRoot"/> by reflecting over a deserialization type the way XmlSerializer sees it:
    /// public fields and read/write properties become elements, collections become wrapped lists, [XmlIgnore] members are
    /// skipped, [XmlElement]/[XmlArray]/[XmlArrayItem]/[XmlType] rename. Shapes the patcher cannot model (unwrapped
    /// collections, attributes, dictionaries, IXmlSerializable, ...) are left out and reported in <see cref="Notes"/>;
    /// a patch that touches such a member is rejected as "unknown field".
    /// </summary>
    public sealed class SchemaReflector
    {
        private readonly Dictionary<Type, SchemaType> _structs = new Dictionary<Type, SchemaType>();

        /// <summary>What was skipped and why, for the log.</summary>
        public List<string> Notes { get; } = new List<string>();

        /// <summary>The root of a list type such as <c>RaceList : List&lt;Race&gt;</c> (XML root <c>ArrayOfRace</c>), or null if it is not a list.</summary>
        public SchemaRoot BuildRoot(Type listType)
        {
            Type item = ItemTypeOf(listType);
            if (item == null)
            {
                Notes.Add(listType.FullName + ": not a list type");
                return null;
            }
            string entityName = XmlTypeName(item);
            return new SchemaRoot
            {
                RootElement = "ArrayOf" + Capitalize(entityName),
                EntityElement = entityName,
                EntityType = StructOf(item)
            };
        }

        /// <summary>The element name XmlSerializer uses for a type (<c>short</c> for Int16, the type or [XmlType] name for classes).</summary>
        public static string XmlTypeName(Type type)
        {
            Type t = Nullable.GetUnderlyingType(type) ?? type;
            if (t.IsEnum || !t.IsPrimitive && t != typeof(string) && t != typeof(decimal))
            {
                XmlTypeAttribute xt = t.GetCustomAttribute<XmlTypeAttribute>();
                if (xt != null && !string.IsNullOrEmpty(xt.TypeName)) return xt.TypeName;
                return t.Name;
            }
            if (t == typeof(short)) return "short";
            if (t == typeof(int)) return "int";
            if (t == typeof(long)) return "long";
            if (t == typeof(byte)) return "unsignedByte";
            if (t == typeof(sbyte)) return "byte";
            if (t == typeof(ushort)) return "unsignedShort";
            if (t == typeof(uint)) return "unsignedInt";
            if (t == typeof(ulong)) return "unsignedLong";
            if (t == typeof(float)) return "float";
            if (t == typeof(double)) return "double";
            if (t == typeof(bool)) return "boolean";
            if (t == typeof(string)) return "string";
            if (t == typeof(decimal)) return "decimal";
            if (t == typeof(char)) return "char";
            return t.Name;
        }

        private static string Capitalize(string name)
        {
            return string.IsNullOrEmpty(name) ? name : char.ToUpperInvariant(name[0]) + name.Substring(1);
        }

        private static Type ItemTypeOf(Type type)
        {
            if (type == typeof(string) || type == typeof(byte[])) return null;
            if (type.IsArray) return type.GetElementType();
            foreach (Type i in new[] { type }.Concat(type.GetInterfaces()))
            {
                if (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IList<>)) return i.GetGenericArguments()[0];
            }
            return null;
        }

        private SchemaType StructOf(Type type)
        {
            if (_structs.TryGetValue(type, out SchemaType existing)) return existing;
            SchemaType schema = new SchemaType(XmlTypeName(type));
            _structs[type] = schema; // before the members, so recursive types terminate

            foreach (FieldInfo f in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (f.IsInitOnly || f.IsLiteral) continue;
                AddMember(schema, type, f.Name, f.FieldType, f);
            }
            foreach (PropertyInfo p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!p.CanRead || p.GetIndexParameters().Length > 0 || p.GetMethod == null || !p.GetMethod.IsPublic) continue;
                if (!p.CanWrite && ItemTypeOf(p.PropertyType) == null) continue; // get-only collections are filled by XmlSerializer; other get-only members are not serialized
                AddMember(schema, type, p.Name, p.PropertyType, p);
            }
            return schema;
        }

        private void AddMember(SchemaType owner, Type ownerType, string memberName, Type memberType, MemberInfo info)
        {
            if (info.GetCustomAttribute<XmlIgnoreAttribute>() != null) return;
            string where = ownerType.Name + "." + memberName;
            if (info.GetCustomAttribute<XmlAttributeAttribute>() != null || info.GetCustomAttribute<XmlTextAttribute>() != null)
            {
                Notes.Add(where + ": XML attribute/text members are not patchable");
                return;
            }

            Type t = Nullable.GetUnderlyingType(memberType) ?? memberType;
            XmlElementAttribute el = info.GetCustomAttribute<XmlElementAttribute>();
            XmlArrayAttribute arr = info.GetCustomAttribute<XmlArrayAttribute>();
            string name = !string.IsNullOrEmpty(el?.ElementName) ? el.ElementName : !string.IsNullOrEmpty(arr?.ElementName) ? arr.ElementName : memberName;

            ScalarSpec scalar = ScalarOf(t);
            if (scalar != null)
            {
                owner.Members[name] = new SchemaMember { Name = name, Kind = MemberKind.Scalar, Scalar = scalar };
                return;
            }

            Type item = ItemTypeOf(t);
            if (item != null)
            {
                if (el != null)
                {
                    Notes.Add(where + ": collection stored as repeated elements without a wrapper is not patchable");
                    return;
                }
                XmlArrayItemAttribute ai = info.GetCustomAttributes<XmlArrayItemAttribute>().FirstOrDefault();
                string itemName = !string.IsNullOrEmpty(ai?.ElementName) ? ai.ElementName : XmlTypeName(item);
                ScalarSpec itemScalar = ScalarOf(Nullable.GetUnderlyingType(item) ?? item);
                if (itemScalar != null)
                {
                    owner.Members[name] = new SchemaMember { Name = name, Kind = MemberKind.List, ItemName = itemName, Scalar = itemScalar };
                    return;
                }
                if (IsModelable(item))
                {
                    owner.Members[name] = new SchemaMember { Name = name, Kind = MemberKind.List, ItemName = itemName, Type = StructOf(Nullable.GetUnderlyingType(item) ?? item) };
                    return;
                }
                Notes.Add(where + ": list of " + item.Name + " is not patchable");
                return;
            }

            if (IsModelable(t))
            {
                owner.Members[name] = new SchemaMember { Name = name, Kind = MemberKind.Struct, Type = StructOf(t) };
                return;
            }
            Notes.Add(where + ": " + memberType.Name + " is not patchable");
        }

        private static bool IsModelable(Type t)
        {
            if (t == typeof(object) || t.IsInterface || t.IsAbstract || t.IsPointer || t.IsGenericTypeDefinition) return false;
            if (typeof(IXmlSerializable).IsAssignableFrom(t)) return false;
            if (typeof(IDictionary).IsAssignableFrom(t)) return false;
            return t.IsClass || t.IsValueType;
        }

        private static ScalarSpec ScalarOf(Type t)
        {
            if (t.IsEnum)
            {
                return ScalarSpec.OfEnum(t.GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.GetCustomAttribute<XmlIgnoreAttribute>() == null).Select(f => f.GetCustomAttribute<XmlEnumAttribute>()?.Name ?? f.Name), t.GetCustomAttribute<FlagsAttribute>() != null);
            }
            if (t == typeof(string) || t == typeof(char) || t == typeof(byte[]) || t == typeof(DateTime) || t == typeof(TimeSpan) || t == typeof(Guid) || t == typeof(decimal)) return ScalarSpec.Text();
            if (t == typeof(bool)) return ScalarSpec.Boolean();
            if (t == typeof(float) || t == typeof(double)) return ScalarSpec.Float();
            if (t == typeof(sbyte)) return ScalarSpec.Integer(sbyte.MinValue, sbyte.MaxValue);
            if (t == typeof(byte)) return ScalarSpec.Integer(byte.MinValue, byte.MaxValue);
            if (t == typeof(short)) return ScalarSpec.Integer(short.MinValue, short.MaxValue);
            if (t == typeof(ushort)) return ScalarSpec.Integer(ushort.MinValue, ushort.MaxValue);
            if (t == typeof(int)) return ScalarSpec.Integer(int.MinValue, int.MaxValue);
            if (t == typeof(uint)) return ScalarSpec.Integer(uint.MinValue, uint.MaxValue);
            if (t == typeof(long)) return ScalarSpec.Integer(long.MinValue, long.MaxValue);
            if (t == typeof(ulong)) return ScalarSpec.Integer(0, long.MaxValue);
            return null;
        }
    }
}
