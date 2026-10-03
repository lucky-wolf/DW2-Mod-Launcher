using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Media;
using DW2ModLauncher.Core.Diagnostics;
using DW2ModLauncher.Core.Services;

namespace DW2ModLauncher.Avalonia.Views
{
    /// <summary>Simple modal message box. Closes with true for the first button, false for the second or dismissal.</summary>
    public partial class MessageDialog : Window
    {
        public MessageDialog()
        {
            InitializeComponent();
        }

        // "check" icon, Material Design Icons, Apache-2.0: shown briefly after a copy as confirmation.
        private static readonly Geometry CheckIcon = Geometry.Parse("M9 16.17L4.83 12l-1.42 1.41L9 19 21 7l-1.41-1.41z");

        public MessageDialog(string message, string title, string yesText, string noText, string copyText = "Copy to clipboard", string openLogText = "Open log") : this()
        {
            Title = title;
            MessageText.Text = message;
            ToolTip.SetTip(CopyButton, copyText);
            CopyButton.Click += async delegate { await CopyMessageAsync(message); };
            // Only offered once there is a log to open (errors write to it).
            ToolTip.SetTip(OpenLogButton, openLogText);
            OpenLogButton.IsVisible = File.Exists(Logger.CrashLogPath);
            OpenLogButton.Click += delegate { OpenLog(); };
            YesButton.Content = yesText;
            YesButton.Click += delegate { Close(true); };
            if (noText == null) { NoButton.IsVisible = false; YesButton.IsCancel = true; }
            else
            {
                NoButton.Content = noText;
                NoButton.Click += delegate { Close(false); };
            }
        }

        private void OpenLog()
        {
            try { PlatformShell.Create().OpenFile(Logger.CrashLogPath); }
            catch (Exception)
            {
                // No handler for .log files: nothing useful to say in an already-open dialog.
            }
        }

        private async Task CopyMessageAsync(string message)
        {
            try
            {
                var clipboard = GetTopLevel(this)?.Clipboard;
                if (clipboard == null) return;
                await clipboard.SetTextAsync(message);
                Geometry copyIcon = CopyIcon.Data;
                CopyIcon.Data = CheckIcon;
                await Task.Delay(1500);
                CopyIcon.Data = copyIcon;
            }
            catch (Exception)
            {
                // The clipboard can be unavailable (e.g. no display server on Linux); copying is a convenience, not worth an error dialog on an error dialog.
            }
        }
    }
}
