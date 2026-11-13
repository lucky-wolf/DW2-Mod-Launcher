using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DW2ModLauncher.Core.Diagnostics;
using DW2ModLauncher.Core.Models;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>
    /// Gives the player's own local mods their identity (<see cref="ModUid"/>) the moment the launcher sees them without one, instead of at their first publish, so a mod
    /// that depends on another of the player's mods can name it by uid straight away (<see cref="DependencyIds"/>). "Own" means the mod's dw2modlauncher.json names the player's
    /// Author setting as its author. A mod with no author, one by someone else (a catalog download), a Workshop copy, or a file the launcher could not read is left alone.
    /// </summary>
    public static class OwnModUids
    {
        /// <summary>Whether <paramref name="mod"/> is a local mod whose author is <paramref name="authorName"/> (ignoring case); false when either is blank.</summary>
        public static bool IsOwn(ModInfo mod, string authorName)
        {
            return mod != null && !mod.IsWorkshop && !string.IsNullOrWhiteSpace(mod.Folder) && !string.IsNullOrWhiteSpace(authorName)
                && string.Equals((mod.Author ?? "").Trim(), authorName.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Stamps a uid on every own mod of <paramref name="local"/> that has none (also set on the ModInfo); returns the mod and the error for each file that could not be written.</summary>
        public static List<(string Mod, string Problem)> Stamp(IEnumerable<ModInfo> local, string authorName)
        {
            List<(string Mod, string Problem)> problems = new List<(string Mod, string Problem)>();
            foreach (ModInfo mod in (local ?? Enumerable.Empty<ModInfo>()).Where(m => IsOwn(m, authorName) && m.Uid == null && m.LauncherJsonError == null))
            {
                try
                {
                    mod.Uid = LauncherMetaWriter.StampUid(mod.Folder);
                    Logger.Log("Mod identity", mod.DisplayName + ": " + mod.Uid + " (its own mod, stamped on sight)");
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException || ex is System.Text.Json.JsonException)
                {
                    Logger.LogException("Stamp the identity of " + mod.DisplayName, ex);
                    problems.Add((mod.DisplayName ?? mod.Id, ex.Message));
                }
            }
            return problems;
        }
    }
}
