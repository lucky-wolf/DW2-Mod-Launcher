using System;
using System.Collections.Generic;
using System.Linq;
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
        private ModSettingsDialog modSettingsDialog;
        private PublishDialog publishDialog;

        public DialogService(Func<Window> owner)
        {
            mainWindow = owner;
        }

        // While the settings or publish window is open it is the window that is not blocked, so dialogs opened from it must belong to it.
        // Owned by the main window instead, a confirm raised from the publish window leaves focus on nothing when it closes.
        private Window owner()
        {
            return (Window)modSettingsDialog ?? publishDialog ?? settingsDialog ?? mainWindow();
        }

        public bool IsDialogOpen { get { return mainWindow().OwnedWindows.Count > 0; } }

        public async Task ShowSettingsAsync(SettingsViewModel settings)
        {
            if (settingsDialog != null) return;
            settingsDialog = new SettingsDialog(settings);
            try { await settingsDialog.ShowDialog(mainWindow()); }
            finally { settingsDialog = null; }
        }

        public Task ShowAboutAsync(MainViewModel main)
        {
            return new AboutDialog(main).ShowDialog(owner());
        }

        public string OkText { get; set; } = "OK";

        public string CancelText { get; set; } = "Cancel";

        public string CopyText { get; set; } = "Copy to clipboard";

        public string OpenLogText { get; set; } = "Open log";

        public Task ShowMessageAsync(string message, string title)
        {
            return new MessageDialog(message, title, OkText, null, CopyText, OpenLogText).ShowDialog<bool>(owner());
        }

        public Task<bool> ConfirmAsync(string message, string title, string yesText, string noText)
        {
            return new MessageDialog(message, title, yesText, noText, CopyText, OpenLogText).ShowDialog<bool>(owner());
        }

        public async Task<int> ChooseAsync(string message, string title, IList<string> buttons, IList<string> tooltips = null)
        {
            int? choice = await new ChoiceDialog(message, title, buttons, tooltips).ShowDialog<int?>(owner());
            return choice ?? -1;
        }

        public Task<string> PickFromListAsync(string title, string note, IList<string> items)
        {
            return new ListPickerDialog(title, note, items, OkText, CancelText).ShowDialog<string>(owner());
        }

        public async Task<IList<string>> PickManyFromListAsync(string title, string note, IList<string> items)
        {
            List<string> picked = await new ListPickerDialog(title, note, items, OkText, CancelText, true).ShowDialog<List<string>>(owner());
            return picked == null ? null : items.Where(picked.Contains).ToList();
        }

        public Task<string> PromptTextAsync(string title, string label, string initial, string okText, string cancelText, Func<string, string> hint, IList<string> choices = null, Func<string, bool> isInvalid = null)
        {
            return new InputDialog(title, label, initial, okText, cancelText, hint, choices, isInvalid).ShowDialog<string>(owner());
        }

        public Task<string[]> PromptRenameAsync(string title, string intro, string titleLabel, string folderLabel, string initialTitle, string initialFolder, Func<string, string> folderFor, Func<string, string> hint, Func<string, bool> folderInvalid)
        {
            return new RenameDialog(title, intro, titleLabel, folderLabel, initialTitle, initialFolder, OkText, CancelText, folderFor, hint, folderInvalid).ShowDialog<string[]>(owner());
        }

        public async Task<string> PickFolderAsync(string title, string startFolder)
        {
            FolderPickerOpenOptions options = new FolderPickerOpenOptions { Title = title, AllowMultiple = false };
            if (!string.IsNullOrWhiteSpace(startFolder))
                options.SuggestedStartLocation = await owner().StorageProvider.TryGetFolderFromPathAsync(startFolder);
            IReadOnlyList<IStorageFolder> folders = await owner().StorageProvider.OpenFolderPickerAsync(options);
            return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
        }

        public async Task<string> DownloadsFolderAsync()
        {
            IStorageFolder folder = await owner().StorageProvider.TryGetWellKnownFolderAsync(WellKnownFolder.Downloads);
            return folder?.TryGetLocalPath();
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
            return ShowModSettingsAsync(editor);
        }

        private async Task<bool> ShowModSettingsAsync(ModSettingsEditorViewModel editor)
        {
            ModSettingsDialog dialog = new ModSettingsDialog(editor);
            Window parent = owner();
            modSettingsDialog = dialog;
            try { return await dialog.ShowDialog<bool>(parent); }
            finally { modSettingsDialog = null; }
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

        public IProgressHandle ShowProgress(string message, string title, string cancelText, CancellationTokenSource cancel)
        {
            ProgressDialog dialog = new ProgressDialog(message, title, cancelText, cancel);
            var shown = dialog.ShowDialog(owner());
            return dialog;
        }

        public void RequestExit()
        {
            mainWindow().Close();
        }

        public async Task<bool> EditPublishAsync(PublishDialogViewModel editor)
        {
            PublishDialog dialog = new PublishDialog(editor, this);
            Window parent = owner();
            publishDialog = dialog;
            try { return await dialog.ShowDialog<bool>(parent); }
            finally { publishDialog = null; }
        }
    }
}
