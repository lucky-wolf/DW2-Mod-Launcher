using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using DW2ModLauncher.Core.Diagnostics;
using DW2ModLauncher.Core.Models;
using DW2ModLauncher.Avalonia.Services;
using DW2ModLauncher.Avalonia.ViewModels;
using DW2ModLauncher.Core.Services;

namespace DW2ModLauncher.Avalonia
{
    public partial class MainWindow : Window
    {
        /// <summary>Smallest on-screen area (device pixels, each axis) a restored window must keep.</summary>
        private const int MinVisible = 200;

        public MainWindow()
        {
            InitializeComponent();
            Title = "DW2 Mod Launcher v" + DW2ModLauncher.Core.AppVersion.Display;
            MainViewModel main = new MainViewModel(new DialogService(() => this), UserDataRoot.GetLauncherDataRoot(System.AppContext.BaseDirectory));
            DataContext = main;
            RestorePosition(main);
            // The online Workshop check waits until the window is up so a Steam or network problem can't break startup.
            Opened += async delegate
            {
                main.CleanUpFinishedUpdate();
                await main.BeginWorkshopUpdateCheck(false);
                await main.CheckForLauncherUpdateAsync(false);
            };
            // Closing can't await, so cancel it, ask, and close again once the user has decided.
            bool exitConfirmed = false;
            Closing += async (sender, e) =>
            {
                if (exitConfirmed) return;
                e.Cancel = true;
                if (!await main.Settings.ConfirmSaveForExitAsync()) return;
                exitConfirmed = true;
                SavePosition(main);
                Close();
            };
        }

        /// <summary>Puts the window back where it last closed, unless that spot is no longer on any screen.</summary>
        private void RestorePosition(MainViewModel main)
        {
            LauncherSettings s = main.LauncherSettings;
            if (s.WindowX == null || s.WindowY == null) { Logger.Log("Window", "No saved position."); return; }
            PixelPoint p = new PixelPoint(s.WindowX.Value, s.WindowY.Value);
            if (Screens == null) { Logger.Log("Window", "Screens unavailable; not restoring."); return; }
            // Saved size (DIPs), clamped to the window's minimum; falls back to the XAML size.
            double w = Math.Max(MinWidth, s.WindowWidth ?? Width);
            double h = Math.Max(MinHeight, s.WindowHeight ?? Height);
            // Require at least MinVisible x MinVisible of the window to overlap one screen's working area,
            // so it can never be restored off screen or as an unreachable sliver.
            bool visible = Screens.All.Any(sc =>
            {
                PixelRect window = new PixelRect(p, new PixelSize((int)(w * sc.Scaling), (int)(h * sc.Scaling)));
                PixelRect overlap = window.Intersect(sc.WorkingArea);
                return overlap.Width >= MinVisible && overlap.Height >= MinVisible;
            });
            if (!visible)
            {
                Logger.Log("Window", "Saved " + p + " " + w + "x" + h + " not visible on screens: "
                    + string.Join("; ", Screens.All.Select(sc => sc.WorkingArea + " @" + sc.Scaling)));
                return;
            }
            Logger.Log("Window", "Restoring " + p + " " + w + "x" + h);
            Width = w;
            Height = h;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = p;
        }

        private void SavePosition(MainViewModel main)
        {
            // A maximized or minimized window reports a position that isn't worth restoring.
            if (WindowState != WindowState.Normal) { Logger.Log("Window", "State " + WindowState + "; position not saved."); main.SaveSettings(); return; }
            Logger.Log("Window", "Saving " + Position + " " + ClientSize.Width + "x" + ClientSize.Height);
            main.LauncherSettings.WindowX = Position.X;
            main.LauncherSettings.WindowY = Position.Y;
            main.LauncherSettings.WindowWidth = ClientSize.Width;
            main.LauncherSettings.WindowHeight = ClientSize.Height;
            main.SaveSettings();
        }
    }
}
