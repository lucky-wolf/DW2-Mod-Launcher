using System.IO;

namespace DW2ModLauncher.Core.Services
{
    public static class FileNames
    {
        public static string Safe(string value)
        {
            string result = value ?? "Profile";
            foreach (char c in Path.GetInvalidFileNameChars()) result = result.Replace(c, '_');
            return string.IsNullOrWhiteSpace(result) ? "Profile" : result.Trim();
        }

        public static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (string file in Directory.GetFiles(source, "*", SearchOption.TopDirectoryOnly)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
            foreach (string dir in Directory.GetDirectories(source, "*", SearchOption.TopDirectoryOnly)) CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
        }
    }
}
