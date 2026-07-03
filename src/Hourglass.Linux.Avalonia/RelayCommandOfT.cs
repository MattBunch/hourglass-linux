using System.Windows.Input;

namespace Hourglass.Linux.Avalonia;

public sealed class RelayCommand<T> : ICommand
{
    private readonly Predicate<T?>? canExecute;
    private readonly Action<T?> execute;

    public RelayCommand(Action<T?> execute, Predicate<T?>? canExecute = null)
    {
        this.execute = execute ?? throw new ArgumentNullException(nameof(execute));
        this.canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        return this.canExecute?.Invoke(ConvertParameter(parameter)) ?? true;
    }

    public void Execute(object? parameter)
    {
        T? converted = ConvertParameter(parameter);
        if (!this.CanExecute(converted))
        {
            return;
        }

        this.execute(converted);
    }

    public void RaiseCanExecuteChanged()
    {
        this.CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    private static T? ConvertParameter(object? parameter)
    {
        if (parameter is T typed)
        {
            return typed;
        }

        return default;
    }
}
