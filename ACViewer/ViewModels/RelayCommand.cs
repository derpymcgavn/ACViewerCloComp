#nullable enable
using System;
using System.Windows.Input;

namespace ACViewer.ViewModels
{
    public class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Func<object?, bool>? _canExecute;
        public RelayCommand(Action execute) : this(_ => execute(), _ => true) { }
        public RelayCommand(Action execute, Func<bool> canExecute) : this(_ => execute(), _ => canExecute()) { }
        public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
        { _execute = execute ?? throw new ArgumentNullException(nameof(execute)); _canExecute = canExecute; }
        public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
        public void Execute(object? parameter) => _execute(parameter);
        public event EventHandler? CanExecuteChanged { add { CommandManager.RequerySuggested += value; } remove { CommandManager.RequerySuggested -= value; } }
        public void RaiseCanExecuteChanged() => CommandManager.InvalidateRequerySuggested();
    }
}
