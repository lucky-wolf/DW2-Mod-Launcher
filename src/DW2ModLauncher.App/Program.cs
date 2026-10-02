using System;
using System.Net;
using System.Threading;
using System.Windows.Forms;
using DW2ModLauncher.Core.Diagnostics;

namespace DW2ModLauncher.App
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            try
            {
                Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += Application_ThreadException;
                AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                Logger.LogException("Fatal startup", ex);
                ShowException("An error occurred during startup.", ex);
            }
        }

        private static void Application_ThreadException(object sender, ThreadExceptionEventArgs e)
        {
            Exception ex = e == null ? null : e.Exception;
            Logger.LogException("UI thread", ex);
            ShowException("An error occurred. The launcher will continue if possible.", ex);
        }

        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Exception ex = e == null ? null : e.ExceptionObject as Exception;
            Logger.LogException("Unhandled domain exception", ex);
        }

        private static void ShowException(string message, Exception ex)
        {
            try
            {
                string detail = ex == null ? "" : ("\r\n\r\n" + ex.GetType().Name + ": " + ex.Message);
                MessageBox.Show(message + detail + "\r\n\r\nLog: " + Logger.CrashLogPath,
                    "DW2 Mod Launcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch { }
        }
    }
}
