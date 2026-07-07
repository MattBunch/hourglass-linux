using Hourglass.Platform;
using Avalonia.Threading;

namespace Hourglass.Linux.Avalonia;

internal sealed class SingleInstanceLaunchRequestDispatcher
{
    private readonly Queue<SingleInstanceLaunchRequest> pendingRequests = [];
    private Func<SingleInstanceLaunchRequest, Task>? handler;

    public static SingleInstanceLaunchRequestDispatcher Shared { get; } = new();

    public Task DispatchAsync(SingleInstanceLaunchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        lock (this.pendingRequests)
        {
            if (this.handler == null)
            {
                this.pendingRequests.Enqueue(request);
                return Task.CompletedTask;
            }

            return this.PostToUiThreadAsync(this.handler, request);
        }
    }

    public void Register(Func<SingleInstanceLaunchRequest, Task> requestHandler)
    {
        ArgumentNullException.ThrowIfNull(requestHandler);

        SingleInstanceLaunchRequest[] queued;
        lock (this.pendingRequests)
        {
            this.handler = requestHandler;
            queued = this.pendingRequests.ToArray();
            this.pendingRequests.Clear();
        }

        foreach (SingleInstanceLaunchRequest request in queued)
        {
            _ = this.PostToUiThreadAsync(requestHandler, request);
        }
    }

    public void Unregister(Func<SingleInstanceLaunchRequest, Task> requestHandler)
    {
        ArgumentNullException.ThrowIfNull(requestHandler);

        lock (this.pendingRequests)
        {
            if (this.handler == requestHandler)
            {
                this.handler = null;
            }
        }
    }

    private Task PostToUiThreadAsync(
        Func<SingleInstanceLaunchRequest, Task> requestHandler,
        SingleInstanceLaunchRequest request)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            return requestHandler(request);
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await requestHandler(request).ConfigureAwait(true);
                completion.SetResult();
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        });
        return completion.Task;
    }
}
