using System;
using Avalonia.Controls;
using Avalonia.Input;

namespace DW2ModLauncher.Avalonia.Views
{
    /// <summary>Modal single-line text prompt. Closes with the entered text, or null if cancelled. OK stays disabled while the text is blank.</summary>
    public partial class InputDialog : Window
    {
        public InputDialog()
        {
            InitializeComponent();
        }

        /// <param name="hint">Optional live note shown under the box (e.g. the folder name the text will become).</param>
        public InputDialog(string title, string label, string initial, string okText, string cancelText, Func<string, string> hint) : this()
        {
            Title = title;
            LabelText.Text = label;
            OkButton.Content = okText;
            CancelButton.Content = cancelText;
            InputBox.Text = initial ?? "";
            Opened += delegate { InputBox.Focus(); InputBox.SelectAll(); };

            void Update()
            {
                string text = InputBox.Text ?? "";
                OkButton.IsEnabled = text.Trim().Length > 0;
                HintText.Text = hint == null ? "" : hint(text);
                HintText.IsVisible = hint != null;
            }
            InputBox.TextChanged += delegate { Update(); };
            Update();

            OkButton.Click += delegate { Accept(); };
            CancelButton.Click += delegate { Close(null); };
            InputBox.KeyDown += (sender, e) =>
            {
                if (e.Key == Key.Enter) { Accept(); e.Handled = true; }
            };
        }

        private void Accept()
        {
            string text = (InputBox.Text ?? "").Trim();
            if (text.Length > 0) Close(text);
        }
    }
}
