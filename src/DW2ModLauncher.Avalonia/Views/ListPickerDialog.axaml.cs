using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;

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

        public ListPickerDialog(string title, string note, IList<string> items, string okText, string cancelText, bool multiple = false, ISet<string> cautionItems = null, string cautionTooltip = null) : this()
        {
            // items in cautionItems carry a caution flag before the text
            if (cautionItems != null && cautionItems.Count > 0)
                Items.ItemTemplate = new FuncDataTemplate<string>((text, _) =>
                {
                    StackPanel row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                    bool flagged = text != null && cautionItems.Contains(text);
                    row.Children.Add(new PathIcon
                    {
                        Width = 14,
                        Height = 14,
                        Opacity = flagged ? 1 : 0,
                        Foreground = Brushes.Orange,
                        // "alert" icon, Material Design Icons, Apache-2.0
                        Data = Geometry.Parse("M13 14h-2V9h2m0 9h-2v-2h2M1 21h22L12 2 1 21z")
                    });
                    row.Children.Add(new TextBlock { Text = text });
                    if (flagged && !string.IsNullOrEmpty(cautionTooltip)) ToolTip.SetTip(row, cautionTooltip);
                    return row;
                }, true);
            Title = title;
            NoteText.Text = note;
            OkButton.Content = okText;
            CancelButton.Content = cancelText;
            Items.ItemsSource = items;
            this.multiple = multiple;
            if (multiple) Items.SelectionMode = SelectionMode.Multiple | SelectionMode.Toggle;
            Items.SelectionChanged += delegate { OkButton.IsEnabled = Items.SelectedItems != null && Items.SelectedItems.Count > 0; };
            if (!multiple) Items.DoubleTapped += delegate { Choose(); };
            OkButton.Click += delegate { Choose(); };
            CancelButton.Click += delegate { Close(null); };
        }

        private readonly bool multiple;

        // One item closes with that string; with several allowed it closes with the list of the selected strings.
        private void Choose()
        {
            if (multiple)
            {
                List<string> chosen = new List<string>();
                foreach (object item in Items.SelectedItems) if (item is string text) chosen.Add(text);
                if (chosen.Count > 0) Close(chosen);
            }
            else if (Items.SelectedItem is string one) Close(one);
        }
    }
}
