using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace DW2ModLauncher.Avalonia.Views
{
    /// <summary>
    /// Modal single-line text prompt. Closes with the entered text, or null if cancelled. OK stays disabled while the text is blank.
    /// With <c>choices</c> the box is an editable drop-down: type a new value or pick an existing one.
    /// </summary>
    public partial class InputDialog : Window
    {
        public InputDialog()
        {
            InitializeComponent();
        }

        /// <param name="hint">Optional live note shown under the box (e.g. the folder name the text will become).</param>
        /// <param name="choices">Optional existing values offered in a drop-down; the text can still be typed freely.</param>
        /// <param name="isInvalid">Optional rule that disables OK for a typed value (e.g. a name that is already taken).</param>
        public InputDialog(string title, string label, string initial, string okText, string cancelText, Func<string, string> hint, IList<string> choices = null, Func<string, bool> isInvalid = null) : this()
        {
            Title = title;
            LabelText.Text = label;
            OkButton.Content = okText;
            CancelButton.Content = cancelText;
            bool picking = choices != null;
            InputBox.IsVisible = !picking;
            ChoiceBox.IsVisible = picking;
            if (picking) ChoiceBox.ItemsSource = choices;

            Func<string> read = () => picking ? ChoiceBox.Text ?? "" : InputBox.Text ?? "";
            if (picking) ChoiceBox.Text = initial ?? "";
            else InputBox.Text = initial ?? "";
            Opened += delegate
            {
                if (picking) ChoiceBox.Focus();
                else { InputBox.Focus(); InputBox.SelectAll(); }
            };

            void Update()
            {
                string text = read();
                OkButton.IsEnabled = text.Trim().Length > 0 && (isInvalid == null || !isInvalid(text.Trim()));
                HintText.Text = hint == null ? "" : hint(text);
                HintText.IsVisible = hint != null;
            }
            InputBox.TextChanged += delegate { Update(); };
            ChoiceBox.PropertyChanged += (sender, e) =>
            {
                if (e.Property == ComboBox.TextProperty || e.Property == SelectingItemsControl.SelectedItemProperty) Update();
            };
            Update();

            OkButton.Click += delegate { Accept(read()); };
            CancelButton.Click += delegate { Close(null); };
            InputBox.KeyDown += (sender, e) =>
            {
                if (e.Key == Key.Enter) { Accept(read()); e.Handled = true; }
            };
            ChoiceBox.KeyDown += (sender, e) =>
            {
                if (e.Key == Key.Enter && !ChoiceBox.IsDropDownOpen) { Accept(read()); e.Handled = true; }
            };
        }

        private void Accept(string raw)
        {
            string text = (raw ?? "").Trim();
            if (text.Length > 0 && OkButton.IsEnabled) Close(text);
        }
    }
}
