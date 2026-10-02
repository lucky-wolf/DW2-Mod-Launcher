using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using DW2ModLauncher.Avalonia.ViewModels;
using DW2ModLauncher.Avalonia.Views;

namespace DW2ModLauncher.Avalonia.Services
{
    public class DialogService : IDialogService
    {
        private readonly Func<Window> owner;

        public DialogService(Func<Window> owner)
        {
            this.owner = owner;
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

        public Task<bool> EditPublishAsync(PublishDialogViewModel editor)
        {
            return new PublishDialog(editor, this).ShowDialog<bool>(owner());
        }
    }
}
