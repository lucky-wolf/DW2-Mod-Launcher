using Avalonia.Controls;

namespace DW2ModLauncher.Avalonia.Views
{
    /// <summary>Simple modal message box. Closes with true for the first button, false for the second or dismissal.</summary>
    public partial class MessageDialog : Window
    {
        public MessageDialog()
        {
            InitializeComponent();
        }

        public MessageDialog(string message, string title, string yesText, string noText) : this()
        {
            Title = title;
            MessageText.Text = message;
            YesButton.Content = yesText;
            YesButton.Click += delegate { Close(true); };
            if (noText == null) NoButton.IsVisible = false;
            else
            {
                NoButton.Content = noText;
                NoButton.Click += delegate { Close(false); };
            }
        }
    }
}
