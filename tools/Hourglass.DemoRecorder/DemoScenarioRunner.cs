using Hourglass.DemoRecorder.Scenarios;
using Avalonia.Threading;

namespace Hourglass.DemoRecorder;

public sealed class DemoScenarioRunner
{
    private readonly IDemoScenario scenario;
    private readonly DemoContext context;

    public DemoScenarioRunner(IDemoScenario scenario, DemoContext context)
    {
        this.scenario = scenario ?? throw new ArgumentNullException(nameof(scenario));
        this.context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                await this.scenario.RunAsync(this.context, cancellationToken).ConfigureAwait(true);
            }
            else
            {
                await Dispatcher.UIThread
                    .InvokeAsync(async () => await this.scenario.RunAsync(this.context, cancellationToken).ConfigureAwait(true))
                    .ConfigureAwait(true);
            }

            return this.context.FrameCount;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"Demo scenario '{this.scenario.Name}' failed: {exception.Message}", exception);
        }
    }
}
