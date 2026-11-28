using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using DW2ModLauncher.Avalonia.Services;
using DW2ModLauncher.Avalonia.ViewModels;

namespace DW2ModLauncher.Avalonia.Views
{
    public partial class PublishDialog : Window
    {
        public PublishDialog()
        {
            InitializeComponent();
            ScreenFit.Attach(this);
        }

        // the hook section was last scrolled into view for a warning that still stands (cleared once nothing is unresolved, so a new warning scrolls again)
        private bool hookIssuesRevealed;

        /// <summary>
        /// Unresolved hook issues sit near the bottom of a long form, out of sight, and can appear after the dialog opens (the list is rebuilt for the version being
        /// published). The first time one shows, the hook section opens and scrolls to about the middle of the window.
        /// </summary>
        private void RevealHookIssues(PublishDialogViewModel editor)
        {
            if (!editor.Compatible.HasCompatibleCaution)
            {
                hookIssuesRevealed = false;
                return;
            }
            if (hookIssuesRevealed) return;
            hookIssuesRevealed = true;
            Dispatcher.UIThread.Post(ScrollToHookSection, DispatcherPriority.Loaded);
        }

        private void ScrollToHookSection()
        {
            CompatibleExpander.IsExpanded = true;
            ContentScroll.UpdateLayout();
            if (ContentScroll.Content is not Visual content || CompatibleExpander.TranslatePoint(new Point(0, 0), content) is not Point at) return;
            double viewport = ContentScroll.Viewport.Height;
            double spare = Math.Max(0, viewport - CompatibleExpander.Bounds.Height);
            double max = Math.Max(0, ContentScroll.Extent.Height - viewport);
            ContentScroll.Offset = ContentScroll.Offset.WithY(Math.Clamp(at.Y - spare / 2, 0, max));
        }

        public PublishDialog(PublishDialogViewModel editor, IDialogService dialogs) : this()
        {
            DataContext = editor;
            Closed += delegate { editor.Dispose(); };
            Opened += delegate { RevealHookIssues(editor); };
            editor.PropertyChanged += (sender, e) =>
            {
                if (e.PropertyName == nameof(PublishDialogViewModel.Compatible)) RevealHookIssues(editor);
            };
            CancelButton.Click += delegate { Close(false); };
            editor.CloseRequested += delegate { Close(false); };
            KeyDown += (sender, e) =>
            {
                if (e.Key == Key.Escape) { Close(false); e.Handled = true; }
            };
            PublishButton.Click += async delegate
            {
                if (editor.NothingToPublish) { Close(false); return; }
                // Saving rewrites mod.json's bundle list from the folder, so missing entries are about to be dropped: ask first.
                string question = editor.MissingBundlesQuestion;
                if (question != null && !await dialogs.ConfirmAsync(question, Title, editor.L["OK"], editor.L["Cancel"])) return;
                // Loose images other than the preview would be uploaded for no purpose: offer to clear them out, then carry on whatever the answer.
                await editor.OfferStrayImageCleanupAsync();
                // A "Compatible with" mark for another version than the installed one says nothing about the installed one: ask here, over this
                // dialog, so cancelling leaves the author where the marks can be fixed.
                List<string> staleMarks = editor.StaleCompatible();
                if (staleMarks.Count > 0)
                {
                    int carryOn = await dialogs.ChooseAsync(editor.L.Format("PublishStaleCompatibleWarning", ("Marks", string.Join("\n", staleMarks))), editor.L["PublishStaleCompatibleTitle"],
                        new[] { editor.L["PublishStaleCompatibleYes"], editor.L["Cancel"] });
                    if (carryOn != 0) return;
                }
                // Likewise for overlapping mods the list does not mention at all (the count on the "Compatible with" bar).
                List<string> unmarked = editor.UnmarkedOverlaps();
                if (unmarked.Count > 0)
                {
                    int carryOn = await dialogs.ChooseAsync(editor.L.Format("PublishUnmarkedOverlapWarning", ("Mods", string.Join("\n", unmarked))), editor.L["PublishUnmarkedOverlapTitle"],
                        new[] { editor.L["PublishStaleCompatibleYes"], editor.L["Cancel"] });
                    if (carryOn != 0) return;
                }
                // Saving mod.json is part of publishing: a failed write keeps the dialog open.
                string error = editor.Commit();
                if (error != null) await new MessageDialog(error, Title, editor.L["OK"], null, editor.L["CopyToClipboard"], editor.L["OpenLog"]).ShowDialog<bool>(this);
                else Close(true);
            };
        }
    }
}
