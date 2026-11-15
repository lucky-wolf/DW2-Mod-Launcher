using Avalonia.Controls;
using DW2ModLauncher.Avalonia.ViewModels;

namespace DW2ModLauncher.Avalonia.Views
{
    /// <summary>
    /// The author's dialog for marking a mod's overlapping hooks compatible: the same checkable list as the Compatible with section of the
    /// Edit Properties and Publish dialogs, on its own. Saving writes the marks into the mod's hooks file.
    /// </summary>
    public partial class HookReviewDialog : Window
    {
        public HookReviewDialog()
        {
            InitializeComponent();
        }

        public HookReviewDialog(CompatibleListViewModel list) : this()
        {
            DataContext = list;
            // the author's list is "Compatible with"; anyone else's is a read-only list of the hooks shared, closed with Close
            Title = (list.CanEdit ? list.L["PublishCompatibleWith"] : list.L["DetailHooksTitle"]) + " - " + (list.Mod.DisplayName ?? list.Mod.Id);
            CancelButton.Content = list.CanEdit ? list.L["Cancel"] : list.L["Close"];
            SaveButton.Click += delegate { Close(true); };
            CancelButton.Click += delegate { Close(false); };
        }
    }
}
