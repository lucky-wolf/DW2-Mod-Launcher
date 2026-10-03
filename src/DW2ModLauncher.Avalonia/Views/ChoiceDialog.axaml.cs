using System.Collections.Generic;
using Avalonia.Controls;

namespace DW2ModLauncher.Avalonia.Views
{
    /// <summary>
    /// Small modal message with one button per choice (e.g. Save / Don't save / Cancel). Closes with the index of the
    /// clicked button, or null if dismissed (Esc triggers the last button, which is the cancel button).
    /// </summary>
    public partial class ChoiceDialog : Window
    {
        public ChoiceDialog()
        {
            InitializeComponent();
        }

        public ChoiceDialog(string message, string title, IList<string> buttons) : this()
        {
            Title = title;
            MessageText.Text = message;
            for (int i = 0; i < buttons.Count; i++)
            {
                int index = i;
                Button button = new Button { Content = buttons[i], MinWidth = 100, IsDefault = i == 0, IsCancel = i == buttons.Count - 1 };
                button.Click += delegate { Close(index); };
                ButtonRow.Children.Add(button);
            }
        }
    }
}
