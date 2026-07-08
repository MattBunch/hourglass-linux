namespace Hourglass.Linux.Avalonia.Tests;

using Xunit;

public sealed class Milestone7CoordinatorTests
{
    [Fact]
    public void AppRegistersCoordinatorForSingleInstanceLaunchRequests()
    {
        string app = File.ReadAllText(FindRepositoryFile("src/Hourglass.Linux.Avalonia/App.axaml.cs"));

        Assert.Contains("internal static SingleInstanceLaunchRequest? InitialLaunchRequest", app, StringComparison.Ordinal);
        Assert.Contains("SingleInstanceLaunchRequestDispatcher.Shared.Register(this.coordinator.HandleLaunchRequestAsync);", app, StringComparison.Ordinal);
        Assert.Contains("_ = this.StartCoordinatorAsync(initialRequest);", app, StringComparison.Ordinal);
        Assert.True(
            app.IndexOf("await this.coordinator.StartAsync(initialRequest)", StringComparison.Ordinal)
            < app.IndexOf("SingleInstanceLaunchRequestDispatcher.Shared.Register(this.coordinator.HandleLaunchRequestAsync);", StringComparison.Ordinal));
        Assert.Contains("SingleInstanceLaunchRequestDispatcher.Shared.Unregister(this.coordinator.HandleLaunchRequestAsync);", app, StringComparison.Ordinal);
    }

    [Fact]
    public void CoordinatorRoutesActivationAndTimerRequests()
    {
        string coordinator = File.ReadAllText(FindRepositoryFile("src/Hourglass.Linux.Avalonia/TimerWindowCoordinator.cs"));
        int handleStart = coordinator.IndexOf("public Task HandleLaunchRequestAsync", StringComparison.Ordinal);
        int disposeStart = coordinator.IndexOf("public async ValueTask DisposeAsync", StringComparison.Ordinal);
        int activateStart = coordinator.IndexOf("private void ActivateMostRelevantWindow()", StringComparison.Ordinal);
        int closeAllStart = coordinator.IndexOf("private async Task CloseAllWindowsAsync()", StringComparison.Ordinal);

        Assert.True(handleStart >= 0);
        Assert.True(disposeStart > handleStart);
        Assert.True(activateStart > disposeStart);
        Assert.True(closeAllStart > activateStart);
        string handleMethod = coordinator[handleStart..disposeStart];
        string activateMethod = coordinator[activateStart..closeAllStart];

        Assert.Contains("request.Kind == SingleInstanceLaunchRequestKind.StartTimer", handleMethod, StringComparison.Ordinal);
        Assert.Contains("this.CreateWindow(launchRequest: request);", handleMethod, StringComparison.Ordinal);
        Assert.Contains("this.ActivateMostRelevantWindow();", handleMethod, StringComparison.Ordinal);
        Assert.Contains("WindowRegistration? target = this.GetStatusIconTarget();", activateMethod, StringComparison.Ordinal);
        Assert.Contains("target = this.CreateWindow() == null ? null : this.mostRecentWindow;", activateMethod, StringComparison.Ordinal);
        Assert.Contains("new WindowAttentionController(target.Window).RequestAttention();", activateMethod, StringComparison.Ordinal);
    }

    [Fact]
    public void CoordinatorAppliesLaunchTimerRequestAfterSettingsLoad()
    {
        string coordinator = File.ReadAllText(FindRepositoryFile("src/Hourglass.Linux.Avalonia/TimerWindowCoordinator.cs"));
        int loadWindowStart = coordinator.IndexOf("private async Task LoadWindowAsync", StringComparison.Ordinal);
        int loadActiveSessionsStart = coordinator.IndexOf("private async Task<ActiveTimerSessionsDocument> LoadActiveSessionsAsync", StringComparison.Ordinal);

        Assert.True(loadWindowStart >= 0);
        Assert.True(loadActiveSessionsStart > loadWindowStart);
        string loadWindowMethod = coordinator[loadWindowStart..loadActiveSessionsStart];

        Assert.True(
            loadWindowMethod.IndexOf("await registration.ViewModel.LoadSettingsAsync()", StringComparison.Ordinal)
            < loadWindowMethod.IndexOf("registration.ViewModel.ApplyLaunchTimerRequest", StringComparison.Ordinal));
        Assert.Contains("launchRequest?.Kind == SingleInstanceLaunchRequestKind.StartTimer", loadWindowMethod, StringComparison.Ordinal);
        Assert.Contains("registration.ViewModel.ApplyLaunchTimerRequest(", loadWindowMethod, StringComparison.Ordinal);
    }

    private static string FindRepositoryFile(string relativePath)
    {
        string directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            string candidate = Path.Combine(directory, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            DirectoryInfo? parent = Directory.GetParent(directory);
            directory = parent?.FullName ?? string.Empty;
        }

        throw new FileNotFoundException($"Could not find {relativePath}.");
    }
}
