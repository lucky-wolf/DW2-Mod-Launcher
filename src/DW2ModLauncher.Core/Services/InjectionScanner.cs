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

    /// <summary>Why a DLL can't be injected. For an "Entry" type that exists, later values are closer to working.</summary>
    public enum EntryProblem { NotManaged, NoEntryType, NotPublicTopLevel, NotStatic, NoInitMethod }

    /// <summary>A DLL in a mod folder that has no valid entry point. Shown to the author as an error; never written to the loader manifest.</summary>
    public class InvalidInjectionDll
    {
        public string Dll { get; set; }
        public EntryProblem Problem { get; set; }
    }

    /// <summary>
    /// Finds a mod's injection DLLs by convention instead of configuration: any managed DLL below the mod root that
    /// declares a public static class named "Entry" with a public static InitWithOptions(string) or Init() method.
    /// DLLs are inspected through their metadata only (never loaded). DLLs that don't qualify are not injected; <see cref="InvalidFor"/> reports them.
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

        /// <summary>
        /// Every DLL in the mod folder that has no valid entry point, with the reason: a mod folder holds only injectable DLLs, so
        /// anything else (native, a stray dependency, a misnamed or mis-shaped Entry) is reported as broken. Display only: Scan/TargetsFor
        /// (and so the loader manifest) never include them. Not consulted when dw2modlauncher.json names the DLL explicitly.
        /// </summary>
        public static List<InvalidInjectionDll> InvalidFor(ModInfo mod)
        {
            List<InvalidInjectionDll> invalid = new List<InvalidInjectionDll>();
            if (mod == null) return invalid;
            LauncherMeta meta = LauncherMetaReader.Read(mod);
            if (meta?.injection != null && !string.IsNullOrWhiteSpace(meta.injection.dll)) return invalid;
            string modRoot = mod.ContentRoot ?? mod.Folder;
            if (string.IsNullOrWhiteSpace(modRoot) || !Directory.Exists(modRoot)) return invalid;
            string[] files;
            try { files = Directory.GetFiles(modRoot, "*.dll", SearchOption.AllDirectories); }
            catch { return invalid; }
            List<string> relative = new List<string>();
            foreach (string file in files)
                relative.Add(Path.GetRelativePath(modRoot, file).Replace('\\', '/'));
            relative.Sort(StringComparer.Ordinal);
            foreach (string rel in relative)
            {
                string path = Path.Combine(modRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                if (FindEntryPoint(path) != null) continue;
                invalid.Add(new InvalidInjectionDll { Dll = rel, Problem = FindEntryProblem(path) });
            }
            return invalid;
        }

        /// <summary>The best explanation for why a DLL has no valid entry point (call only for DLLs where <see cref="FindEntryPoint"/> is null).</summary>
        public static EntryProblem FindEntryProblem(string dllPath)
        {
            try
            {
                using (FileStream stream = File.OpenRead(dllPath))
                using (PEReader pe = new PEReader(stream))
                {
                    if (!pe.HasMetadata) return EntryProblem.NotManaged;
                    MetadataReader md = pe.GetMetadataReader();
                    EntryProblem best = EntryProblem.NoEntryType;
                    foreach (TypeDefinitionHandle handle in md.TypeDefinitions)
                    {
                        TypeDefinition type = md.GetTypeDefinition(handle);
                        if (md.GetString(type.Name) != EntryTypeName) continue;
                        TypeAttributes attrs = type.Attributes;
                        // Report the problem closest to working, so a near-miss Entry is not hidden by an unrelated nested one.
                        EntryProblem problem = (attrs & TypeAttributes.VisibilityMask) != TypeAttributes.Public ? EntryProblem.NotPublicTopLevel
                            : (attrs & TypeAttributes.Abstract) == 0 || (attrs & TypeAttributes.Sealed) == 0 ? EntryProblem.NotStatic
                            : EntryProblem.NoInitMethod;
                        if (problem > best) best = problem;
                    }
                    return best;
                }
            }
            catch (BadImageFormatException) { }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return EntryProblem.NotManaged;
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

        /// <summary>
        /// True when the DLL's qualifying Entry class has InitWithOptions(string), i.e. it can receive the mod's settings.
        /// A DLL with only Init() loads fine but never sees its settings.
        /// </summary>
        public static bool AcceptsOptions(string dllPath)
        {
            try
            {
                using (FileStream stream = File.OpenRead(dllPath))
                using (PEReader pe = new PEReader(stream))
                {
                    if (!pe.HasMetadata) return false;
                    MetadataReader md = pe.GetMetadataReader();
                    foreach (TypeDefinitionHandle handle in md.TypeDefinitions)
                    {
                        TypeDefinition type = md.GetTypeDefinition(handle);
                        if (md.GetString(type.Name) != EntryTypeName) continue;
                        TypeAttributes attrs = type.Attributes;
                        if ((attrs & TypeAttributes.VisibilityMask) != TypeAttributes.Public) continue;
                        if ((attrs & TypeAttributes.Abstract) == 0 || (attrs & TypeAttributes.Sealed) == 0) continue;
                        if (HasPublicStaticMethod(md, type, "InitWithOptions", 1)) return true;
                    }
                }
            }
            catch (BadImageFormatException) { }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return false;
        }

        private static bool HasInitMethod(MetadataReader md, TypeDefinition type)
        {
            return HasPublicStaticMethod(md, type, "Init", 0) || HasPublicStaticMethod(md, type, "InitWithOptions", 1);
        }

        private static bool HasPublicStaticMethod(MetadataReader md, TypeDefinition type, string name, int parameters)
        {
            foreach (MethodDefinitionHandle mh in type.GetMethods())
            {
                MethodDefinition method = md.GetMethodDefinition(mh);
                MethodAttributes a = method.Attributes;
                if ((a & MethodAttributes.MemberAccessMask) != MethodAttributes.Public || (a & MethodAttributes.Static) == 0) continue;
                if (md.GetString(method.Name) == name && method.GetParameters().Count == parameters) return true;
            }
            return false;
        }
    }
}
