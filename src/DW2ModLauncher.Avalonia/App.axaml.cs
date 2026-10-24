using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace DW2ModLauncher.Avalonia
{
    public class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);

            // Avalonia's TextBox binds Ctrl+C/X but not the classic Windows Ctrl+Insert copy or Shift+Delete cut; Shift+Insert paste already works.
            InputElement.KeyDownEvent.AddClassHandler<TextBox>((box, e) =>
            {
                if (e.Key == Key.Insert && e.KeyModifiers == KeyModifiers.Control)
                {
                    box.Copy();
                    e.Handled = true;
                }
                else if (e.Key == Key.Delete && e.KeyModifiers == KeyModifiers.Shift && !box.IsReadOnly)
                {
                    box.Cut();
                    e.Handled = true;
                }
            }, global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                MainWindow window = new MainWindow();
                desktop.MainWindow = window;
                // A second launch asked this instance to come to the front.
                Program.SingleInstance.Activated += delegate
                {
                    Dispatcher.UIThread.Post(delegate
                    {
                        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
                        window.Show();
                        window.Activate();
                        // Nudge: some window managers only raise a window that was briefly topmost.
                        window.Topmost = true;
                        window.Topmost = false;
                    });
                };
            }
            base.OnFrameworkInitializationCompleted();
        }
    }
}
