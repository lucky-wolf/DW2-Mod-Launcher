using System;
using Avalonia.Controls;
using DW2ModLauncher.Avalonia.ViewModels;
using DW2ModLauncher.Core;
using DW2ModLauncher.Core.Diagnostics;
using DW2ModLauncher.Core.Services;

namespace DW2ModLauncher.Avalonia.Views
{
    /// <summary>The About box: version, what the launcher is, and a link to the GitHub releases page.</summary>
    public partial class AboutDialog : Window
    {
        public AboutDialog()
        {
            InitializeComponent();
            CloseButton.Click += delegate { Close(); };
            ReleasesButton.Click += delegate { OpenReleases(); };
        }

        public AboutDialog(MainViewModel main) : this()
        {
            DataContext = main;
            VersionText.Text = main.T("AboutVersion", AppVersion.Display);
            ReleasesLink.Text = AppVersion.ReleasesUrl;
        }

        private static void OpenReleases()
        {
            try { PlatformShell.Create().OpenUrl(AppVersion.ReleasesUrl); }
            catch (Exception ex) { Logger.Log("About", "Open releases page failed: " + ex.Message); }
        }
    }
}
