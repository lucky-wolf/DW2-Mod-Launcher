using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DW2ModLauncher.Core.Models;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>One discovered injection target: a DLL (path relative to the mod root, '/' separated) and its Namespace.Type.Method entry point.</summary>
    public class InjectionTarget
    {
        public string Dll { get; set; }
        public string EntryPoint { get; set; }
    }

    /// <summary>
    /// Finds a mod's injection DLLs by convention instead of configuration: any managed DLL below the mod root that
    /// declares a public static class named "Entry" with a public static InitWithOptions(string) or Init() method.
    /// DLLs are inspected through their metadata only (never loaded), so dependencies and native DLLs are simply skipped.
    /// </summary>
    public static class InjectionScanner
    {
        public const string EntryTypeName = "Entry";

        /// <summary>
        /// The injection targets a mod will get at launch: the dw2modlauncher.json override when it names a DLL,
        /// otherwise whatever Scan() infers from the mod's content folder.
        /// </summary>
        public static List<InjectionTarget> TargetsFor(ModInfo mod)
        {
            if (mod == null) return new List<InjectionTarget>();
            LauncherMeta meta = LauncherMetaReader.Read(mod);
            if (meta?.injection != null && !string.IsNullOrWhiteSpace(meta.injection.dll))
                return new List<InjectionTarget> { new InjectionTarget { Dll = meta.injection.dll, EntryPoint = meta.injection.entryPoint } };
            return Scan(mod.ContentRoot ?? mod.Folder);
        }

        /// <summary>Targets in ordinal path order (that order is the load order within a mod).</summary>
        public static List<InjectionTarget> Scan(string modRoot)
        {
            List<InjectionTarget> found = new List<InjectionTarget>();
            if (string.IsNullOrWhiteSpace(modRoot) || !Directory.Exists(modRoot)) return found;
            string[] files;
            try { files = Directory.GetFiles(modRoot, "*.dll", SearchOption.AllDirectories); }
            catch { return found; }

            List<string> relative = new List<string>();
            foreach (string file in files)
                relative.Add(Path.GetRelativePath(modRoot, file).Replace('\\', '/'));
            relative.Sort(StringComparer.Ordinal);

            foreach (string rel in relative)
            {
                string entry = FindEntryPoint(Path.Combine(modRoot, rel.Replace('/', Path.DirectorySeparatorChar)));
                if (entry != null) found.Add(new InjectionTarget { Dll = rel, EntryPoint = entry });
            }
            return found;
        }

        /// <summary>The "Namespace.Entry.Init" entry point for the DLL, or null when it has no conventional Entry type.</summary>
        public static string FindEntryPoint(string dllPath)
        {
            try
            {
                using (FileStream stream = File.OpenRead(dllPath))
                using (PEReader pe = new PEReader(stream))
                {
                    if (!pe.HasMetadata) return null;
                    MetadataReader md = pe.GetMetadataReader();
                    foreach (TypeDefinitionHandle handle in md.TypeDefinitions)
                    {
                        TypeDefinition type = md.GetTypeDefinition(handle);
                        if (md.GetString(type.Name) != EntryTypeName) continue;
                        // Top-level public static class (static = abstract + sealed in metadata).
                        TypeAttributes attrs = type.Attributes;
                        if ((attrs & TypeAttributes.VisibilityMask) != TypeAttributes.Public) continue;
                        if ((attrs & TypeAttributes.Abstract) == 0 || (attrs & TypeAttributes.Sealed) == 0) continue;
                        if (!HasInitMethod(md, type)) continue;
                        string ns = md.GetString(type.Namespace);
                        return (ns.Length == 0 ? "" : ns + ".") + EntryTypeName + ".Init";
                    }
                }
            }
            catch (BadImageFormatException) { }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return null;
        }

        private static bool HasInitMethod(MetadataReader md, TypeDefinition type)
        {
            foreach (MethodDefinitionHandle mh in type.GetMethods())
            {
                MethodDefinition method = md.GetMethodDefinition(mh);
                MethodAttributes a = method.Attributes;
                if ((a & MethodAttributes.MemberAccessMask) != MethodAttributes.Public || (a & MethodAttributes.Static) == 0) continue;
                string name = md.GetString(method.Name);
                int parameters = method.GetParameters().Count;
                if (name == "Init" && parameters == 0) return true;
                if (name == "InitWithOptions" && parameters == 1) return true;
            }
            return false;
        }
    }
}
