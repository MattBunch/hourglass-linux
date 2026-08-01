using Hourglass.DemoRecorder.Scenarios;
using Hourglass.DemoRecorder.Services;
using Xunit;

namespace Hourglass.DemoRecorder.Tests;

public sealed class DemoScenarioRunnerTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(Path.GetTempPath(), $"hourglass-scenario-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task ReadmeScenarioProducesDeterministicFrameCount()
    {
        HeadlessTestHost.EnsureStarted();
        DemoRecorderOptions options = DemoRecorderOptions.Defaults(Directory.GetCurrentDirectory()) with
        {
            FramesDirectory = Path.Combine(this.temporaryDirectory, "frames"),
            SkipGif = true,
            SkipVideo = true
        };
        var recorder = new FrameRecorder(options.FramesDirectory, options.Width, options.Height);
        recorder.PrepareEmptyDirectory();
        var services = new DemoPlatformServices();
        await using DemoContext context = await DemoContext.CreateAsync(options, recorder, services);
        var runner = new DemoScenarioRunner(new ReadmeDemoScenario(), context);

        int frameCount = await runner.RunAsync();

        Assert.Equal(192, frameCount);
        Assert.Equal(1, services.NotificationService.NotificationCount);
        Assert.True(services.SoundService.PlayCount >= 1);
        Assert.Equal(0, services.ExternalUriLauncher.OpenCount);
    }

    [Fact]
    public async Task ScenarioFailuresIncludeScenarioName()
    {
        HeadlessTestHost.EnsureStarted();
        DemoRecorderOptions options = DemoRecorderOptions.Defaults(Directory.GetCurrentDirectory()) with
        {
            FramesDirectory = Path.Combine(this.temporaryDirectory, "failure-frames"),
            SkipGif = true,
            SkipVideo = true
        };
        var recorder = new FrameRecorder(options.FramesDirectory, options.Width, options.Height);
        recorder.PrepareEmptyDirectory();
        await using DemoContext context = await DemoContext.CreateAsync(options, recorder, new DemoPlatformServices());
        var runner = new DemoScenarioRunner(new ThrowingScenario(), context);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync());

        Assert.Contains("failed", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HoldAsyncPreservesRequestedDurationAcrossFrameSteps()
    {
        HeadlessTestHost.EnsureStarted();
        DemoRecorderOptions options = DemoRecorderOptions.Defaults(Directory.GetCurrentDirectory()) with
        {
            FramesDirectory = Path.Combine(this.temporaryDirectory, "duration-frames"),
            SkipGif = true,
            SkipVideo = true
        };
        var recorder = new FrameRecorder(options.FramesDirectory, options.Width, options.Height);
        recorder.PrepareEmptyDirectory();
        var services = new DemoPlatformServices();
        await using DemoContext context = await DemoContext.CreateAsync(options, recorder, services);

        await context.ShowWindowAsync();
        await context.HoldAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(TimeSpan.FromSeconds(1), services.Clock.Elapsed);
        Assert.Equal(12, context.FrameCount);
    }

    [Fact]
    public async Task HoldAsyncAdvancesClockForSubFrameDurations()
    {
        HeadlessTestHost.EnsureStarted();
        DemoRecorderOptions options = DemoRecorderOptions.Defaults(Directory.GetCurrentDirectory()) with
        {
            FramesDirectory = Path.Combine(this.temporaryDirectory, "sub-frame-duration-frames"),
            SkipGif = true,
            SkipVideo = true
        };
        var recorder = new FrameRecorder(options.FramesDirectory, options.Width, options.Height);
        recorder.PrepareEmptyDirectory();
        var services = new DemoPlatformServices();
        await using DemoContext context = await DemoContext.CreateAsync(options, recorder, services);

        await context.HoldAsync(TimeSpan.FromMilliseconds(1));

        Assert.Equal(TimeSpan.FromMilliseconds(1), services.Clock.Elapsed);
        Assert.Equal(0, context.FrameCount);
    }

    public void Dispose()
    {
        if (Directory.Exists(this.temporaryDirectory))
        {
            Directory.Delete(this.temporaryDirectory, recursive: true);
        }
    }

    private sealed class ThrowingScenario : IDemoScenario
    {
        public string Name => "throwing";

        public Task RunAsync(DemoContext context, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("boom");
        }
    }
}
