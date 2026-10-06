using System.Windows.Input;

namespace TextBean.ViewModels;

/// <summary>
/// DI 컨테이너와 MVVM 툴킷을 쓰지 않으므로(D-014) 직접 둔다.
/// </summary>
public sealed class RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => execute(parameter);

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
