using Hourglass.DemoRecorder.Services;
using Xunit;

namespace Hourglass.DemoRecorder.Tests;

public sealed class RecorderIsolationTests
{
    [Fact]
    public async Task DemoRunUsesRecordingPlatformServices()
    {
        HeadlessTestHost.EnsureStarted();
        string temporaryDirectory = Path.Combine(Path.GetTempPath(), $"hourglass-isolation-tests-{Guid.NewGuid():N}");
        try
        {
            DemoRecorderOptions options = DemoRecorderOptions.Defaults(Directory.GetCurrentDirectory()) with
            {
                FramesDirectory = Path.Combine(temporaryDirectory, "frames"),
                SkipGif = true,
                SkipVideo = true
            };
            var recorder = new FrameRecorder(options.FramesDirectory, options.Width, options.Height);
            recorder.PrepareEmptyDirectory();
            var services = new DemoPlatformServices();
            await using DemoContext context = await DemoContext.CreateAsync(options, recorder, services);

            await new DemoScenarioRunner(new Hourglass.DemoRecorder.Scenarios.ReadmeDemoScenario(), context).RunAsync();

            Assert.True(services.SettingsStore.Accessed);
            Assert.True(services.SessionInhibitor.AcquireCount >= 1);
            Assert.True(services.DesktopProgressService.SetCount >= 1);
            Assert.Equal(1, services.NotificationService.NotificationCount);
            Assert.True(services.SoundService.PlayCount >= 1);
            Assert.Equal(0, services.ExternalUriLauncher.OpenCount);
            Assert.Equal(0, services.SystemPowerService.RequestCount);
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory))
            {
                Directory.Delete(temporaryDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void DemoContextDefersDisposedFlagUntilUiThreadCleanup()
    {
        string sourcePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "tools",
            "Hourglass.DemoRecorder",
            "DemoContext.cs"));
        string source = File.ReadAllText(sourcePath);

        int dispatchBranchIndex = source.IndexOf("if (!Dispatcher.UIThread.CheckAccess())", StringComparison.Ordinal);
        int firstDisposedAssignmentIndex = source.IndexOf("this.disposed = true;", StringComparison.Ordinal);
        int cleanupMethodIndex = source.IndexOf("private void DisposeOnUiThread()", StringComparison.Ordinal);

        Assert.True(dispatchBranchIndex >= 0);
        Assert.True(cleanupMethodIndex >= 0);
        Assert.True(firstDisposedAssignmentIndex > cleanupMethodIndex);
        Assert.True(dispatchBranchIndex < cleanupMethodIndex);
    }

    [Fact]
    public void DemoAboutMetadataIsStableAcrossMachines()
    {
        Hourglass.Linux.Avalonia.ApplicationInfo applicationInfo = DemoContext.DemoApplicationInfo;

        Assert.Equal("Hourglass Linux", applicationInfo.ProductName);
        Assert.Equal("README demo", applicationInfo.Version);
        Assert.Equal("README demo", applicationInfo.InformationalVersion);
        Assert.Equal("Demo", applicationInfo.BuildConfiguration);
        Assert.Equal("demo", applicationInfo.SourceRevision);
        Assert.Equal(".NET", applicationInfo.RuntimeDescription);
        Assert.Equal("Linux", applicationInfo.OperatingSystemDescription);
        Assert.Equal("x64", applicationInfo.ProcessArchitecture);
    }

    [Fact]
    public void DemoContextUsesCoordinatedWindowCloseDuringDisposal()
    {
        string contextSourcePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "tools",
            "Hourglass.DemoRecorder",
            "DemoContext.cs"));
        string windowSourcePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "src",
            "Hourglass.Linux.Avalonia",
            "MainWindow.axaml.cs"));
        string contextSource = File.ReadAllText(contextSourcePath);
        string windowSource = File.ReadAllText(windowSourcePath);

        Assert.Contains("this.Window.CloseCoordinatedAsync().GetAwaiter().GetResult();", contextSource, StringComparison.Ordinal);
        Assert.Contains("await this.closeCoordinator.PendingPreparation.ConfigureAwait(true);", windowSource, StringComparison.Ordinal);
        Assert.Contains("Dispatcher.UIThread.RunJobs();", windowSource, StringComparison.Ordinal);
    }
}
