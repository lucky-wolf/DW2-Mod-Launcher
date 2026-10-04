namespace DW2ModLauncher.Core.Models
{
    public class LauncherMeta
    {
        public LauncherInjection injection { get; set; }
    }

    // Optional manual override of injection discovery (normally inferred, see InjectionScanner):
    // dll is a path relative to the mod's own content folder; entryPoint is the
    // Namespace.Type.Method the loader should invoke on it.
    public class LauncherInjection
    {
        public string dll { get; set; }
        public string entryPoint { get; set; }
    }
}
