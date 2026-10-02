using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace DW2ModLauncher.Avalonia.ViewModels
{
    /// <summary>Button command. The async form disables itself while running so a click can't re-enter.</summary>
    public class RelayCommand : ICommand
    {
        private readonly Func<object, Task> execute;
        private readonly Func<bool> canExecute;
        private bool running;

        public RelayCommand(Action execute, Func<bool> canExecute = null)
            : this(delegate (object p) { execute(); return Task.CompletedTask; }, canExecute)
        {
        }

        public RelayCommand(Func<Task> execute, Func<bool> canExecute = null)
            : this(delegate (object p) { return execute(); }, canExecute)
        {
        }

        private RelayCommand(Func<object, Task> execute, Func<bool> canExecute)
        {
            this.execute = execute;
            this.canExecute = canExecute;
        }

        /// <summary>A command that receives the XAML CommandParameter.</summary>
        public static RelayCommand WithParameter(Action<object> execute, Func<bool> canExecute = null)
        {
            return new RelayCommand(delegate (object p) { execute(p); return Task.CompletedTask; }, canExecute);
        }

        public event EventHandler CanExecuteChanged;

        public bool CanExecute(object parameter) { return !running && (canExecute == null || canExecute()); }

        public async void Execute(object parameter)
        {
            running = true;
            RaiseCanExecuteChanged();
            try { await execute(parameter); }
            finally
            {
                running = false;
                RaiseCanExecuteChanged();
            }
        }

        public void RaiseCanExecuteChanged() { CanExecuteChanged?.Invoke(this, EventArgs.Empty); }
    }
}
