using Avalonia.Controls;
using DW2ModLauncher.Avalonia.ViewModels;

namespace DW2ModLauncher.Avalonia.Views
{
    public partial class ModSettingsDialog : Window
    {
        public ModSettingsDialog()
        {
            InitializeComponent();
        }

        public ModSettingsDialog(ModSettingsEditorViewModel editor) : this()
        {
            DataContext = editor;
            SaveButton.Click += delegate { Close(true); };
            CancelButton.Click += delegate { Close(false); };
        }
    }
}
