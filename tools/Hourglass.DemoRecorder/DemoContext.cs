using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Hourglass.DemoRecorder.Services;
using Hourglass.Linux.Avalonia;
using Hourglass.Platform;
using Hourglass.Timing;

namespace Hourglass.DemoRecorder;

public sealed class DemoContext : IAsyncDisposable
{
    private static readonly FontFamily DemoFontFamily = new("avares://Avalonia.Fonts.Inter/Assets#Inter");

    internal static ApplicationInfo DemoApplicationInfo { get; } = new(
        ProductName: "Hourglass Linux",
        Description: "A simple, polished timer for Linux.",
        Version: "README demo",
        InformationalVersion: "README demo",
        BuildConfiguration: "Demo",
        SourceRevision: "demo",
        RuntimeDescription: ".NET",
        OperatingSystemDescription: "Linux",
        ProcessArchitecture: "x64",
        DeveloperName: "Matt Bunch",
        RepositoryUri: new Uri("https://github.com/MattBunch/hourglass-linux"),
        DeveloperWebsiteUri: new Uri("https://mattbunch.dev"),
        OriginalProjectUri: new Uri("http://chris.dziemborowicz.com/apps/hourglass/"),
        LicenseName: "MIT");

    private readonly DemoRecorderOptions options;
    private readonly FrameRecorder frameRecorder;
    private readonly DemoPlatformServices services;
    private readonly MainWindowViewModel viewModel;
    private AboutWindow? aboutWindow;
    private bool disposed;

    private DemoContext(DemoRecorderOptions options, FrameRecorder frameRecorder, DemoPlatformServices services)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.frameRecorder = frameRecorder ?? throw new ArgumentNullException(nameof(frameRecorder));
        this.services = services ?? throw new ArgumentNullException(nameof(services));
        if (Avalonia.Application.Current != null)
        {
            Avalonia.Application.Current.RequestedThemeVariant = ThemeVariant.Dark;
        }

        this.viewModel = new MainWindowViewModel(
            new CountdownEngine(services.Clock),
            () => services.Clock.WallClockNow,
            services.NotificationService,
            services.SessionInhibitor,
            services.SettingsStore,
            new DirectAppSettingsStore(services.SettingsStore),
            new DirectSavedTimersStore(services.SettingsStore),
            services.SoundService,
            services.SystemPowerService,
            statusIconSupported: false,
            statusIconCanRecoverHiddenWindow: false,
            persistActiveSessionDirectly: false,
            restoreActiveSessionOnLoad: false,
            uiDispatcher: AvaloniaUiDispatcher.Instance);
        this.Window = new MainWindow(
            this.viewModel,
            services.DesktopProgressService,
            services.StatusIconService,
            new ApplicationInfoProvider(typeof(App).Assembly),
            services.ExternalUriLauncher,
            createWindowAttentionService: _ => services.WindowAttentionService,
            loadSettingsOnOpened: false,
            suppressExpiryVisualFeedback: true,
            suppressCommandPanelTransitions: true);
        this.Window.Width = options.Width;
        this.Window.Height = options.Height;
        this.Window.CanResize = false;
        ApplyDemoFont(this.Window);
    }

    public MainWindow Window { get; }

    public int FrameCount => this.frameRecorder.FrameCount;

    public DemoPlatformServices Services => this.services;

    public static Task<DemoContext> CreateAsync(
        DemoRecorderOptions options,
        FrameRecorder frameRecorder,
        DemoPlatformServices services)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            return Task.FromResult(new DemoContext(options, frameRecorder, services));
        }

        return Dispatcher.UIThread.InvokeAsync(() => new DemoContext(options, frameRecorder, services)).GetTask();
    }

    public async Task ShowWindowAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        this.Window.Show();
        await this.viewModel.LoadSettingsAsync(cancellationToken).ConfigureAwait(true);
        await this.FlushAsync(cancellationToken).ConfigureAwait(true);
    }

    public Task TypeTimerInputAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        cancellationToken.ThrowIfCancellationRequested();
        this.viewModel.TimerInput = text;
        return this.FlushAsync(cancellationToken);
    }

    public Task SetTimerTitleAsync(string title, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(title);
        cancellationToken.ThrowIfCancellationRequested();
        this.viewModel.TimerTitle = title;
        return this.FlushAsync(cancellationToken);
    }

    public Task PressEnterAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (this.viewModel.StartCommand.CanExecute(null))
        {
            this.viewModel.StartCommand.Execute(null);
        }

        return this.FlushAsync(cancellationToken);
    }

    public Task PauseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (this.viewModel.PauseResumeCommand.CanExecute(null))
        {
            this.viewModel.PauseResumeCommand.Execute(null);
        }

        return this.FlushAsync(cancellationToken);
    }

    public Task ResumeAsync(CancellationToken cancellationToken = default)
    {
        return this.PauseAsync(cancellationToken);
    }

    public Task OpenContextMenuAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Grid? root = this.Window.FindControl<Grid>("RootGrid");
        root?.ContextMenu?.Open(root);
        return this.FlushAsync(cancellationToken);
    }

    public Task CloseContextMenuAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Grid? root = this.Window.FindControl<Grid>("RootGrid");
        root?.ContextMenu?.Close();
        return this.FlushAsync(cancellationToken);
    }

    public Task ToggleAlwaysOnTopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (this.viewModel.ToggleAlwaysOnTopCommand.CanExecute(null))
        {
            this.viewModel.ToggleAlwaysOnTopCommand.Execute(null);
        }

        return this.FlushAsync(cancellationToken);
    }

    public async Task OpenAboutAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        this.aboutWindow ??= new AboutWindow(DemoApplicationInfo, this.services.ExternalUriLauncher)
        {
            Width = 460,
            Height = 430,
            CanResize = false
        };
        ApplyDemoFont(this.aboutWindow);
        this.aboutWindow.Show(this.Window);
        await this.FlushAsync(cancellationToken).ConfigureAwait(true);
    }

    public async Task AdvanceAsync(TimeSpan duration, CancellationToken cancellationToken = default)
    {
        if (duration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        int frames = (int)Math.Round(duration.TotalSeconds * this.options.FrameRate, MidpointRounding.AwayFromZero);
        if (frames == 0)
        {
            if (duration > TimeSpan.Zero)
            {
                cancellationToken.ThrowIfCancellationRequested();
                this.services.Clock.Advance(duration);
                this.viewModel.Tick();
            }

            return;
        }

        long baseStepTicks = Math.DivRem(duration.Ticks, frames, out long remainderTicks);
        long advancedTicks = 0;
        for (int frame = 0; frame < frames; frame++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long targetTicks = (baseStepTicks * (frame + 1)) + ((remainderTicks * (frame + 1)) / frames);
            TimeSpan step = TimeSpan.FromTicks(targetTicks - advancedTicks);
            this.services.Clock.Advance(step);
            advancedTicks = targetTicks;
            this.viewModel.Tick();
            await this.CaptureFrameAsync(cancellationToken).ConfigureAwait(true);
        }
    }

    public Task HoldAsync(TimeSpan duration, CancellationToken cancellationToken = default)
    {
        return this.AdvanceAsync(duration, cancellationToken);
    }

    public async Task CaptureFrameAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await this.FlushAsync(cancellationToken).ConfigureAwait(true);
        this.frameRecorder.Capture(this.aboutWindow?.IsVisible == true ? this.aboutWindow : this.Window);
    }

    private static void ApplyDemoFont(Control control)
    {
        if (control is TextBlock textBlock)
        {
            textBlock.FontFamily = DemoFontFamily;
        }
        else if (control is TemplatedControl templatedControl)
        {
            templatedControl.FontFamily = DemoFontFamily;
        }

        foreach (Avalonia.Visual child in control.GetVisualChildren())
        {
            if (child is Control childControl)
            {
                ApplyDemoFont(childControl);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (this.disposed)
        {
            return;
        }

        if (!Dispatcher.UIThread.CheckAccess())
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    await this.DisposeOnUiThreadAsync().ConfigureAwait(true);
                    completion.SetResult();
                }
                catch (Exception exception)
                {
                    completion.SetException(exception);
                }
            });

            await completion.Task.ConfigureAwait(true);
            return;
        }

        await this.DisposeOnUiThreadAsync().ConfigureAwait(true);
    }

    private async Task DisposeOnUiThreadAsync()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        await this.FlushAsync(CancellationToken.None).ConfigureAwait(true);
        this.aboutWindow?.Close();
        await this.Window.CloseCoordinatedWithPreapprovedExitAsync().ConfigureAwait(true);
        await this.FlushAsync(CancellationToken.None).ConfigureAwait(true);
    }

    private Task FlushAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Dispatcher.UIThread.RunJobs();
        Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        return Task.CompletedTask;
    }
}
