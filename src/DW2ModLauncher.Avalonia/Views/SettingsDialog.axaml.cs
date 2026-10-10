using Avalonia.Controls;
using DW2ModLauncher.Avalonia.ViewModels;

namespace DW2ModLauncher.Avalonia.Views
{
    /// <summary>Hosts the settings page in its own modal window. OK validates and saves; anything else that closes it discards the edits.</summary>
    public partial class SettingsDialog : Window
    {
        private bool accepted;

        public SettingsDialog()
        {
            InitializeComponent();
            OkButton.Click += async delegate
            {
                if (DataContext is SettingsViewModel settings && !await settings.TrySaveAsync()) return;
                accepted = true;
                Close();
            };
            CancelButton.Click += delegate { Close(); };
        }

        public SettingsDialog(SettingsViewModel settings) : this()
        {
            DataContext = settings;
        }

        protected override void OnClosed(System.EventArgs e)
        {
            base.OnClosed(e);
            if (!accepted && DataContext is SettingsViewModel settings) settings.DiscardPathEdits();
        }
    }
}
