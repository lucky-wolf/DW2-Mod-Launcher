using Avalonia.Controls;
using DW2ModLauncher.Avalonia.ViewModels;

namespace DW2ModLauncher.Avalonia.Views
{
    /// <summary>The Browse window over the mod catalog. It fetches the catalog when it opens.</summary>
    public partial class CatalogDialog : Window
    {
        public CatalogDialog()
        {
            InitializeComponent();
            CloseButton.Click += delegate { Close(); };
        }

        public CatalogDialog(CatalogViewModel catalog) : this()
        {
            DataContext = catalog;
            Opened += async delegate { await catalog.LoadAsync(); };
        }
    }
}
