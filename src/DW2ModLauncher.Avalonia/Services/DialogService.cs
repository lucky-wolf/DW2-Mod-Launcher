using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using DW2ModLauncher.Avalonia.ViewModels;
using DW2ModLauncher.Avalonia.Views;

namespace DW2ModLauncher.Avalonia.Services
{
    public class DialogService : IDialogService
    {
        private readonly Func<Window> mainWindow;
        private SettingsDialog settingsDialog;

        public DialogService(Func<Window> owner)
        {
            mainWindow = owner;
        }

        // While the settings window is open it is the window that is not blocked, so dialogs opened from it must belong to it.
        private Window owner()
        {
            return settingsDialog ?? mainWindow();
        }

        public async Task ShowSettingsAsync(SettingsViewModel settings)
        {
            if (settingsDialog != null) return;
            settingsDialog = new SettingsDialog(settings);
            try { await settingsDialog.ShowDialog(mainWindow()); }
            finally { settingsDialog = null; }
        }

        public string OkText { get; set; } = "OK";

        public Task ShowMessageAsync(string message, string title)
        {
            return new MessageDialog(message, title, OkText, null).ShowDialog<bool>(owner());
        }

        public Task<bool> ConfirmAsync(string message, string title, string yesText, string noText)
        {
            return new MessageDialog(message, title, yesText, noText).ShowDialog<bool>(owner());
        }

        public Task<string> PickFromListAsync(string title, string note, IList<string> items)
        {
            return new ListPickerDialog(title, note, items).ShowDialog<string>(owner());
        }

        public Task<string> PromptTextAsync(string title, string label, string initial, string okText, string cancelText, Func<string, string> hint)
        {
            return new InputDialog(title, label, initial, okText, cancelText, hint).ShowDialog<string>(owner());
        }

        public async Task<string> PickFolderAsync(string title, string startFolder)
        {
            FolderPickerOpenOptions options = new FolderPickerOpenOptions { Title = title, AllowMultiple = false };
            if (!string.IsNullOrWhiteSpace(startFolder))
                options.SuggestedStartLocation = await owner().StorageProvider.TryGetFolderFromPathAsync(startFolder);
            IReadOnlyList<IStorageFolder> folders = await owner().StorageProvider.OpenFolderPickerAsync(options);
            return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
        }

        public async Task<string> PickFileAsync(string title, string startFolder, string filterName, params string[] patterns)
        {
            FilePickerOpenOptions options = new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                FileTypeFilter = new List<FilePickerFileType> { new FilePickerFileType(filterName) { Patterns = patterns } }
            };
            if (!string.IsNullOrWhiteSpace(startFolder))
                options.SuggestedStartLocation = await owner().StorageProvider.TryGetFolderFromPathAsync(startFolder);
            IReadOnlyList<IStorageFile> files = await owner().StorageProvider.OpenFilePickerAsync(options);
            return files.Count == 0 ? null : files[0].TryGetLocalPath();
        }

        public Task<bool> EditModSettingsAsync(ModSettingsEditorViewModel editor)
        {
            return new ModSettingsDialog(editor).ShowDialog<bool>(owner());
        }

        public IDisposable ShowBusy(string message, string title, string cancelText, TimeSpan cancelAfter, CancellationTokenSource cancel)
        {
            BusyDialog dialog = new BusyDialog(message, title, cancelText, cancelAfter, cancel);
            var shown = dialog.ShowDialog(owner());
            return new BusyHandle(dialog);
        }

        private sealed class BusyHandle : IDisposable
        {
            private readonly BusyDialog dialog;
            public BusyHandle(BusyDialog dialog) { this.dialog = dialog; }
            public void Dispose() { dialog.Finish(); }
        }

        public Task<bool> EditPublishAsync(PublishDialogViewModel editor)
        {
            return new PublishDialog(editor, this).ShowDialog<bool>(owner());
        }
    }
}
