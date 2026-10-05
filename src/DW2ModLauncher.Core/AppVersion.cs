using System.Reflection;

namespace DW2ModLauncher.Core
{
    /// <summary>The launcher's semver, baked into the assembly by MinVer from the git tag; untagged (dev) builds end in "-dev".</summary>
    public static class AppVersion
    {
        /// <summary>The project's GitHub releases page, where new launcher versions are published.</summary>
        public const string ReleasesUrl = "https://github.com/lucky-wolf/DW2-Mod-Launcher/releases";

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
