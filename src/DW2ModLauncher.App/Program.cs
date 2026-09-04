using System;
using System.Net;
using System.Threading;
using System.Windows.Forms;
using DW2ModLauncher.Core.Diagnostics;

namespace DW2ModLauncherBeta
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; } catch { }

            try
            {
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
                ShowException("起動中にエラーが発生しました。", ex);
            }
        }

        private static void Application_ThreadException(object sender, ThreadExceptionEventArgs e)
        {
            Exception ex = e == null ? null : e.Exception;
            Logger.LogException("UI thread", ex);
            ShowException("処理中にエラーが発生しました。ランチャーは可能な限り継続します。", ex);
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
                MessageBox.Show(message + detail + "\r\n\r\nログ: " + Logger.CrashLogPath,
                    "DW2 Mod Launcher BETA", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch { }
        }
    }
}
