using System;
using System.Collections.Generic;
using System.Linq;
using DW2ModLauncher.Core.Models;

namespace DW2ModLauncher.Core.Services.Publishing
{
    /// <summary>The places a mod is published to.</summary>
    [Flags]
    public enum PublishChannels
    {
        None = 0,
        Steam = 1,
        GitHub = 2
    }

    /// <summary>
    /// The owner's override, for every publishing target at once: the mod's folder as it is now counts as what the target has (Steam Workshop, GitHub release), so only
    /// later edits show as modified. It works on the targets asked for, the way Publish does, and touches only the launcher's local records; nothing is sent anywhere.
    /// </summary>
    public static class MarkUpToDate
    {
        /// <summary>
        /// Which of the asked-for targets have something to mark: Steam when the mod is published there and its folder has changed since; GitHub when the mod names a release
        /// repository and its folder has changed since the recorded release, or no release was recorded here at all (a release made before records were kept, or outside the launcher).
        /// </summary>
        /// <param name="pendingEdits">The Publish dialog holds edits not yet written to the folder: marking writes them first, so every tracked target counts as having something to mark.</param>
        public static PublishChannels Eligible(ModInfo mod, bool steam, bool github, bool pendingEdits = false)
        {
            PublishChannels result = PublishChannels.None;
            if (mod == null || mod.IsWorkshop) return result;
            bool publishedOnSteam = ModDetails.WorkshopId(mod) != null;
            if (steam && publishedOnSteam && (mod.PublishStatus == PublishStatus.Changed || (pendingEdits && mod.PublishStatus == PublishStatus.Current))) result |= PublishChannels.Steam;
            if (github && (mod.GitHubStatus == PublishStatus.Changed || mod.GitHubStatus == PublishStatus.Unknown
                || (pendingEdits && mod.GitHubStatus == PublishStatus.Current))) result |= PublishChannels.GitHub;
            return result;
        }

        /// <summary>How many files differ from the records of <paramref name="channels"/> (a file changed for both counts once).</summary>
        public static int ChangedFileCount(ModInfo mod, PublishChannels channels)
        {
            IEnumerable<string> steam = channels.HasFlag(PublishChannels.Steam) ? mod.UnpublishedChanges : Enumerable.Empty<string>();
            IEnumerable<string> github = channels.HasFlag(PublishChannels.GitHub) ? mod.GitHubChanges : Enumerable.Empty<string>();
            return steam.Concat(github).Select(c => c.Length > 2 ? c.Substring(2) : c).Distinct(StringComparer.Ordinal).Count();
        }

        /// <summary>
        /// Records the folder as it is now as what each of <paramref name="channels"/> has. Every channel is tried; the ones that could not be written come back with the error, so
        /// a failure on one never hides the other.
        /// </summary>
        public static List<(PublishChannels Channel, Exception Error)> Apply(ModInfo mod, PublishChannels channels, string contentFolder, string dataRoot = null)
        {
            List<(PublishChannels Channel, Exception Error)> failures = new List<(PublishChannels Channel, Exception Error)>();
            if (channels.HasFlag(PublishChannels.Steam))
            {
                try { PublishedState.MarkCurrent(ModDetails.WorkshopId(mod), contentFolder, dataRoot); }
                catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException || ex is ArgumentException)
                {
                    failures.Add((PublishChannels.Steam, ex));
                }
            }
            if (channels.HasFlag(PublishChannels.GitHub))
            {
                try { PublishedState.MarkCurrentGitHub(mod.Uid, contentFolder, dataRoot); }
                catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException || ex is ArgumentException)
                {
                    failures.Add((PublishChannels.GitHub, ex));
                }
            }
            return failures;
        }
    }
}
