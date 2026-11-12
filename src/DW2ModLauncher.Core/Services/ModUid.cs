using System;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>A mod's identity: a random UUID written into its dw2modlauncher.json "uid" where the mod is made or first published.</summary>
    public static class ModUid
    {
        /// <summary>A new random identity, in the canonical form (lower case, hyphenated).</summary>
        public static string New() { return Guid.NewGuid().ToString("D"); }

        /// <summary>The canonical form of <paramref name="text"/>, or null when it is blank or not a UUID.</summary>
        public static string Normalize(string text)
        {
            return Guid.TryParse((text ?? "").Trim(), out Guid guid) && guid != Guid.Empty ? guid.ToString("D") : null;
        }

        /// <summary>True when both are identities and the same one.</summary>
        public static bool Same(string a, string b)
        {
            return a != null && b != null && string.Equals(a, b, StringComparison.Ordinal);
        }
    }
}
