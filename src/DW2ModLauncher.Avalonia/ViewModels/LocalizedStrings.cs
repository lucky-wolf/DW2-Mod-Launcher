using System.ComponentModel;
using DW2ModLauncher.Core.Services;

namespace DW2ModLauncher.Avalonia.ViewModels
{
    /// <summary>
    /// Language-pack lookup for bindings: {Binding L[Key]}. Changing the language raises the indexer's change notification so every
    /// bound string refreshes without rebuilding the UI. Avalonia 12 re-reads an indexer binding only when the notification names the
    /// declared indexer property ("Item"); the WPF-style "Item[]" it used to accept is ignored, so both are raised.
    /// </summary>
    public class LocalizedStrings : INotifyPropertyChanged
    {
        private string language = "en";

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>The property names a language change raises: "Item" is the indexer's own name, "Item[]" the older convention.</summary>
        public static readonly string[] ChangedNames = { "Item", "Item[]" };

        public string this[string key] { get { return Localization.Get(language, key); } }

        public void SetLanguage(string code)
        {
            language = code;
            foreach (string name in ChangedNames) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public string Format(string key, params object[] args)
        {
            string template = this[key];
            return args != null && args.Length > 0 ? string.Format(template, args) : template;
        }
    }
}
