using System;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Threading;

namespace DW2ModLauncher.Avalonia.Views
{
    /// <summary>
    /// Modal "working..." box with a spinner. The user can't dismiss it; the caller closes it when the work
    /// is done. A Cancel button appears after <c>cancelAfter</c> in case the work never finishes.
    /// </summary>
    public partial class BusyDialog : Window
    {
        private readonly DispatcherTimer cancelTimer;
        private bool finished;

        public BusyDialog()
        {
            InitializeComponent();
        }

        public BusyDialog(string message, string title, string cancelText, TimeSpan cancelAfter, CancellationTokenSource cancel) : this()
        {
            Title = title;
            MessageText.Text = message;
            CancelButton.Content = cancelText;
            CancelButton.Click += delegate
            {
                CancelButton.IsEnabled = false;
                cancel.Cancel();
            };
            cancelTimer = new DispatcherTimer { Interval = cancelAfter };
            cancelTimer.Tick += delegate
            {
                cancelTimer.Stop();
                CancelButton.IsVisible = true;
            };
            cancelTimer.Start();
        }

        /// <summary>Closes the box once the work is over (the user's own close attempts are ignored until then).</summary>
        public void Finish()
        {
            finished = true;
            cancelTimer?.Stop();
            Close();
        }

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            if (!finished) e.Cancel = true;
            base.OnClosing(e);
        }
    }
}
