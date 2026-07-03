namespace Hourglass.Linux.Avalonia.Tests;

using Hourglass.Platform;
using Hourglass.Timing;
using Xunit;

public sealed class Milestone6CoordinatorTests
{
    [Fact]
    public void NewTimerCommandPublishesCoordinatorRequest()
    {
        var viewModel = new MainWindowViewModel(new CountdownEngine(new TestMonotonicClock()), () => DateTime.Now);
        int requestCount = 0;
        viewModel.NewTimerRequested += (_, _) => requestCount++;

        viewModel.NewTimerCommand.Execute(null);

        Assert.Equal(1, requestCount);
    }

    [Fact]
    public async Task CoordinatedSessionInhibitorSharesLeaseUntilLastTimerReleases()
    {
        var inner = new RecordingSessionInhibitor();
        await using var coordinator = new CoordinatedSessionInhibitor(inner);

        IAsyncDisposable? first = await coordinator.InhibitAsync(
            "Timer one",
            inhibitSuspend: true,
            inhibitIdle: true);
        IAsyncDisposable? second = await coordinator.InhibitAsync(
            "Timer two",
            inhibitSuspend: true,
            inhibitIdle: true);

        Assert.Equal(1, inner.AcquireCount);
        Assert.Equal(0, inner.ReleaseCount);

        Assert.NotNull(first);
        await first.DisposeAsync();
        Assert.Equal(0, inner.ReleaseCount);

        Assert.NotNull(second);
        await second.DisposeAsync();
        Assert.Equal(1, inner.ReleaseCount);
    }

    [Fact]
    public void CoordinatorOwnedWindowsUseSingleSettingsLoadPath()
    {
        string coordinator = File.ReadAllText(FindRepositoryFile("src/Hourglass.Linux.Avalonia/TimerWindowCoordinator.cs"));
        string window = File.ReadAllText(FindRepositoryFile("src/Hourglass.Linux.Avalonia/MainWindow.axaml.cs"));

        Assert.Contains("loadSettingsOnOpened: false", coordinator, StringComparison.Ordinal);
        Assert.Contains("if (this.loadSettingsOnOpened)", window, StringComparison.Ordinal);
        Assert.Contains("await this.viewModel.LoadSettingsAsync();", window, StringComparison.Ordinal);
    }

    [Fact]
    public void CoordinatorFlushesClosingWindowSessionBeforeFinalClose()
    {
        string coordinator = File.ReadAllText(FindRepositoryFile("src/Hourglass.Linux.Avalonia/TimerWindowCoordinator.cs"));
        string window = File.ReadAllText(FindRepositoryFile("src/Hourglass.Linux.Avalonia/MainWindow.axaml.cs"));

        Assert.Contains("prepareCoordinatorClose: this.PrepareWindowCloseAsync", coordinator, StringComparison.Ordinal);
        Assert.Contains("await this.prepareCoordinatorClose(this).ConfigureAwait(false);", window, StringComparison.Ordinal);
        Assert.Contains("this.closingWindows.Add(window);", coordinator, StringComparison.Ordinal);
        Assert.Contains("await this.QueueSessionSave().ConfigureAwait(false);", coordinator, StringComparison.Ordinal);
        Assert.Contains(".Where(window => !this.closingWindows.Contains(window.Window))", coordinator, StringComparison.Ordinal);
    }

    [Fact]
    public void CoordinatorDistinguishesMissingActiveSessionsFromEmptyActiveSessions()
    {
        string coordinator = File.ReadAllText(FindRepositoryFile("src/Hourglass.Linux.Avalonia/TimerWindowCoordinator.cs"));

        Assert.Contains("LoadDocumentResult<ActiveTimerSessionsDocument>", coordinator, StringComparison.Ordinal);
        Assert.Contains("if (activeSessions.Found)", coordinator, StringComparison.Ordinal);
        Assert.Contains("return activeSessions.Value ?? ActiveTimerSessionsDocument.Empty;", coordinator, StringComparison.Ordinal);
        Assert.Contains("LoadOptionalDocumentAsync<ActiveTimerSessionDocument>", coordinator, StringComparison.Ordinal);
    }

    [Fact]
    public void CoordinatorStatusIconUpdatesAreDispatchedToUiThread()
    {
        string coordinator = File.ReadAllText(FindRepositoryFile("src/Hourglass.Linux.Avalonia/TimerWindowCoordinator.cs"));
        int applyStatusIconStart = coordinator.IndexOf("private void ApplyStatusIconState()", StringComparison.Ordinal);
        int applyStatusIconAsyncStart = coordinator.IndexOf("private async Task ApplyStatusIconStateAsync()", StringComparison.Ordinal);

        Assert.True(applyStatusIconStart >= 0);
        Assert.True(applyStatusIconAsyncStart > applyStatusIconStart);
        string syncMethod = coordinator[applyStatusIconStart..applyStatusIconAsyncStart];

        Assert.Contains("if (!Dispatcher.UIThread.CheckAccess())", syncMethod, StringComparison.Ordinal);
        Assert.Contains("Dispatcher.UIThread.Post(this.ApplyStatusIconState);", syncMethod, StringComparison.Ordinal);
        Assert.Contains("_ = this.ApplyStatusIconStateAsync();", syncMethod, StringComparison.Ordinal);
    }

    private sealed class RecordingSessionInhibitor : ISessionInhibitor
    {
        public int AcquireCount { get; private set; }

        public int ReleaseCount { get; private set; }

        public ValueTask<IAsyncDisposable?> InhibitAsync(
            string reason,
            bool inhibitSuspend,
            bool inhibitIdle,
            CancellationToken cancellationToken = default)
        {
            this.AcquireCount++;
            return ValueTask.FromResult<IAsyncDisposable?>(new Lease(this));
        }

        private sealed class Lease(RecordingSessionInhibitor owner) : IAsyncDisposable
        {
            private RecordingSessionInhibitor? owner = owner;

            public ValueTask DisposeAsync()
            {
                RecordingSessionInhibitor? current = Interlocked.Exchange(ref this.owner, null);
                if (current != null)
                {
                    current.ReleaseCount++;
                }

                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class TestMonotonicClock : IMonotonicClock
    {
        public TimeSpan Elapsed { get; private set; }
    }

    private static string FindRepositoryFile(string relativePath)
    {
        string? directory = AppContext.BaseDirectory;
        while (directory != null)
        {
            string candidate = Path.Combine(directory, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new FileNotFoundException($"Could not find {relativePath} from {AppContext.BaseDirectory}.");
    }
}
