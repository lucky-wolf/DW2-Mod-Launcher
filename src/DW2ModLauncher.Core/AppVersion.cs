using System.Reflection;

namespace DW2ModLauncher.Core
{
    /// <summary>The launcher's semver, baked into the assembly by MinVer from the git tag; untagged (dev) builds end in "-dev".</summary>
    public static class AppVersion
    {
        public static string Current
        {
            get
            {
                string version = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
                int plus = version.IndexOf('+');
                return plus >= 0 ? version.Substring(0, plus) : version;
            }
        }

        /// <summary>Human-readable form for window titles: "0.1.1" or "0.1.1 dev".</summary>
        public static string Display
        {
            get { return Current.Replace("-dev", " dev"); }
        }
    }
}
