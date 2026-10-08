using System;
using System.Linq;

namespace DW2ModLauncher.Core.Services.Publishing
{
    public enum VersionBumpLevel { Patch, Minor, Major }

    public static class ModVersion
    {
        /// <summary>True for the "none" level: the author wants the version left alone on update.</summary>
        public static bool IsNoBump(string level) { return string.Equals(level, "none", StringComparison.OrdinalIgnoreCase); }

        public static VersionBumpLevel ParseLevel(string level)
        {
            if (string.Equals(level, "minor", StringComparison.OrdinalIgnoreCase)) return VersionBumpLevel.Minor;
            if (string.Equals(level, "major", StringComparison.OrdinalIgnoreCase)) return VersionBumpLevel.Major;
            return VersionBumpLevel.Patch;
        }

        public static string LevelName(VersionBumpLevel level) { return level.ToString().ToLowerInvariant(); }

        /// <summary>
        /// The next patch version: "1.35.8" becomes "1.35.9", "2.0" becomes "2.0.1". Anything that isn't plain
        /// dot-separated numbers (blank, "beta", "1.0-rc1") is returned unchanged so the author's own scheme is never mangled.
        /// </summary>
        public static string BumpPatch(string version) { return Bump(version, VersionBumpLevel.Patch); }

        /// <summary>
        /// Raises the given part and zeroes everything after it: minor "1.35.8" becomes "1.36.0", major becomes "2.0.0".
        /// Non-numeric versions are returned unchanged, as with <see cref="BumpPatch"/>.
        /// </summary>
        public static string Bump(string version, VersionBumpLevel level)
        {
            string text = (version ?? "").Trim();
            string[] parts = text.Split('.');
            if (text.Length == 0 || parts.Any(p => p.Length == 0 || p.Length > 9 || !p.All(char.IsAsciiDigit))) return text;
            while (parts.Length < 3) parts = parts.Append("0").ToArray();
            int at = level == VersionBumpLevel.Major ? 0 : level == VersionBumpLevel.Minor ? 1 : 2;
            parts[at] = (int.Parse(parts[at]) + 1).ToString();
            for (int i = at + 1; i < parts.Length; i++) parts[i] = "0";
            return string.Join(".", parts);
        }

        /// <summary>True only for exactly X.Y.Z with plain numbers - the shape the three-box editor can represent.</summary>
        public static bool TryParseSemver(string version, out int major, out int minor, out int patch)
        {
            major = minor = patch = 0;
            string[] parts = (version ?? "").Trim().Split('.');
            if (parts.Length != 3 || parts.Any(p => p.Length == 0 || p.Length > 9 || !p.All(char.IsAsciiDigit))) return false;
            major = int.Parse(parts[0]);
            minor = int.Parse(parts[1]);
            patch = int.Parse(parts[2]);
            return true;
        }
    }
}
