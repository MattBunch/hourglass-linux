using Avalonia.Threading;

namespace Hourglass.Linux.Avalonia;

internal interface IUiDispatcher
{
    bool CheckAccess();

    void Post(Action action);

    Task InvokeAsync(Action action, CancellationToken cancellationToken = default);

    Task<T> InvokeAsync<T>(Func<T> action, CancellationToken cancellationToken = default);
}

internal sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public static AvaloniaUiDispatcher Instance { get; } = new();

    public bool CheckAccess()
    {
        return Dispatcher.UIThread.CheckAccess();
    }

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Dispatcher.UIThread.Post(action);
    }

    public Task InvokeAsync(Action action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();
        return Dispatcher.UIThread.InvokeAsync(action).GetTask();
    }

    public Task<T> InvokeAsync<T>(Func<T> action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();
        return Dispatcher.UIThread.InvokeAsync(action).GetTask();
    }
}

internal sealed class ImmediateUiDispatcher : IUiDispatcher
{
    public static ImmediateUiDispatcher Instance { get; } = new();

    public bool CheckAccess()
    {
        return true;
    }

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        action();
    }

    public Task InvokeAsync(Action action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();
        action();
        return Task.CompletedTask;
    }

    public Task<T> InvokeAsync<T>(Func<T> action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(action());
    }
}
