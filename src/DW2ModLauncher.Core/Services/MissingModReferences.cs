using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>
    /// Takes the load-order entries of mods that are not installed out of mods.json and every profile file. A mod that was renamed, deleted or
    /// unsubscribed leaves its "mods/&lt;folder&gt;" or "steam/&lt;id&gt;" token behind; nothing else ever removes it, and it can match a folder
    /// of the same name later (a test copy of the game that still has the old folder, say) and enable a mod the player never chose.
    /// Run after <see cref="LocalModIdentity"/> has carried a renamed local mod's entry to its new name, so a rename is never mistaken for a deletion.
    /// A token counts as missing only when its folder is gone from a mods folder that exists: a mods or Workshop folder that cannot be found
    /// proves nothing, so its tokens are kept. Entries of any other form are kept.
    /// </summary>
    public static class MissingModReferences
    {
        public class Result
        {
            /// <summary>The tokens that were removed (from at least one file), sorted.</summary>
            public List<string> Removed = new List<string>();
            /// <summary>Files that could not be rewritten, one message each; their entries are still there.</summary>
            public List<string> Problems = new List<string>();
        }

        /// <param name="order">The state read from <paramref name="modsJsonPath"/>; updated in place when that file is rewritten.</param>
        /// <param name="gameRoot">While a game from this folder runs nothing is changed: the game rewrites these files itself.</param>
        public static Result Prune(GameProfileStore profiles, ModOrderState order, string modsJsonPath, string managedModsRoot, string workshopRoot, string gameRoot)
        {
            Result result = new Result();
            if (order == null || order.ReadFailed || string.IsNullOrWhiteSpace(modsJsonPath)) return result;
            if (!string.IsNullOrWhiteSpace(gameRoot) && GameProcess.IsRunning(gameRoot)) return result;

            HashSet<string> removed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Func<string, bool> missing = token => IsMissing(token, managedModsRoot, workshopRoot);

            if (order.FileFound)
            {
                List<string> kept = order.Order.Where(t => !missing(t)).ToList();
                if (kept.Count != order.Order.Count)
                {
                    try
                    {
                        List<string> written = ModOrderStore.Write(modsJsonPath, kept);
                        if (written != null)
                        {
                            foreach (string token in order.Order.Where(missing)) removed.Add(token);
                            order.Order = written;
                        }
                    }
                    catch (Exception ex)
                    {
                        Diagnostics.Logger.LogException("Remove missing mods from mods.json", ex);
                        result.Problems.Add(modsJsonPath + ": entries of mods that are not installed could not be removed.\n" + ex.Message);
                    }
                }
            }

            // every profile, not only the active one: a profile that kept the entries would look edited as soon as it is switched to
            foreach (string name in profiles.ListNames())
            {
                string path = profiles.PathFor(name);
                ModOrderState state = ModOrderStore.Read(path);
                if (!state.FileFound || state.ReadFailed) continue;
                List<string> kept = state.Order.Where(t => !missing(t)).ToList();
                if (kept.Count == state.Order.Count) continue;
                try
                {
                    if (ModOrderStore.Write(path, kept) == null) continue;
                    foreach (string token in state.Order.Where(missing)) removed.Add(token);
                }
                catch (Exception ex)
                {
                    Diagnostics.Logger.LogException("Remove missing mods from profile " + name, ex);
                    result.Problems.Add(path + ": entries of mods that are not installed could not be removed.\n" + ex.Message);
                }
            }

            result.Removed = removed.OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList();
            return result;
        }

        /// <summary>True when the token names a folder that is not there. Unknown forms and unfindable mods folders are never missing.</summary>
        public static bool IsMissing(string token, string managedModsRoot, string workshopRoot)
        {
            if (string.IsNullOrWhiteSpace(token)) return false;
            string root;
            string name;
            if (token.StartsWith("mods/", StringComparison.OrdinalIgnoreCase)) { root = managedModsRoot; name = token.Substring("mods/".Length); }
            else if (token.StartsWith("steam/", StringComparison.OrdinalIgnoreCase)) { root = workshopRoot; name = token.Substring("steam/".Length); }
            else return false;
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return false;
            return string.IsNullOrWhiteSpace(name) || !Directory.Exists(Path.Combine(root, name));
        }
    }
}
