using Avalonia.Controls;
using DW2ModLauncher.Avalonia.ViewModels;

namespace DW2ModLauncher.Avalonia.Views
{
    /// <summary>Hosts the settings page in its own modal window.</summary>
    public partial class SettingsDialog : Window
    {
        public SettingsDialog()
        {
            InitializeComponent();
            CloseButton.Click += delegate { Close(); };
        }

        public SettingsDialog(SettingsViewModel settings) : this()
        {
            DataContext = settings;
        }
    }
}
