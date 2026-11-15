using System;
using System.Collections.Generic;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using DW2ModLauncher.Avalonia.Services;

namespace DW2ModLauncher.Avalonia.Views
{
    /// <summary>
    /// Modal box listing the steps of one job (a publish to Steam, then to GitHub), each with a mark: an hourglass while it waits, a spinner
    /// while it runs, a green check when it is done, a red cross when it failed. The user can't dismiss it; the caller closes it by disposing
    /// the handle. A Cancel button appears after <c>cancelAfter</c> in case the work never finishes.
    /// </summary>
    public partial class StepsDialog : Window, IStepsHandle
    {
        // "hourglass", "check" and "close", Material Design Icons, Apache-2.0
        private const string HourglassPath = "M6 2v6h.01L6 8.01 10 12l-4 4 .01.01H6V22h12v-5.99h-.01L18 16l-4-4 4-3.99-.01-.01H18V2H6zm10 14.5V20H8v-3.5l4-4 4 4zm-4-5l-4-4V4h8v3.5l-4 4z";
        private const string CheckPath = "M9 16.17 4.83 12l-1.42 1.41L9 19 21 7l-1.41-1.41z";
        private const string ClosePath = "M19 6.41L17.59 5 12 10.59 6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 12 13.41 17.59 19 19 17.59 13.41 12z";

        private sealed class Row
        {
            public Control Waiting, Running, Done, Failed;
        }

        private readonly List<Row> rows = new List<Row>();
        private readonly DispatcherTimer cancelTimer;
        private bool finished;

        public StepsDialog()
        {
            InitializeComponent();
        }

        public StepsDialog(string title, IReadOnlyList<string> labels, string cancelText, TimeSpan cancelAfter, CancellationTokenSource cancel) : this()
        {
            Title = title;
            foreach (string label in labels) Rows.Children.Add(Build(label));
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
            for (int i = 0; i < rows.Count; i++) Set(i, StepState.Waiting);
        }

        private Control Build(string label)
        {
            Row row = new Row
            {
                Waiting = Icon(HourglassPath, Brushes.Gray),
                Running = new Arc { Classes = { "spinner" }, Width = 28, Height = 28, StartAngle = 0, SweepAngle = 270, StrokeThickness = 4, VerticalAlignment = VerticalAlignment.Center },
                Done = Icon(CheckPath, (IBrush)this.FindResource("Dw2Green")),
                Failed = Icon(ClosePath, (IBrush)this.FindResource("Dw2Red"))
            };
            ((Arc)row.Running).Stroke = (IBrush)this.FindResource("Dw2Gold");
            rows.Add(row);
            Grid mark = new Grid { Width = 32, Height = 32 };
            mark.Children.Add(row.Waiting);
            mark.Children.Add(row.Running);
            mark.Children.Add(row.Done);
            mark.Children.Add(row.Failed);
            StackPanel line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
            line.Children.Add(mark);
            line.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 320 });
            return line;
        }

        private static Control Icon(string data, IBrush brush)
        {
            return new global::Avalonia.Controls.PathIcon { Data = Geometry.Parse(data), Width = 26, Height = 26, Foreground = brush, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        }

        /// <summary>Marks step <paramref name="index"/> (on the UI thread).</summary>
        public void Set(int index, StepState state)
        {
            Row row = rows[index];
            row.Waiting.IsVisible = state == StepState.Waiting;
            row.Running.IsVisible = state == StepState.Running;
            row.Done.IsVisible = state == StepState.Done;
            row.Failed.IsVisible = state == StepState.Failed;
        }

        public void HideCancel()
        {
            cancelTimer?.Stop();
            CancelButton.IsVisible = false;
        }

        public void Dispose()
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
