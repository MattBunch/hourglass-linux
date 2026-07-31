namespace Hourglass.DemoRecorder.Scenarios;

public sealed class ReadmeDemoScenario : IDemoScenario
{
    public string Name => DemoRecorderOptions.DefaultScenario;

    public async Task RunAsync(DemoContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        await context.ShowWindowAsync(cancellationToken).ConfigureAwait(true);
        await context.HoldAsync(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(true);

        await context.TypeTimerInputAsync("10 sec", cancellationToken).ConfigureAwait(true);
        await context.SetTimerTitleAsync("Demo timer", cancellationToken).ConfigureAwait(true);
        await context.HoldAsync(TimeSpan.FromSeconds(1.5), cancellationToken).ConfigureAwait(true);

        await context.PressEnterAsync(cancellationToken).ConfigureAwait(true);
        await context.HoldAsync(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(true);

        await context.PauseAsync(cancellationToken).ConfigureAwait(true);
        await context.HoldAsync(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(true);

        await context.ResumeAsync(cancellationToken).ConfigureAwait(true);
        await context.HoldAsync(TimeSpan.FromSeconds(1.5), cancellationToken).ConfigureAwait(true);

        await context.OpenContextMenuAsync(cancellationToken).ConfigureAwait(true);
        await context.HoldAsync(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(true);
        await context.ToggleAlwaysOnTopAsync(cancellationToken).ConfigureAwait(true);
        await context.HoldAsync(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(true);
        await context.CloseContextMenuAsync(cancellationToken).ConfigureAwait(true);

        await context.HoldAsync(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(true);
        await context.HoldAsync(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(true);
        await context.OpenAboutAsync(cancellationToken).ConfigureAwait(true);
        await context.HoldAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(true);
    }
}
