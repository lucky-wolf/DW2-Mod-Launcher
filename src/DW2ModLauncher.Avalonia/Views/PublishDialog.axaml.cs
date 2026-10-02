using Avalonia.Controls;
using DW2ModLauncher.Avalonia.Services;
using DW2ModLauncher.Avalonia.ViewModels;

namespace DW2ModLauncher.Avalonia.Views
{
    public partial class PublishDialog : Window
    {
        public PublishDialog()
        {
            InitializeComponent();
        }

        public PublishDialog(PublishDialogViewModel editor, IDialogService dialogs) : this()
        {
            DataContext = editor;
            CancelButton.Click += delegate { Close(false); };
            PublishButton.Click += async delegate
            {
                // Saving mod.json is part of publishing: a failed write keeps the dialog open.
                string error = editor.Commit();
                if (error != null) await new MessageDialog(error, Title, editor.L["OK"], null).ShowDialog<bool>(this);
                else Close(true);
            };
        }
    }
}
