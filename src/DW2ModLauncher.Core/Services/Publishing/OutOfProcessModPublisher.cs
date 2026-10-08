using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using DW2ModLauncher.Core.Diagnostics;

namespace DW2ModLauncher.Core.Services.Publishing
{
    /// <summary>
    /// Runs every Steam call in a short-lived copy of the launcher (<see cref="SteamWorker"/>), so DW2 only looks
    /// "running" to Steam for the duration of the call and never for the life of the launcher.
    /// </summary>
    public class OutOfProcessModPublisher : IModPublisher
    {
        private static readonly TimeSpan CancelGrace = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan WorkerLimit = TimeSpan.FromMinutes(35);

        private readonly uint appId;

        public OutOfProcessModPublisher(uint appId)
        {
            this.appId = appId;
        }

        public ModPublishResult Publish(ModPublishRequest request)
        {
            SteamWorkerReply reply = Call(new SteamWorkerCall { Op = "publish", AppId = appId, Request = request }, request.Cancel, out string error);
            if (reply?.Publish != null) return reply.Publish;
            return new ModPublishResult { ErrorMessage = error ?? "The Steam helper returned no result." };
        }

        public ModVisibility? GetVisibility(long workshopId)
        {
            return Call(new SteamWorkerCall { Op = "visibility", AppId = appId, WorkshopId = workshopId }, CancellationToken.None, out _)?.Visibility;
        }

        public List<long> FindDeletedItems(IReadOnlyList<long> workshopIds)
        {
            // A failed worker must never read as "everything was deleted".
            return Call(new SteamWorkerCall { Op = "deleted", AppId = appId, Ids = new List<long>(workshopIds) }, CancellationToken.None, out _)?.Deleted ?? new List<long>();
        }

        private static SteamWorkerReply Call(SteamWorkerCall call, CancellationToken cancel, out string error)
        {
            error = null;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo { FileName = Environment.ProcessPath, UseShellExecute = false, CreateNoWindow = true };
                // Started with "dotnet DW2ModLauncher.dll" (development): the dll has to be passed along.
                if (string.Equals(Path.GetFileNameWithoutExtension(psi.FileName), "dotnet", StringComparison.OrdinalIgnoreCase))
                    psi.ArgumentList.Add(System.Reflection.Assembly.GetEntryAssembly().Location);
                psi.ArgumentList.Add(SteamWorker.Flag);
                psi.WorkingDirectory = AppContext.BaseDirectory;
                psi.RedirectStandardInput = true;
                psi.RedirectStandardOutput = true;
                psi.StandardInputEncoding = new UTF8Encoding(false);
                psi.StandardOutputEncoding = new UTF8Encoding(false);

                using (Process worker = Process.Start(psi))
                {
                    worker.StandardInput.WriteLine(JsonSerializer.Serialize(call));
                    worker.StandardInput.Flush();

                    // Cancelling asks the worker to stop waiting on Steam and report what it has (e.g. an already-created item's id).
                    using (cancel.Register(delegate ()
                    {
                        try { worker.StandardInput.WriteLine("cancel"); worker.StandardInput.Flush(); } catch { }
                        if (!worker.WaitForExit((int)CancelGrace.TotalMilliseconds)) { try { worker.Kill(true); } catch { } }
                    }))
                    {
                        string result = null;
                        string line;
                        while ((line = worker.StandardOutput.ReadLine()) != null)
                            if (line.StartsWith(SteamWorker.ResultPrefix, StringComparison.Ordinal)) result = line.Substring(SteamWorker.ResultPrefix.Length);
                        if (!worker.WaitForExit((int)WorkerLimit.TotalMilliseconds)) { try { worker.Kill(true); } catch { } }
                        if (result == null)
                        {
                            error = cancel.IsCancellationRequested ? "Cancelled." : "The Steam helper process ended without a result.";
                            return null;
                        }
                        return JsonSerializer.Deserialize<SteamWorkerReply>(result);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogException("Steam helper process", ex);
                error = ex.Message;
                return null;
            }
        }
    }
}
