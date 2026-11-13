using System;
using System.Collections.Generic;
using System.IO;
using DW2ModLauncher.Core.Models;

namespace DW2ModLauncher.Core.Services.Publishing
{
    public enum ReleaseOutcome
    {
        /// <summary>A new release was made.</summary>
        Created,
        /// <summary>The tag was already released with exactly these files; nothing was done.</summary>
        AlreadyCurrent,
        /// <summary>The tag was released with other files; the old release and tag were deleted and a new one made.</summary>
        Replaced,
        /// <summary>The tag was released with other files and the player chose not to replace it.</summary>
        Declined
    }

    public class ReleaseResult
    {
        public ReleaseOutcome Outcome { get; set; }
        public string Tag { get; set; }
        public string Repo { get; set; }
        /// <summary>Things the author should read that did not stop the release.</summary>
        public List<string> Warnings { get; } = new List<string>();
    }

    /// <summary>
    /// Makes a mod's GitHub release from its folder as it is now (docs/plans/mod-catalog.md, section 2). Failures throw
    /// <see cref="ReleaseHostException"/> (gh's own message) or <see cref="InvalidDataException"/> (the catalog would reject the mod).
    /// </summary>
    public static class ReleasePublisher
    {
        /// <param name="confirmReplace">Called with the differences when the tag is already released with other files; true replaces the release (deleting it resets its download count).</param>
        public static ReleaseResult Publish(GitHubReleaseHost host, string repo, string contentFolder, string workFolder, ModPublishMetadata metadata, LauncherMeta meta, string workshopId, DateTime publishedUtc, Func<string, IList<string>, bool> confirmReplace)
        {
            if (!GitHubReleaseHost.IsRepo(repo)) throw new ReleaseHostException("The release repository \"" + repo + "\" is not written OWNER/NAME.");
            host.CheckReady();
            ReleasePackage package = ReleasePackager.Package(contentFolder, Path.Combine(workFolder, "Release"), metadata, meta, workshopId, publishedUtc);
            ReleaseResult result = new ReleaseResult { Tag = package.Tag, Repo = repo };
            result.Warnings.AddRange(package.Warnings);
            if (!host.HasCatalogTopic(repo))
                result.Warnings.Add(repo + " does not have the topic " + GitHubReleaseHost.CatalogTopic + ", so the mod catalog will not find these releases unless the catalog's owner lists the repository by hand.");

            if (!host.ReleaseExists(repo, package.Tag))
            {
                host.Create(repo, package);
                result.Outcome = ReleaseOutcome.Created;
                return result;
            }
            string released = host.DownloadZip(repo, package.Tag, Path.Combine(workFolder, "Released"));
            List<string> differences = ReleasePackager.Differences(released, contentFolder);
            if (differences.Count == 0)
            {
                result.Outcome = ReleaseOutcome.AlreadyCurrent;
                return result;
            }
            if (!confirmReplace(package.Tag, differences))
            {
                result.Outcome = ReleaseOutcome.Declined;
                return result;
            }
            // The new release is fully built (above) before the old one goes.
            host.Delete(repo, package.Tag);
            host.Create(repo, package);
            result.Outcome = ReleaseOutcome.Replaced;
            return result;
        }
    }
}
