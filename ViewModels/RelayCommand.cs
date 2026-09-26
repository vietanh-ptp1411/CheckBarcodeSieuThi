using System.Windows.Input;

namespace CheckBarcodeSieuThi.ViewModels
{
    public class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;

        public void Execute(object? parameter) => execute();
    }

    /// <summary>Command nhận tham số, vd: dòng hàng trong giỏ (CommandParameter="{Binding}").</summary>
    public class RelayCommand<T>(Action<T> execute, Func<T, bool>? canExecute = null) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object? parameter) =>
            parameter is T value ? canExecute?.Invoke(value) ?? true : false;

        public void Execute(object? parameter)
        {
            if (parameter is T value) execute(value);
        }
    }
}
