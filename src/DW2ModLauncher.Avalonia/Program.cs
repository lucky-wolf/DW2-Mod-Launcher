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
