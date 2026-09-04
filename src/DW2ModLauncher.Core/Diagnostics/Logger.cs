using System;
using System.IO;
using System.Text;

namespace DW2ModLauncher.Core.Diagnostics
{
    public static class Logger
    {
        public static string CrashLogPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DW2ModLauncher_BETA.log"); }
        }

        public static void LogException(string context, Exception ex)
        {
            try
            {
                StringBuilder b = new StringBuilder();
                b.AppendLine("============================================================");
                b.AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + (context ?? "Error"));
                if (ex == null)
                {
                    b.AppendLine("Unknown exception");
                }
                else
                {
                    b.AppendLine(ex.ToString());
                }
                File.AppendAllText(CrashLogPath, b.ToString(), new UTF8Encoding(true));
            }
            catch { }
        }
    }
}
