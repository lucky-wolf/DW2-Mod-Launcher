using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using DW2ModLauncher.Core.Diagnostics;

namespace DW2ModLauncher.Core.Services.Publishing
{
    /// <summary>One request from the launcher to the Steam worker process.</summary>
    internal class SteamWorkerCall
    {
        public string Op { get; set; }
        public uint AppId { get; set; }
        public ModPublishRequest Request { get; set; }
        public long WorkshopId { get; set; }
        public List<long> Ids { get; set; }
    }

    /// <summary>The worker's answer; only the field matching the call's Op is filled.</summary>
    internal class SteamWorkerReply
    {
        public ModPublishResult Publish { get; set; }
        public ModVisibility? Visibility { get; set; }
        public List<long> Deleted { get; set; }
    }

    /// <summary>
    /// The launcher re-runs itself with "--steam-worker" for every Steam API call (see <see cref="OutOfProcessModPublisher"/>).
    /// Initializing the Steam API under DW2's app id makes the Steam client think DW2 is running, and that state can outlive
    /// SteamAPI.Shutdown(); in a process of its own it ends for certain when the worker exits.
    /// Protocol: one JSON call on stdin, then optionally a "cancel" line; one "@@RESULT@@ {json}" line on stdout.
    /// </summary>
    public static class SteamWorker
    {
        public const string Flag = "--steam-worker";
        internal const string ResultPrefix = "@@RESULT@@ ";

        public static int Run()
        {
            Stream stdin = Console.OpenStandardInput();
            Stream stdout = Console.OpenStandardOutput();
            StreamReader input = new StreamReader(stdin, new UTF8Encoding(false));
            StreamWriter output = new StreamWriter(stdout, new UTF8Encoding(false)) { AutoFlush = true };
            SteamWorkerReply reply = new SteamWorkerReply();
            try
            {
                SteamWorkerCall call = JsonSerializer.Deserialize<SteamWorkerCall>(input.ReadLine());
                CancellationTokenSource cancel = new CancellationTokenSource();
                Thread watcher = new Thread(delegate ()
                {
                    try { if (input.ReadLine() == "cancel") cancel.Cancel(); } catch { }
                }) { IsBackground = true };
                watcher.Start();

                IModPublisher publisher = new SteamworksNetModPublisher(call.AppId);
                switch (call.Op)
                {
                    case "publish":
                        call.Request.Cancel = cancel.Token;
                        reply.Publish = publisher.Publish(call.Request);
                        break;
                    case "visibility":
                        reply.Visibility = publisher.GetVisibility(call.WorkshopId);
                        break;
                    case "deleted":
                        reply.Deleted = publisher.FindDeletedItems(call.Ids ?? new List<long>());
                        break;
                    default:
                        throw new InvalidOperationException("Unknown Steam worker operation: " + call.Op);
                }
            }
            catch (Exception ex)
            {
                Logger.LogException("Steam worker", ex);
                reply.Publish = new ModPublishResult { ErrorMessage = ex.Message };
                reply.Deleted = new List<long>();
            }
            output.WriteLine(ResultPrefix + JsonSerializer.Serialize(reply));
            return 0;
        }
    }
}
