using System.Collections.Generic;
using System.Threading.Tasks;
using DW2ModLauncher.Avalonia.ViewModels;

namespace DW2ModLauncher.Avalonia.Services
{
    /// <summary>The few things view models need from the windowing layer, kept out of the view models themselves.</summary>
    public interface IDialogService
    {
        /// <summary>Label of the single button on message boxes; the main view model keeps it in the current language.</summary>
        string OkText { get; set; }
        Task ShowMessageAsync(string message, string title);
        Task<bool> ConfirmAsync(string message, string title, string yesText, string noText);
        /// <summary>Null if the user cancelled.</summary>
        Task<string> PickFromListAsync(string title, string note, IList<string> items);
        /// <summary>Null if the user cancelled.</summary>
        Task<string> PickFolderAsync(string title, string startFolder);
        /// <summary>Null if the user cancelled.</summary>
        Task<string> PickFileAsync(string title, string startFolder, string filterName, params string[] patterns);
        /// <summary>True if the user saved.</summary>
        Task<bool> EditModSettingsAsync(ModSettingsEditorViewModel editor);
        /// <summary>True if the user chose to publish.</summary>
        Task<bool> EditPublishAsync(PublishDialogViewModel editor);
    }
}
