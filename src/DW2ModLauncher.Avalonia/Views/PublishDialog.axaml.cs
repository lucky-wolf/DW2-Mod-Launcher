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
            Closed += delegate { editor.Dispose(); };
            CancelButton.Click += delegate { Close(false); };
            PublishButton.Click += async delegate
            {
                // Saving rewrites mod.json's bundle list from the folder, so missing entries are about to be dropped: ask first.
                string question = editor.MissingBundlesQuestion;
                if (question != null && !await dialogs.ConfirmAsync(question, Title, editor.L["OK"], editor.L["Cancel"])) return;
                // Loose images other than the preview would be uploaded for no purpose: offer to clear them out, then carry on whatever the answer.
                await editor.OfferStrayImageCleanupAsync();
                // Saving mod.json is part of publishing: a failed write keeps the dialog open.
                string error = editor.Commit();
                if (error != null) await new MessageDialog(error, Title, editor.L["OK"], null).ShowDialog<bool>(this);
                else Close(true);
            };
        }
    }
}
