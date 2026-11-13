using System.Collections.Generic;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>Finds the game folder from any store: the saved folder if it is still right, then Steam, then GOG, then a direct purchase from Matrix (the Windows uninstall entry). Empty when none is found (the player then browses to it).</summary>
    public static class GameLocator
    {
        public static string Find(string currentGameRoot)
        {
            string steam = SteamLocator.FindGameRoot(currentGameRoot);
            if (steam.Length > 0) return steam;
            string gog = FirstGameRoot(GogLocator.Candidates());
            if (gog.Length > 0) return gog;
            return FirstGameRoot(UninstallLocator.Candidates());
        }

        internal static string FirstGameRoot(IEnumerable<string> candidates)
        {
            foreach (string path in candidates)
            {
                if (GameFolder.IsGameRoot(path)) return path;
            }
            return "";
        }
    }
}
