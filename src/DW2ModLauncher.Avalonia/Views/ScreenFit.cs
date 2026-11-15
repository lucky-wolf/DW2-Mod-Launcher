using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;

namespace DW2ModLauncher.Avalonia.Views
{
    /// <summary>
    /// Keeps a dialog from being taller than the screen it opens on. Call <see cref="Attach"/> from the dialog's constructor;
    /// when the window opens it is capped to the monitor's working area (no taskbar), the content's scroll viewer takes over,
    /// and the window is moved back on screen if it now hangs off the top or bottom.
    /// </summary>
    public static class ScreenFit
    {
        /// <summary>Room for the title bar and window frame, which the window's own Height does not include.</summary>
        private const double FrameAllowance = 48;

        public static void Attach(Window window)
        {
            window.Opened += delegate { Fit(window); };
        }

        private static void Fit(Window window)
        {
            Screen screen = window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary;
            if (screen == null) throw new InvalidOperationException("No screen is available to fit " + window.GetType().Name + " to.");

            double available = screen.WorkingArea.Height / screen.Scaling - FrameAllowance;
            window.MaxHeight = available;
            if (window.Height > available) window.Height = available;

            // The cap changes the height after the window was centered over its owner, so once it has been laid out
            // again, bring it back inside the working area.
            Dispatcher.UIThread.Post(() =>
            {
                PixelRect area = screen.WorkingArea;
                int heightPx = (int)((window.FrameSize ?? window.ClientSize).Height * screen.Scaling);
                int y = window.Position.Y;
                if (y + heightPx > area.Bottom) y = area.Bottom - heightPx;
                if (y < area.Y) y = area.Y;
                if (y != window.Position.Y) window.Position = new PixelPoint(window.Position.X, y);
            }, DispatcherPriority.Loaded);
        }
    }
}
