using System.IO;
using System.Reflection;
using Avalonia.Controls;
using DW2ModLauncher.Avalonia.Services;
using DW2ModLauncher.Avalonia.ViewModels;

namespace DW2ModLauncher.Avalonia
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            Title = "DW2 Mod Launcher v" + DW2ModLauncher.Core.AppVersion.Display;
            MainViewModel main = new MainViewModel(new DialogService(() => this), System.AppContext.BaseDirectory);
            DataContext = main;
            // The online Workshop check waits until the window is up so a Steam or network problem can't break startup.
            Opened += async delegate { await main.BeginWorkshopUpdateCheck(false); };
        }
    }
}
