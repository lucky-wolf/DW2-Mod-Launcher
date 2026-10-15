using System;
using Avalonia;
using Avalonia.Controls;
using DW2ModLauncher.Avalonia.ViewModels;

namespace DW2ModLauncher.Avalonia.Views
{
    public partial class ModSettingsDialog : Window
    {
        public ModSettingsDialog()
        {
            InitializeComponent();
        }

        public ModSettingsDialog(ModSettingsEditorViewModel editor) : this()
        {
            DataContext = editor;
            SaveButton.Click += delegate { Close(true); };
            CancelButton.Click += delegate { Close(false); };
            RestoreScroll(editor.ScrollOffset);
            Closing += delegate { editor.ScrollOffset = Scroller.Offset.Y; };
        }

        /// <summary>
        /// Scrolls back to where the user left off. The content has no height until the first layout pass, so wait for one that
        /// gives it room to scroll; an offset past the end (the content is shorter now) is clamped by the scroll viewer.
        /// </summary>
        private void RestoreScroll(double offset)
        {
            if (offset <= 0) return;
            EventHandler handler = null;
            handler = delegate
            {
                if (Scroller.Extent.Height <= Scroller.Viewport.Height) return;
                Scroller.LayoutUpdated -= handler;
                Scroller.Offset = new Vector(0, offset);
            };
            Scroller.LayoutUpdated += handler;
            Closed += delegate { Scroller.LayoutUpdated -= handler; };
        }
    }
}
