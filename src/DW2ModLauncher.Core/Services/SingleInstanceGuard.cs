using System;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DW2ModLauncher.Core.Diagnostics;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>
    /// Lets only one launcher run per user. The first instance holds an exclusive lock file (the OS drops it
    /// when the process dies, so a crash leaves nothing stale) and listens on a named pipe; a second launch
    /// connects to that pipe to ask the first to come to the front, then exits. Works on Windows, Linux
    /// and under Wine/Proton, none of which share named mutexes reliably.
    /// </summary>
    public sealed class SingleInstanceGuard : IDisposable
    {
        const string ActivateMessage = "activate";

        readonly string lockPath;
        readonly string pipeName;
        readonly CancellationTokenSource stop = new CancellationTokenSource();
        FileStream lockFile;

        /// <summary>Raised on a background thread when another launch asked this instance to come to the front.</summary>
        public event Action Activated;

        public SingleInstanceGuard(string lockPath, string pipeName)
        {
            this.lockPath = lockPath;
            this.pipeName = pipeName;
        }

        /// <summary>The guard for the real launcher: one per OS user, lock file in the launcher's data folder.</summary>
        public static SingleInstanceGuard ForCurrentUser()
        {
            return new SingleInstanceGuard(Path.Combine(UserDataRoot.Get(), "launcher.lock"), "DW2ModLauncher-" + Environment.UserName);
        }

        /// <summary>True if this process is now the only launcher; false if another one holds the lock. Other failures (permissions, ...) throw.</summary>
        public bool TryAcquire()
        {
            try
            {
                lockFile = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                return true;
            }
            catch (IOException)
            {
                // The only IOException a plain open of an existing-or-new file gives is "in use by another process".
                return false;
            }
        }

        /// <summary>Starts answering other launches. Call once, after <see cref="TryAcquire"/> succeeded.</summary>
        public void StartListening()
        {
            Task.Run(Listen);
        }

        async Task Listen()
        {
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    using (NamedPipeServerStream server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
                    {
                        await server.WaitForConnectionAsync(stop.Token).ConfigureAwait(false);
                        // Tell the client our process id first, so it can pass foreground rights to us before we raise the window.
                        server.Write(BitConverter.GetBytes(Environment.ProcessId), 0, 4);
                        server.Flush();
                        using (StreamReader reader = new StreamReader(server, Encoding.UTF8, false, 256, true))
                        {
                            string line = await reader.ReadLineAsync(stop.Token).ConfigureAwait(false);
                            if (line == ActivateMessage)
                            {
                                Action handler = Activated;
                                if (handler != null) handler();
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Logger.LogException("Single-instance pipe stopped; a second launch can no longer raise this window", ex);
            }
        }

        /// <summary>Asks the running launcher to come to the front. Throws if it does not answer.</summary>
        public void SignalExisting()
        {
            using (NamedPipeClientStream client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut))
            {
                client.Connect(5000);
                byte[] pid = new byte[4];
                int read = 0;
                while (read < 4)
                {
                    int n = client.Read(pid, read, 4 - read);
                    if (n == 0) throw new IOException("The running launcher closed the connection before answering.");
                    read += n;
                }
                if (OperatingSystem.IsWindows()) AllowSetForegroundWindow(BitConverter.ToInt32(pid, 0));
                byte[] msg = Encoding.UTF8.GetBytes(ActivateMessage + "\n");
                client.Write(msg, 0, msg.Length);
                client.Flush();
            }
        }

        public void Dispose()
        {
            stop.Cancel();
            if (lockFile != null) lockFile.Dispose();
            lockFile = null;
        }

        [DllImport("user32.dll")]
        static extern bool AllowSetForegroundWindow(int processId);
    }
}
