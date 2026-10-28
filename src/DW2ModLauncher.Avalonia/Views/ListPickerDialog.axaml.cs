using System.Collections.Generic;
using Avalonia.Controls;

namespace DW2ModLauncher.Avalonia.Views
{
    /// <summary>
    /// Pick one string from a list. A click selects it (a second click on the selected item clears it), OK or a double-click
    /// chooses it, Cancel or Esc dismisses. Closes with null if dismissed.
    /// </summary>
    public partial class ListPickerDialog : Window
    {
        public ListPickerDialog()
        {
            InitializeComponent();
        }

        public ListPickerDialog(string title, string note, IList<string> items, string okText, string cancelText) : this()
        {
            Title = title;
            NoteText.Text = note;
            OkButton.Content = okText;
            CancelButton.Content = cancelText;
            Items.ItemsSource = items;
            Items.SelectionChanged += delegate { OkButton.IsEnabled = Items.SelectedItem is string; };
            Items.DoubleTapped += delegate { Choose(); };
            OkButton.Click += delegate { Choose(); };
            CancelButton.Click += delegate { Close(null); };
        }

        private void Choose()
        {
            if (Items.SelectedItem is string chosen) Close(chosen);
        }
    }
}
