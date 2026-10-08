using System;
using System.Threading.Tasks;
using Avalonia;
using DW2ModLauncher.Core.Diagnostics;

namespace DW2ModLauncher.Avalonia
{
    internal static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            // Helper mode: do the Steam call and exit, never showing a window.
            if (args.Length > 0 && args[0] == DW2ModLauncher.Core.Services.Publishing.SteamWorker.Flag)
            {
                Environment.Exit(DW2ModLauncher.Core.Services.Publishing.SteamWorker.Run());
                return;
            }
            AppDomain.CurrentDomain.UnhandledException += delegate (object sender, UnhandledExceptionEventArgs e)
            {
                Logger.LogException("Unhandled domain exception", e.ExceptionObject as Exception);
            };
            TaskScheduler.UnobservedTaskException += delegate (object sender, UnobservedTaskExceptionEventArgs e)
            {
                Logger.LogException("Unobserved task exception", e.Exception);
                e.SetObserved();
            };
            try
            {
                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            }
            catch (Exception ex)
            {
                Logger.LogException("Fatal startup", ex);
                throw;
            }
        }

        public static AppBuilder BuildAvaloniaApp()
        {
            return AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
        }
    }
}
