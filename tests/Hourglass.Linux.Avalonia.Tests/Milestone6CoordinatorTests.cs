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

    [Fact]
    public void CoordinatorValidatesActiveSessionsBeforeMarkingStartupRestored()
    {
        string coordinator = File.ReadAllText(FindRepositoryFile("src/Hourglass.Linux.Avalonia/TimerWindowCoordinator.cs"));
        int startupStart = coordinator.IndexOf("public async Task StartAsync", StringComparison.Ordinal);
        int createWindowStart = coordinator.IndexOf("private MainWindow? CreateWindow", StringComparison.Ordinal);

        Assert.True(startupStart >= 0);
        Assert.True(createWindowStart > startupStart);
        string startupMethod = coordinator[startupStart..createWindowStart];

        Assert.Contains("DateTime wallClockNow = DateTime.Now;", startupMethod, StringComparison.Ordinal);
        Assert.Contains("IsRestorableActiveSession(session.Session, wallClockNow)", startupMethod, StringComparison.Ordinal);
        Assert.Contains("this.CreateWindow(session.SessionId, session.Session)", startupMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("session.Session != null && this.CreateWindow", startupMethod, StringComparison.Ordinal);

        int validationCall = startupMethod.IndexOf(
            "IsRestorableActiveSession(session.Session, wallClockNow)",
            StringComparison.Ordinal);
        int restoredAssignment = startupMethod.IndexOf("restoredAny = true;", validationCall, StringComparison.Ordinal);
        Assert.True(restoredAssignment > validationCall);
    }

    [Fact]
    public void CoordinatorTrayTargetTracksActivationNotViewModelRefresh()
    {
        string coordinator = File.ReadAllText(FindRepositoryFile("src/Hourglass.Linux.Avalonia/TimerWindowCoordinator.cs"));
        int propertyChangedStart = coordinator.IndexOf(
            "private void ViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)",
            StringComparison.Ordinal);
        int activatedStart = coordinator.IndexOf("private void WindowActivated(object? sender, EventArgs e)", StringComparison.Ordinal);
        int closedStart = coordinator.IndexOf("private void WindowClosed(object? sender, EventArgs e)", StringComparison.Ordinal);

        Assert.True(propertyChangedStart >= 0);
        Assert.True(activatedStart > propertyChangedStart);
        Assert.True(closedStart > activatedStart);
        string propertyChangedMethod = coordinator[propertyChangedStart..activatedStart];
        string activatedMethod = coordinator[activatedStart..closedStart];

        Assert.DoesNotContain("this.mostRecentWindow = registration;", propertyChangedMethod, StringComparison.Ordinal);
        Assert.Contains("this.ApplyDesktopProgress();", propertyChangedMethod, StringComparison.Ordinal);
        Assert.Contains("this.ApplyStatusIconState();", propertyChangedMethod, StringComparison.Ordinal);
        Assert.Contains("this.mostRecentWindow = registration;", activatedMethod, StringComparison.Ordinal);
    }

    [Fact]
    public void CoordinatorTrayExitCollectsOneApplicationApprovalBeforeClosingWindows()
    {
        string coordinator = File.ReadAllText(FindRepositoryFile("src/Hourglass.Linux.Avalonia/TimerWindowCoordinator.cs"));
        int exitCaseStart = coordinator.IndexOf("case StatusIconAction.Exit:", StringComparison.Ordinal);
        int closeAllStart = coordinator.IndexOf("private async Task CloseAllWindowsAsync()", StringComparison.Ordinal);
        int targetStart = coordinator.IndexOf("private WindowRegistration? GetStatusIconTarget()", StringComparison.Ordinal);

        Assert.True(exitCaseStart >= 0);
        Assert.True(closeAllStart > exitCaseStart);
        Assert.True(targetStart > closeAllStart);
        string exitCase = coordinator[exitCaseStart..closeAllStart];
        string closeAllMethod = coordinator[closeAllStart..targetStart];

        Assert.Contains("_ = this.CloseAllWindowsAsync();", exitCase, StringComparison.Ordinal);
        Assert.Contains("this.exitCloseInProgress", closeAllMethod, StringComparison.Ordinal);
        Assert.Contains("snapshot.Any(registration => registration.Window.RequiresExitConfirmation)", closeAllMethod, StringComparison.Ordinal);
        Assert.Contains("var dialog = new ExitConfirmationWindow();", closeAllMethod, StringComparison.Ordinal);
        Assert.Contains("await dialog.ShowDialog<bool>(owner.Window).ConfigureAwait(true);", closeAllMethod, StringComparison.Ordinal);
        Assert.Contains("if (!approved)", closeAllMethod, StringComparison.Ordinal);
        Assert.Contains("return;", closeAllMethod, StringComparison.Ordinal);
        Assert.Contains("registration.Window.CloseWithPreapprovedExit();", closeAllMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("registration.Window.Close();", closeAllMethod, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindowCoordinatorApprovedCloseBypassesOnlyExitPrompt()
    {
        string window = File.ReadAllText(FindRepositoryFile("src/Hourglass.Linux.Avalonia/MainWindow.axaml.cs"));
        int approvedCloseStart = window.IndexOf("internal void CloseWithPreapprovedExit()", StringComparison.Ordinal);
        int createServicesStart = window.IndexOf("private static DefaultMainWindowServices CreateDefaultServices()", StringComparison.Ordinal);
        int approvalStart = window.IndexOf("private async Task<bool> RequestCloseApprovalAsync()", StringComparison.Ordinal);
        int prepareStart = window.IndexOf("private async Task PrepareCloseAsync()", StringComparison.Ordinal);

        Assert.True(approvedCloseStart >= 0);
        Assert.True(createServicesStart > approvedCloseStart);
        Assert.True(approvalStart > createServicesStart);
        Assert.True(prepareStart > approvalStart);
        string approvedCloseMethod = window[approvedCloseStart..createServicesStart];
        string approvalMethod = window[approvalStart..prepareStart];

        Assert.Contains("internal bool RequiresExitConfirmation => this.viewModel.ShouldPromptOnExit;", window, StringComparison.Ordinal);
        Assert.Contains("this.closeApprovalPreapproved = true;", approvedCloseMethod, StringComparison.Ordinal);
        Assert.Contains("this.Close();", approvedCloseMethod, StringComparison.Ordinal);
        Assert.Contains("if (this.closeApprovalPreapproved)", approvalMethod, StringComparison.Ordinal);
        Assert.Contains("this.closeApprovalPreapproved = false;", approvalMethod, StringComparison.Ordinal);
        Assert.Contains("return true;", approvalMethod, StringComparison.Ordinal);
        Assert.Contains("new ExitConfirmationWindow()", approvalMethod, StringComparison.Ordinal);
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
