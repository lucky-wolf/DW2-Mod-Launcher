using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DW2ModLauncher.Core.Services.Updates;

namespace DW2ModLauncher.Core.Services.Catalog
{
    /// <summary>
    /// Keeps the catalog's preview images on disk so each is downloaded once. A preview's address names its release (the tag is in it),
    /// so a new release is a new address and a new file; nothing is ever refreshed in place.
    /// </summary>
    public static class CatalogPreviewCache
    {
        /// <summary>Previews are small (Steam's own limit is 1 MiB); anything bigger is not a preview and is refused, not trimmed.</summary>
        public const long MaxBytes = 8L * 1024 * 1024;

        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(4);

        /// <summary>Where the image for <paramref name="url"/> is (or will be) kept under <paramref name="cacheFolder"/>.</summary>
        public static string PathFor(string cacheFolder, string url)
        {
            byte[] hash;
            using (SHA256 sha = SHA256.Create()) hash = sha.ComputeHash(Encoding.UTF8.GetBytes(url));
            string name = BitConverter.ToString(hash, 0, 12).Replace("-", "").ToLowerInvariant();
            return Path.Combine(cacheFolder, name + Path.GetExtension(new Uri(url).AbsolutePath).ToLowerInvariant());
        }

        /// <summary>The local file holding the image, downloading it first when it is not cached. Throws on a network failure or an oversize image; the caller reports it.</summary>
        public static async Task<string> GetAsync(string url, string cacheFolder, CancellationToken token)
        {
            string path = PathFor(cacheFolder, url);
            if (File.Exists(path)) return path;
            await Gate.WaitAsync(token);
            try
            {
                if (File.Exists(path)) return path;
                Directory.CreateDirectory(cacheFolder);
                using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(30));
                    byte[] data = await UpdateChecker.Client.GetByteArrayAsync(url, timeout.Token);
                    if (data.LongLength > MaxBytes) throw new InvalidDataException("The preview image is " + data.LongLength + " bytes, over the " + MaxBytes + " byte limit.");
                    string partial = path + ".part";
                    File.WriteAllBytes(partial, data);
                    File.Move(partial, path, true);
                }
                return path;
            }
            finally { Gate.Release(); }
        }
    }
}
