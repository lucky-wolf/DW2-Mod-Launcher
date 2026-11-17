using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using DW2ModLauncher.Core.Services.Catalog;

namespace DW2ModLauncher.Avalonia.Views
{
    /// <summary>The tags left checked in the tag picker, and which of them nobody has used before (the caller confirms those).</summary>
    public class TagSelection
    {
        public List<string> Tags { get; set; } = new List<string>();
        public List<string> NewTags { get; set; } = new List<string>();
    }

    /// <summary>One line of the tag picker: a tag and whether it is checked.</summary>
    public class TagPickRow
    {
        public string Tag { get; set; }
        public string Text { get; set; }
        public bool IsNew { get; set; }
        public bool IsChecked { get; set; }
    }

    /// <summary>
    /// Picks a mod's topic tags in one go. Every known tag is a check box (the mod's own tags start checked); typing narrows the
    /// list, and the Add button (or Enter) adds the typed text as a new tag, already checked. Closes with a <see cref="TagSelection"/>,
    /// or null if cancelled.
    /// </summary>
    public partial class TagPickerDialog : Window
    {
        private readonly List<TagPickRow> all = new List<TagPickRow>();

        public TagPickerDialog()
        {
            InitializeComponent();
        }

        /// <param name="known">Every tag in use, normalized and without repeats.</param>
        /// <param name="selected">The tags the mod has now; they start checked (any not in <paramref name="known"/> are added to the list).</param>
        public TagPickerDialog(string title, string note, string placeholder, IList<string> known, IList<string> selected, string addText, string okText, string cancelText) : this()
        {
            foreach (string tag in known.Concat(selected).Distinct().OrderBy(t => t, StringComparer.Ordinal))
                all.Add(new TagPickRow { Tag = tag, Text = tag, IsNew = !known.Contains(tag), IsChecked = selected.Contains(tag) });
            Title = title;
            NoteText.Text = note;
            SearchBox.PlaceholderText = placeholder;
            AddButton.Content = addText;
            OkButton.Content = okText;
            CancelButton.Content = cancelText;
            Opened += delegate { SearchBox.Focus(); };
            SearchBox.TextChanged += delegate { Refresh(); };
            SearchBox.KeyDown += (sender, e) =>
            {
                if (e.Key != Key.Enter || CatalogTags.Normalize(SearchBox.Text).Length == 0) return;
                AddTyped();
                e.Handled = true;
            };
            AddButton.Click += delegate { AddTyped(); };
            OkButton.Click += delegate { Close(Result()); };
            CancelButton.Click += delegate { Close(null); };
            Refresh();
        }

        private void Refresh()
        {
            string typed = CatalogTags.Normalize(SearchBox.Text);
            Matches.ItemsSource = all
                .Where(r => typed.Length == 0 || r.Tag.Contains(typed, StringComparison.Ordinal))
                .OrderBy(r => r.Tag, StringComparer.Ordinal)
                .ToList();
            AddButton.IsEnabled = typed.Length > 0;
        }

        /// <summary>Checks the typed tag, adding it to the list first when it is new, then clears the box for the next one.</summary>
        private void AddTyped()
        {
            string typed = CatalogTags.Normalize(SearchBox.Text);
            if (typed.Length == 0) return;
            TagPickRow row = all.FirstOrDefault(r => r.Tag == typed);
            if (row == null)
            {
                row = new TagPickRow { Tag = typed, Text = typed, IsNew = true };
                all.Add(row);
            }
            row.IsChecked = true;
            SearchBox.Text = "";
            Refresh();
            Matches.ScrollIntoView(row);
        }

        private TagSelection Result()
        {
            List<TagPickRow> on = all.Where(r => r.IsChecked).ToList();
            return new TagSelection { Tags = on.Select(r => r.Tag).ToList(), NewTags = on.Where(r => r.IsNew).Select(r => r.Tag).ToList() };
        }
    }
}
