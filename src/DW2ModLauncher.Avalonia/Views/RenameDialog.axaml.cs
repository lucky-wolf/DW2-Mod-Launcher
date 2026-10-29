using System;
using Avalonia.Controls;
using Avalonia.Input;

namespace DW2ModLauncher.Avalonia.Views
{
    /// <summary>
    /// Modal rename prompt with two boxes: the Mod's title and its folder name. The folder follows the title (made valid by <c>folderFor</c>) until it is edited by hand.
    /// Closes with { title, folder }, or null if cancelled.
    /// </summary>
    public partial class RenameDialog : Window
    {
        public RenameDialog()
        {
            InitializeComponent();
        }

        /// <param name="folderFor">Turns what is typed in the folder box into the folder name that will be used.</param>
        /// <param name="hint">The note under the boxes for the folder name that will be used.</param>
        /// <param name="folderInvalid">True when that folder name cannot be used (already taken); disables OK.</param>
        public RenameDialog(string title, string intro, string titleLabel, string folderLabel, string initialTitle, string initialFolder, string okText, string cancelText,
            Func<string, string> folderFor, Func<string, string> hint, Func<string, bool> folderInvalid) : this()
        {
            Title = title;
            IntroText.Text = intro;
            TitleLabel.Text = titleLabel;
            FolderLabel.Text = folderLabel;
            OkButton.Content = okText;
            CancelButton.Content = cancelText;
            TitleBox.Text = initialTitle ?? "";
            FolderBox.Text = initialFolder ?? "";
            bool follows = string.Equals(initialFolder, folderFor(initialTitle ?? ""), StringComparison.Ordinal);
            bool setting = false;

            void Update()
            {
                string folder = FolderBox.Text ?? "";
                string used = folderFor(folder);
                OkButton.IsEnabled = (TitleBox.Text ?? "").Trim().Length > 0 && folder.Trim().Length > 0 && !folderInvalid(used);
                HintText.Text = hint(used);
            }
            TitleBox.TextChanged += delegate
            {
                if (follows)
                {
                    setting = true;
                    FolderBox.Text = folderFor(TitleBox.Text ?? "");
                    setting = false;
                }
                Update();
            };
            FolderBox.TextChanged += delegate
            {
                if (!setting) follows = string.Equals(FolderBox.Text, folderFor(TitleBox.Text ?? ""), StringComparison.Ordinal);
                Update();
            };
            Update();
            Opened += delegate { TitleBox.Focus(); TitleBox.SelectAll(); };

            void Accept()
            {
                if (OkButton.IsEnabled) Close(new[] { (TitleBox.Text ?? "").Trim(), folderFor(FolderBox.Text ?? "") });
            }
            OkButton.Click += delegate { Accept(); };
            CancelButton.Click += delegate { Close(null); };
            EventHandler<KeyEventArgs> enter = (sender, e) => { if (e.Key == Key.Enter) { Accept(); e.Handled = true; } };
            TitleBox.KeyDown += enter;
            FolderBox.KeyDown += enter;
        }
    }
}
