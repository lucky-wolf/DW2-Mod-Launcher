using System.Linq;

namespace DW2ModLauncher.Core.Services.Publishing
{
    public static class ModVersion
    {
        /// <summary>
        /// The next patch version: "1.35.8" becomes "1.35.9", "2.0" becomes "2.0.1". Anything that isn't plain
        /// dot-separated numbers (blank, "beta", "1.0-rc1") is returned unchanged so the author's own scheme is never mangled.
        /// </summary>
        public static string BumpPatch(string version)
        {
            string text = (version ?? "").Trim();
            string[] parts = text.Split('.');
            if (text.Length == 0 || parts.Any(p => p.Length == 0 || p.Length > 9 || !p.All(char.IsAsciiDigit))) return text;
            while (parts.Length < 3) parts = parts.Append("0").ToArray();
            parts[2] = (int.Parse(parts[2]) + 1).ToString();
            for (int i = 3; i < parts.Length; i++) parts[i] = "0";
            return string.Join(".", parts);
        }
    }
}
