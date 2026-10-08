using System;
using System.IO;
using DW2ModLauncher.Core.Services.Publishing;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class BundleSetTests
    {
        private static void Write(string dir, string name, int bytes) { File.WriteAllBytes(Path.Combine(dir, name), new byte[bytes]); }

        [Fact]
        public void SumsHeadAndHashedChildrenOnly()
        {
            string dir = Path.Combine(Path.GetTempPath(), "bundleset-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                Write(dir, "XL.bundle", 10);
                Write(dir, "XL.aaaa.bundle", 100);
                Write(dir, "XL.bbbb.bundle", 1000);
                Write(dir, "XLAssets.bundle", 5);
                Write(dir, "XLAssets.cccc.bundle", 50);
                Assert.Equal(1110, BundleSet.TotalBytes(dir, "XL.bundle"));
                Assert.Equal(55, BundleSet.TotalBytes(dir, "XLAssets.bundle"));
                Assert.Null(BundleSet.TotalBytes(dir, "Nope.bundle"));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void DetectFindsHeadsNotParts()
        {
            string dir = Path.Combine(Path.GetTempPath(), "bundledetect-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                Write(dir, "XL.bundle", 1);
                Write(dir, "XL.aaaa.bundle", 1);
                Write(dir, "XLAssets.bundle", 1);
                Write(dir, "XLAssets.bbbb.bundle", 1);
                Write(dir, "XLAssets.cccc.bundle", 1);
                Assert.Equal(new[] { "XL.bundle", "XLAssets.bundle" }, BundleSet.Detect(dir));
            }
            finally { Directory.Delete(dir, true); }
        }
    }
}
