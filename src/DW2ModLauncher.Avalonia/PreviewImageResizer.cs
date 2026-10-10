using System;
using System.IO;
using DW2ModLauncher.Core.Services.Publishing;
using SkiaSharp;

namespace DW2ModLauncher.Avalonia
{
    /// <summary>A preview image encoded to fit Steam's limit, held in memory until the author has chosen its name.</summary>
    public class FittedPreview
    {
        public byte[] Data { get; set; }
        /// <summary>".jpg" or ".png".</summary>
        public string Extension { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }

    /// <summary>
    /// Makes a copy of a preview image that fits Steam's 1 MiB limit (see <see cref="PreviewImagePlan"/>). The source is only read, and
    /// nothing is written until <see cref="Save"/>. An opaque image becomes a JPEG; one with real transparency stays a PNG (JPEG would
    /// have to flatten it), so it can only get smaller by losing pixels. Sizes are tried largest first, so an image that is only heavy
    /// because of its format keeps all of its pixels.
    /// </summary>
    public static class PreviewImageResizer
    {
        /// <summary>The fitted image, or null when the file can't be read or won't fit even at the smallest size.</summary>
        public static FittedPreview TryFit(string sourceFile)
        {
            try
            {
                using (SKBitmap original = SKBitmap.Decode(sourceFile))
                {
                    if (original == null || original.Width <= 0 || original.Height <= 0) return null;
                    // Decode into a known layout so the transparency scan below can read the alpha byte directly.
                    using (SKBitmap source = original.Copy(SKColorType.Bgra8888))
                    {
                        if (source == null) return null;
                        bool transparent = HasTransparency(source);
                        foreach (var size in PreviewImagePlan.Sizes(source.Width, source.Height))
                        {
                            byte[] data = Encode(source, size.Key, size.Value, transparent);
                            if (data == null || data.LongLength > PreviewImagePlan.TargetBytes) continue;
                            return new FittedPreview { Data = data, Extension = transparent ? ".png" : ".jpg", Width = size.Key, Height = size.Value };
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DW2ModLauncher.Core.Diagnostics.Logger.LogException("Resize preview image", ex);
            }
            return null;
        }

        /// <summary>Writes the fitted image into the mod folder under <paramref name="fileName"/> and returns its full path.</summary>
        public static string Save(FittedPreview fitted, string modFolder, string fileName)
        {
            string target = Path.Combine(modFolder, fileName);
            File.WriteAllBytes(target, fitted.Data);
            return target;
        }

        /// <summary>
        /// Replaces an image that is already in the mod: the original goes to the recycle bin, then the fitted image is saved under the
        /// original's name (its extension may change, e.g. a heavy PNG becomes a JPG). Returns the new full path. If the recycle fails
        /// nothing is written, so the original is never lost.
        /// </summary>
        public static string Replace(FittedPreview fitted, string original)
        {
            string target = Path.Combine(Path.GetDirectoryName(original), Path.GetFileNameWithoutExtension(original) + fitted.Extension);
            // Another file already holds the new name (a "x.jpg" next to a heavy "x.png"): refuse rather than overwrite it.
            if (!string.Equals(target, original, StringComparison.OrdinalIgnoreCase) && File.Exists(target))
                throw new IOException(Path.GetFileName(target) + " already exists next to " + Path.GetFileName(original) + ".");
            DW2ModLauncher.Core.Services.RecycleBin.Send(original);
            File.WriteAllBytes(target, fitted.Data);
            return target;
        }

        private static bool HasTransparency(SKBitmap bitmap)
        {
            ReadOnlySpan<byte> pixels = bitmap.GetPixelSpan();
            for (int i = 3; i < pixels.Length; i += 4)
                if (pixels[i] != 255) return true;
            return false;
        }

        private static byte[] Encode(SKBitmap source, int width, int height, bool transparent)
        {
            SKBitmap scaled = source;
            try
            {
                if (width != source.Width || height != source.Height)
                {
                    scaled = source.Resize(new SKImageInfo(width, height, SKColorType.Bgra8888, transparent ? SKAlphaType.Premul : SKAlphaType.Opaque),
                        new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
                    if (scaled == null) return null;
                }
                using (SKImage image = SKImage.FromBitmap(scaled))
                {
                    // A JPEG has to be good enough to look at but small; 88 keeps art clean, and the size steps do the rest.
                    using (SKData data = transparent ? image.Encode(SKEncodedImageFormat.Png, 100) : image.Encode(SKEncodedImageFormat.Jpeg, 88))
                        return data?.ToArray();
                }
            }
            finally
            {
                if (!ReferenceEquals(scaled, source)) scaled?.Dispose();
            }
        }
    }
}
