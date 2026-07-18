using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace WinForge.UI;

public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null) { if (EqualityComparer<T>.Default.Equals(field, value)) return false; field = value; PropertyChanged?.Invoke(this, new(propertyName)); return true; }
    protected void Raise([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new(propertyName));
}

public sealed class AsyncCommand(Func<Task> execute, Func<bool>? canExecute = null) : ICommand
{
    private bool _running;
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => !_running && (canExecute?.Invoke() ?? true);
    public async void Execute(object? parameter) { if (!CanExecute(parameter)) return; _running = true; CanExecuteChanged?.Invoke(this, EventArgs.Empty); try { await execute(); } finally { _running = false; CanExecuteChanged?.Invoke(this, EventArgs.Empty); } }
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class RelayCommand(Action execute) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute();
}
