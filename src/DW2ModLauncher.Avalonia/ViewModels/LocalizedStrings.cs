using System.ComponentModel;
using DW2ModLauncher.Core.Services;

namespace DW2ModLauncher.Avalonia.ViewModels
{
    /// <summary>
    /// Language-pack lookup for bindings: {Binding L[Key]}. Changing the language raises "Item[]" so every
    /// bound string refreshes without rebuilding the UI.
    /// </summary>
    public class LocalizedStrings : INotifyPropertyChanged
    {
        private string language = "en";

        public event PropertyChangedEventHandler PropertyChanged;

        public string this[string key] { get { return Localization.Get(language, key); } }

        public void SetLanguage(string code)
        {
            language = code;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        }

        public string Format(string key, params object[] args)
        {
            string template = this[key];
            return args != null && args.Length > 0 ? string.Format(template, args) : template;
        }
    }
}
