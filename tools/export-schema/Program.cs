using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using DW2ModLauncher.XmlPatching;

namespace ExportSchema
{
    /// <summary>
    /// Dumps the patch schema (what XmlPatching reflects from the game's DistantWorlds.Types.dll at run time) to JSON, so tools
    /// that cannot load the game's assemblies, such as the VS Code extension, know which element is a field, a struct or a list.
    /// Usage: export-schema &lt;game folder&gt; &lt;output.json&gt;
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length != 2)
            {
                Console.Error.WriteLine("usage: export-schema <game folder> <output.json>");
                return 2;
            }
            string gameDir = args[0];
            string typesPath = Path.Combine(gameDir, "DistantWorlds.Types.dll");
            if (!File.Exists(typesPath))
            {
                Console.Error.WriteLine("not found: " + typesPath);
                return 2;
            }

            AssemblyLoadContext.Default.Resolving += (ctx, name) =>
            {
                string p = Path.Combine(gameDir, name.Name + ".dll");
                return File.Exists(p) ? ctx.LoadFromAssemblyPath(p) : null;
            };

            Assembly types = Assembly.LoadFrom(typesPath);
            Type[] all;
            try
            {
                all = types.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                all = ex.Types.Where(t => t != null).ToArray();
            }

            // Same discovery as Loader/XmlPatchHooks.BuildSchemas: a list type with a static XmlSerializationHelper "XmlSerializer".
            SortedDictionary<string, SchemaRoot> roots = new SortedDictionary<string, SchemaRoot>(StringComparer.Ordinal);
            foreach (Type t in all)
            {
                FieldInfo f = t.IsClass ? t.GetField("XmlSerializer", BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly) : null;
                if (f == null || !f.FieldType.IsGenericType || f.FieldType.GetGenericTypeDefinition().Name != "XmlSerializationHelper`1") continue;
                SchemaRoot root = new SchemaReflector().BuildRoot(t);
                if (root != null) roots[root.RootElement] = root;
            }

            Dictionary<SchemaType, int> ids = new Dictionary<SchemaType, int>();
            List<SchemaType> order = new List<SchemaType>();
            Func<SchemaType, int> idOf = null;
            idOf = type =>
            {
                if (ids.TryGetValue(type, out int id)) return id;
                id = order.Count;
                ids[type] = id;
                order.Add(type);
                foreach (SchemaMember m in type.Members.Values)
                    if (m.Type != null) idOf(m.Type);
                return id;
            };
            foreach (SchemaRoot r in roots.Values) idOf(r.EntityType);

            using (FileStream fs = File.Create(args[1]))
            using (Utf8JsonWriter w = new Utf8JsonWriter(fs, new JsonWriterOptions { Indented = false }))
            {
                w.WriteStartObject();
                w.WriteString("generatedFrom", "DistantWorlds.Types.dll " + types.GetName().Version);
                w.WriteStartObject("roots");
                foreach (SchemaRoot r in roots.Values)
                {
                    w.WriteStartObject(r.RootElement);
                    w.WriteString("entity", r.EntityElement);
                    w.WriteNumber("type", idOf(r.EntityType));
                    w.WriteEndObject();
                }
                w.WriteEndObject();

                w.WriteStartArray("types");
                foreach (SchemaType type in order)
                {
                    w.WriteStartObject();
                    foreach (SchemaMember m in type.Members.Values.OrderBy(x => x.Name, StringComparer.Ordinal))
                    {
                        w.WriteStartObject(m.Name);
                        w.WriteString("k", m.Kind == MemberKind.Scalar ? "s" : m.Kind == MemberKind.Struct ? "t" : "l");
                        if (m.Kind == MemberKind.List) w.WriteString("item", m.ItemName);
                        if (m.Type != null) w.WriteNumber("type", ids[m.Type]);
                        if (m.Scalar != null)
                        {
                            w.WriteString("v", m.Scalar.Kind.ToString().ToLowerInvariant());
                            if (m.Scalar.Kind == ScalarKind.Enum)
                            {
                                w.WriteBoolean("flags", m.Scalar.IsFlagsEnum);
                                w.WriteStartArray("names");
                                foreach (string n in m.Scalar.EnumNames) w.WriteStringValue(n);
                                w.WriteEndArray();
                            }
                        }
                        w.WriteEndObject();
                    }
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteEndObject();
            }

            Console.WriteLine(roots.Count + " roots, " + order.Count + " types -> " + args[1]);
            return 0;
        }
    }
}
