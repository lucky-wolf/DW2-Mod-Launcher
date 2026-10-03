using System.IO;
using System.Reflection;
using Avalonia.Controls;
using DW2ModLauncher.Avalonia.Services;
using DW2ModLauncher.Avalonia.ViewModels;
using DW2ModLauncher.Core.Services;

namespace DW2ModLauncher.Avalonia
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            Title = "DW2 Mod Launcher v" + DW2ModLauncher.Core.AppVersion.Display;
            MainViewModel main = new MainViewModel(new DialogService(() => this), UserDataRoot.GetLauncherDataRoot(System.AppContext.BaseDirectory));
            DataContext = main;
            // The online Workshop check waits until the window is up so a Steam or network problem can't break startup.
            Opened += async delegate { await main.BeginWorkshopUpdateCheck(false); };
            // Closing can't await, so cancel it, ask, and close again once the user has decided.
            bool exitConfirmed = false;
            Closing += async (sender, e) =>
            {
                if (exitConfirmed) return;
                e.Cancel = true;
                if (!await main.Settings.ConfirmSaveForExitAsync()) return;
                exitConfirmed = true;
                Close();
            };
        }
    }
}
