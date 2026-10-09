using System.Threading;
using Avalonia.Controls;
using DW2ModLauncher.Avalonia.Services;

namespace DW2ModLauncher.Avalonia.Views
{
    /// <summary>
    /// Modal progress bar with a Cancel button. The user can only leave it through Cancel (or Esc); the caller closes
    /// it by disposing the handle once the work is over.
    /// </summary>
    public partial class ProgressDialog : Window, IProgressHandle
    {
        private bool finished;

        public ProgressDialog()
        {
            InitializeComponent();
        }

        public ProgressDialog(string message, string title, string cancelText, CancellationTokenSource cancel) : this()
        {
            Title = title;
            MessageText.Text = message;
            CancelButton.Content = cancelText;
            CancelButton.Click += delegate
            {
                CancelButton.IsEnabled = false;
                cancel.Cancel();
            };
        }

        public void Report(double? fraction, string detail)
        {
            Bar.IsIndeterminate = !fraction.HasValue;
            if (fraction.HasValue) Bar.Value = fraction.Value;
            DetailText.Text = detail;
        }

        public void Dispose()
        {
            if (finished) return;
            finished = true;
            Close();
        }

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            if (!finished) e.Cancel = true;
            base.OnClosing(e);
        }
    }
}
