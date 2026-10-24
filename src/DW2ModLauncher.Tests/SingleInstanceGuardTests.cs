using System;
using System.IO;
using System.Threading;
using DW2ModLauncher.Core.Services;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class SingleInstanceGuardTests : IDisposable
    {
        readonly string dir = Path.Combine(Path.GetTempPath(), "dw2ml-sig-" + Guid.NewGuid().ToString("N"));
        readonly string pipe = "dw2ml-test-" + Guid.NewGuid().ToString("N");

        public SingleInstanceGuardTests()
        {
            Directory.CreateDirectory(dir);
        }

        public void Dispose()
        {
            Directory.Delete(dir, true);
        }

        [Fact]
        public void SecondGuardOnSameLockIsRefusedUntilFirstIsDisposed()
        {
            string path = Path.Combine(dir, "launcher.lock");
            SingleInstanceGuard first = new SingleInstanceGuard(path, pipe);
            SingleInstanceGuard second = new SingleInstanceGuard(path, pipe);
            Assert.True(first.TryAcquire());
            Assert.False(second.TryAcquire());
            first.Dispose();
            Assert.True(second.TryAcquire());
            second.Dispose();
        }

        [Fact]
        public void SignalReachesTheFirstInstance()
        {
            string path = Path.Combine(dir, "launcher.lock");
            using (SingleInstanceGuard first = new SingleInstanceGuard(path, pipe))
            using (ManualResetEventSlim got = new ManualResetEventSlim())
            {
                Assert.True(first.TryAcquire());
                first.Activated += delegate { got.Set(); };
                first.StartListening();
                using (SingleInstanceGuard second = new SingleInstanceGuard(path, pipe))
                {
                    Assert.False(second.TryAcquire());
                    second.SignalExisting();
                }
                Assert.True(got.Wait(5000));
            }
        }
    }
}
