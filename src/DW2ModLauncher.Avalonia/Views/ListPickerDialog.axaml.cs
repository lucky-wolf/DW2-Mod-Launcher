using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;

namespace DW2ModLauncher.Avalonia.Views
{
    /// <summary>Pick one string from a list; double-click or Enter chooses it. Closes with null if dismissed.</summary>
    public partial class ListPickerDialog : Window
    {
        public ListPickerDialog()
        {
            InitializeComponent();
        }

        public ListPickerDialog(string title, string note, IList<string> items) : this()
        {
            Title = title;
            NoteText.Text = note;
            Items.ItemsSource = items;
            Items.DoubleTapped += delegate { Choose(); };
            Items.KeyDown += (s, e) => { if (e.Key == Key.Enter) Choose(); };
        }

        private void Choose()
        {
            if (Items.SelectedItem is string chosen) Close(chosen);
        }
    }
}
