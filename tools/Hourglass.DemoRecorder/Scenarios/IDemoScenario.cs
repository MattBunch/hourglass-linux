namespace Hourglass.DemoRecorder.Scenarios;

public interface IDemoScenario
{
    string Name { get; }

    Task RunAsync(DemoContext context, CancellationToken cancellationToken);
}
