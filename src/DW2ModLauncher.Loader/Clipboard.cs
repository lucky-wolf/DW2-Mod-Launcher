using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace DW2ModLauncher.Loader
{
    // Puts text on the Windows clipboard with the Win32 calls directly: the loader is plain net8.0 (no Windows Forms), and these
    // work from any thread, unlike System.Windows.Forms.Clipboard which needs an STA one.
    internal static class Clipboard
    {
        const uint CfUnicodeText = 13;
        const uint GmemMoveable = 0x0002;

        [DllImport("user32.dll", SetLastError = true)] static extern bool OpenClipboard(IntPtr owner);
        [DllImport("user32.dll", SetLastError = true)] static extern bool CloseClipboard();
        [DllImport("user32.dll", SetLastError = true)] static extern bool EmptyClipboard();
        [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetClipboardData(uint format, IntPtr data);
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr GlobalLock(IntPtr mem);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool GlobalUnlock(IntPtr mem);
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr GlobalFree(IntPtr mem);

        public static bool SetText(string text)
        {
            try
            {
                // another program can hold the clipboard for a moment
                bool opened = false;
                for (int i = 0; i < 5 && !(opened = OpenClipboard(IntPtr.Zero)); i++) Thread.Sleep(10);
                if (!opened) return false;
                try
                {
                    EmptyClipboard();
                    int bytes = (text.Length + 1) * 2;
                    IntPtr mem = GlobalAlloc(GmemMoveable, (UIntPtr)bytes);
                    if (mem == IntPtr.Zero) return false;
                    IntPtr p = GlobalLock(mem);
                    if (p == IntPtr.Zero)
                    {
                        GlobalFree(mem);
                        return false;
                    }
                    Marshal.Copy((text + "\0").ToCharArray(), 0, p, text.Length + 1);
                    GlobalUnlock(mem);
                    if (SetClipboardData(CfUnicodeText, mem) == IntPtr.Zero)
                    {
                        GlobalFree(mem);
                        return false;
                    }
                    return true;
                }
                finally
                {
                    CloseClipboard();
                }
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
