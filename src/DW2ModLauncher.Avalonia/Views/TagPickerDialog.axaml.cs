using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using DW2ModLauncher.Core.Services.Catalog;

namespace DW2ModLauncher.Avalonia.Views
{
    /// <summary>The tag the author picked in the tag picker, and whether it is one nobody has used before (the caller confirms those).</summary>
    public class TagPick
    {
        public string Tag { get; set; }
        public bool IsNew { get; set; }
    }

    /// <summary>
    /// Picks a topic tag. Typing narrows the list of known tags to those containing the text (the best match first), and a tag that
    /// is not known is offered as an "add new tag" row. Closes with a <see cref="TagPick"/>, or null if cancelled.
    /// </summary>
    public partial class TagPickerDialog : Window
    {
        private sealed class Row
        {
            public string Tag;
            public bool IsNew;
            public string Text;
            public override string ToString() { return Text; }
        }

        private readonly IList<string> known;
        private readonly Func<string, string> newText;

        public TagPickerDialog()
        {
            InitializeComponent();
        }

        /// <param name="known">The tags already in use, normalized and without repeats.</param>
        /// <param name="newText">The text of the "add new tag" row for a typed tag.</param>
        public TagPickerDialog(string title, string note, string placeholder, IList<string> known, Func<string, string> newText, string okText, string cancelText) : this()
        {
            this.known = known;
            this.newText = newText;
            Title = title;
            NoteText.Text = note;
            SearchBox.PlaceholderText = placeholder;
            OkButton.Content = okText;
            CancelButton.Content = cancelText;
            Opened += delegate { SearchBox.Focus(); };
            SearchBox.TextChanged += delegate { Refresh(); };
            // the arrow keys move through the list while the cursor stays in the box
            SearchBox.KeyDown += (sender, e) =>
            {
                if (e.Key != Key.Down && e.Key != Key.Up || Matches.ItemCount == 0) return;
                int next = Matches.SelectedIndex + (e.Key == Key.Down ? 1 : -1);
                Matches.SelectedIndex = Math.Max(0, Math.Min(Matches.ItemCount - 1, next));
                e.Handled = true;
            };
            Matches.SelectionChanged += delegate { OkButton.IsEnabled = Matches.SelectedItem != null; };
            Matches.DoubleTapped += delegate { Choose(); };
            OkButton.Click += delegate { Choose(); };
            CancelButton.Click += delegate { Close(null); };
            Refresh();
        }

        private void Refresh()
        {
            string typed = CatalogTags.Normalize(SearchBox.Text);
            List<Row> rows = known
                .Where(k => typed.Length == 0 || k.Contains(typed, StringComparison.Ordinal))
                .OrderBy(k => k == typed ? 0 : k.StartsWith(typed, StringComparison.Ordinal) ? 1 : 2)
                .ThenBy(k => k, StringComparer.Ordinal)
                .Select(k => new Row { Tag = k, Text = k })
                .ToList();
            if (typed.Length > 0 && !known.Contains(typed)) rows.Add(new Row { Tag = typed, IsNew = true, Text = newText(typed) });
            Matches.ItemsSource = rows;
            Matches.SelectedIndex = rows.Count > 0 ? 0 : -1;
        }

        private void Choose()
        {
            if (Matches.SelectedItem is Row row) Close(new TagPick { Tag = row.Tag, IsNew = row.IsNew });
        }
    }
}
