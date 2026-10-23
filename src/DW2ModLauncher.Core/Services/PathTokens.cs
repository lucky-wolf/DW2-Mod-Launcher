using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace DW2ModLauncher.Core.Services
{
    public enum PathTokenProblem
    {
        None,
        /// <summary>The text has a %NAME% that is not one of <see cref="PathTokens.Names"/>.</summary>
        UnknownToken,
        /// <summary>%DW2% was used but the launcher's game folder is not set.</summary>
        NoGameFolder,
        /// <summary>Linux only: the Proton prefix could not be located from the game folder.</summary>
        NoProtonPrefix
    }

    /// <summary>The result of expanding a path with %TOKEN% folder names: the path, or what is wrong with it.</summary>
    public sealed class PathExpansion
    {
        public string Path { get; init; }
        public PathTokenProblem Problem { get; init; }
        /// <summary>The offending token name for <see cref="PathTokenProblem.UnknownToken"/>.</summary>
        public string Detail { get; init; }
        public bool Ok { get { return Problem == PathTokenProblem.None; } }

        /// <summary>English description for logs and exceptions (the editor shows a translated one).</summary>
        public string Message
        {
            get
            {
                switch (Problem)
                {
                    case PathTokenProblem.UnknownToken: return "Unknown folder name %" + Detail + "% (known: " + PathTokens.KnownList + ")";
                    case PathTokenProblem.NoGameFolder: return "%DW2% needs the game folder, which is not set in the launcher";
                    case PathTokenProblem.NoProtonPrefix: return "The Proton prefix of the game was not found beside the game folder";
                    default: return "";
                }
            }
        }
    }

    /// <summary>A folder/file setting names a %FOLDER% that cannot be resolved; carries the parts so the launcher can say so in the user's language.</summary>
    public sealed class PathTokenException : InvalidOperationException
    {
        public PathTokenException(string mod, string setting, string text, PathExpansion problem)
            : base(mod + ", setting " + setting + " (" + text + "): " + problem.Message)
        {
            Mod = mod;
            Setting = setting;
            Text = text;
            Problem = problem;
        }

        public string Mod { get; }
        public string Setting { get; }
        public string Text { get; }
        public PathExpansion Problem { get; }
    }

    /// <summary>
    /// Folder names a mod's folder/file setting may start with, written the Windows way (%APPDATA%\Foo): the launcher stores them as typed and
    /// expands them only when it needs a real path (Browse, the exists check, the options handed to the game). Mods never see a token.
    /// <list type="bullet">
    /// <item>%DW2% is the game's install folder, so %DW2%\data\Logs is where the game keeps its own logs.</item>
    /// <item>%APPDATA%, %LOCALAPPDATA%, %DOCUMENTS% (the real Documents folder, wherever it is redirected) and %USERPROFILE%.</item>
    /// </list>
    /// Under Proton (Linux) the game sees the Wine prefix's profile, so these resolve to <c>C:\users\steamuser\...</c> for the game and to the
    /// same folders inside <c>compatdata/&lt;app id&gt;/pfx</c> for the launcher; %DW2% is the game folder, as <c>Z:\...</c> for the game.
    /// </summary>
    public static class PathTokens
    {
        public const string Dw2 = "DW2";
        public static readonly string[] Names = { Dw2, "APPDATA", "LOCALAPPDATA", "DOCUMENTS", "USERPROFILE" };
        public static string KnownList { get { return string.Join(", ", Names.Select(n => "%" + n + "%")); } }

        private static readonly Regex TokenPattern = new Regex(@"%([^%\\/:*?""<>|\r\n]*)%", RegexOptions.Compiled);
        private const string ProtonUser = @"users\steamuser";

        public static bool HasToken(string text) { return !string.IsNullOrEmpty(text) && TokenPattern.IsMatch(text); }

        /// <summary>The path as the launcher itself can open it (Browse start folder, exists check).</summary>
        public static PathExpansion ExpandForHost(string text, string gameRoot) { return Expand(text, gameRoot, forGame: false, OperatingSystem.IsWindows()); }

        /// <summary>The path as the game process sees it (Z:\... under Proton).</summary>
        public static PathExpansion ExpandForGame(string text, string gameRoot) { return Expand(text, gameRoot, forGame: true, OperatingSystem.IsWindows()); }

        internal static PathExpansion Expand(string text, string gameRoot, bool forGame, bool hostIsWindows)
        {
            text = (text ?? "").Trim();
            if (!HasToken(text)) return new PathExpansion { Path = text };
            PathTokenProblem problem = PathTokenProblem.None;
            string detail = null;
            string result = TokenPattern.Replace(text, m =>
            {
                string name = m.Groups[1].Value;
                string root = Names.FirstOrDefault(n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (root == null)
                {
                    if (problem == PathTokenProblem.None) { problem = PathTokenProblem.UnknownToken; detail = name; }
                    return m.Value;
                }
                string folder = RootOf(root, gameRoot, forGame, hostIsWindows, out PathTokenProblem p);
                if (p != PathTokenProblem.None && problem == PathTokenProblem.None) problem = p;
                return folder ?? m.Value;
            });
            if (problem != PathTokenProblem.None) return new PathExpansion { Problem = problem, Detail = detail };
            // the rest after a token is written with backslashes; the host wants its own separator
            if (!forGame && !hostIsWindows) result = result.Replace('\\', '/');
            return new PathExpansion { Path = result };
        }

        private static string RootOf(string name, string gameRoot, bool forGame, bool hostIsWindows, out PathTokenProblem problem)
        {
            problem = PathTokenProblem.None;
            if (name == Dw2)
            {
                if (string.IsNullOrWhiteSpace(gameRoot)) { problem = PathTokenProblem.NoGameFolder; return null; }
                string root = gameRoot.Trim().TrimEnd('/', '\\');
                return forGame ? GamePaths.ToGameVisiblePath(root, hostIsWindows) : root;
            }
            if (hostIsWindows) return WindowsFolder(name);
            // Proton: the game sees the prefix's own profile
            string relative = name switch
            {
                "APPDATA" => ProtonUser + @"\AppData\Roaming",
                "LOCALAPPDATA" => ProtonUser + @"\AppData\Local",
                "DOCUMENTS" => ProtonUser + @"\Documents",
                _ => ProtonUser
            };
            if (forGame) return @"C:\" + relative;
            string prefix = ProtonDriveC(gameRoot);
            if (prefix == null) { problem = PathTokenProblem.NoProtonPrefix; return null; }
            return Path.Combine(prefix, relative.Replace('\\', '/'));
        }

        private static string WindowsFolder(string name)
        {
            switch (name)
            {
                case "APPDATA": return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                case "LOCALAPPDATA": return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                case "DOCUMENTS": return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                default: return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }
        }

        /// <summary>.../steamapps/compatdata/&lt;app id&gt;/pfx/drive_c, found from a game folder of the form .../steamapps/common/&lt;game&gt;.</summary>
        private static string ProtonDriveC(string gameRoot)
        {
            if (string.IsNullOrWhiteSpace(gameRoot)) return null;
            DirectoryInfo common = new DirectoryInfo(gameRoot.Trim().TrimEnd('/', '\\')).Parent;
            DirectoryInfo steamapps = common?.Parent;
            if (steamapps == null || !common.Name.Equals("common", StringComparison.OrdinalIgnoreCase)) return null;
            return Path.Combine(steamapps.FullName, "compatdata", SteamLocator.AppId, "pfx", "drive_c");
        }

        /// <summary>
        /// A folder the user picked, written back with the most specific token that contains it (%DW2%, then %APPDATA%, ...), or unchanged when
        /// no token's folder contains it. The rest uses backslashes, as the game sees it.
        /// </summary>
        public static string Collapse(string pickedHostPath, string gameRoot)
        {
            return Collapse(pickedHostPath, gameRoot, OperatingSystem.IsWindows());
        }

        internal static string Collapse(string pickedHostPath, string gameRoot, bool hostIsWindows)
        {
            if (string.IsNullOrWhiteSpace(pickedHostPath)) return pickedHostPath;
            string picked = pickedHostPath.Trim().TrimEnd('/', '\\');
            StringComparison cmp = hostIsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            string best = null;
            string bestRoot = null;
            foreach (string name in Names)
            {
                PathExpansion root = Expand("%" + name + "%", gameRoot, forGame: false, hostIsWindows);
                if (!root.Ok || string.IsNullOrEmpty(root.Path)) continue;
                string r = root.Path.TrimEnd('/', '\\');
                bool inside = picked.Equals(r, cmp)
                    || (picked.StartsWith(r, cmp) && picked.Length > r.Length && (picked[r.Length] == '/' || picked[r.Length] == '\\'));
                if (inside && (bestRoot == null || r.Length > bestRoot.Length)) { best = name; bestRoot = r; }
            }
            if (best == null) return pickedHostPath.Trim();
            return "%" + best + "%" + picked.Substring(bestRoot.Length).Replace('/', '\\');
        }
    }
}
