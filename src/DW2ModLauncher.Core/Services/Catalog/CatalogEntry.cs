using System;
using System.Collections.Generic;

namespace DW2ModLauncher.Core.Services.Catalog
{
    /// <summary>A mod another mod names in its manifest.</summary>
    public class CatalogDependency
    {
        public string Mod { get; set; }
        /// <summary>The other mod's identity in canonical form, or null when the author gave none (then the Workshop id or name is all there is).</summary>
        public string Uid { get; set; }
        /// <summary>Null when the dependency names no Workshop item.</summary>
        public string WorkshopId { get; set; }
        public bool Required { get; set; }
    }

    /// <summary>The newest release of one mod, as listed in the catalog's index.json.</summary>
    public class CatalogEntry
    {
        /// <summary>A short label for the release (used in file names); not the mod's identity, see <see cref="Uid"/>.</summary>
        public string Id { get; set; }
        /// <summary>The mod's identity, the UUID in its dw2modlauncher.json "uid". Always present: the catalog lists no mod without one.</summary>
        public string Uid { get; set; }
        public string Name { get; set; }
        /// <summary>What the mod holds, at least one of "code" (a .dll), "xml" (data or patches), "translation" or "font". Anything can contain anything, so a mod may have several.</summary>
        public List<string> Kinds { get; } = new List<string>();
        /// <summary>Topics the author gave the mod (music, art, icons ...), written the way <see cref="CatalogTags"/> says. May be empty.</summary>
        public List<string> Tags { get; } = new List<string>();
        public string Version { get; set; }
        /// <summary>Who made the mod. Always present: the catalog lists no mod without an author.</summary>
        public string Author { get; set; }
        public string ShortDescription { get; set; }
        /// <summary>The mod's Steam Workshop item, or null when it has none.</summary>
        public string WorkshopId { get; set; }
        public string MinLauncherVersion { get; set; }
        public List<CatalogDependency> Dependencies { get; } = new List<CatalogDependency>();
        public long Size { get; set; }
        /// <summary>Lower-case hex SHA-256 of the zip.</summary>
        public string Sha256 { get; set; }
        public string DownloadUrl { get; set; }
        public string ReleaseUrl { get; set; }
        /// <summary>The mod's preview image (a separate release file, so listing needs no zip), or null when the release has none.</summary>
        public string PreviewUrl { get; set; }
        public DateTime PublishedUtc { get; set; }
        /// <summary>Total downloads of every release of this mod, or null where the host publishes no counts (GitLab).</summary>
        public long? Downloads { get; set; }
        /// <summary>The most downloads any one release of this mod has had, or null where the host publishes no counts. Nearly everyone downloads the version current at the time, so this tracks people better than the total, which counts every update again.</summary>
        public long? PeakDownloads { get; set; }
    }

    /// <summary>A parsed index.json.</summary>
    public class CatalogIndex
    {
        public DateTime GeneratedUtc { get; set; }
        public List<CatalogEntry> Entries { get; } = new List<CatalogEntry>();
    }
}
