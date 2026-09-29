namespace Hourglass.Tui;

using Hourglass.Application;
using Hourglass.Settings;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

/// <summary>Terminal.Gui owns only views and input routing; the controller owns presentation state.</summary>
public sealed class TuiShell(TuiController controller, CancellationToken interruption, Action? tickHook = null)
{
    private volatile bool running;
    private bool cleanQuit;
    private bool closing;
    private string? loadedDraftId;
    private long loadedDraftRevision = -1;
    private TuiMode loadedMode = TuiMode.Dashboard;

    public bool Run()
    {
        using IApplication app = Application.Create();
        app.Init();
        using Window window = new() { Title = "Hourglass" };
        using Label list = new() { X = 0, Y = 0, Width = 24, Height = Dim.Fill(4) };
        using Label detail = new() { X = 25, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(4) };
        using Label status = new() { X = 0, Y = 0, Width = Dim.Fill(), Height = 3 };
        using Label editorLabel = new() { X = 0, Y = 2, Width = Dim.Fill(), Height = 1, Text = "Expression:" };
        using TextField input = new() { X = 0, Y = 3, Width = Dim.Fill() };
        using Label titleLabel = new() { X = 0, Y = 5, Width = Dim.Fill(), Height = 1, Text = "Title:" };
        using TextField title = new() { X = 0, Y = 6, Width = Dim.Fill() };
        window.Add(list, detail, status, editorLabel, input, titleLabel, title);

        void Render(TuiState state)
        {
            if (!this.running) { return; }
            int width = app.Screen.Width;
            int height = app.Screen.Height;
            bool editing = state.Mode is TuiMode.New or TuiMode.Edit or TuiMode.Save;
            bool menu = state.Mode is TuiMode.Saved or TuiMode.Recent or TuiMode.Settings or TuiMode.Options;
            bool narrow = width < 80;
            bool tiny = width < 40 || height < 12;
            list.Visible = !narrow && !tiny && !editing && !menu && state.Mode == TuiMode.Dashboard;
            detail.X = list.Visible ? 25 : 0;
            detail.Visible = !editing || tiny;
            editorLabel.Visible = editing && !tiny;
            input.Visible = editing && !tiny;
            titleLabel.Visible = editing && !tiny;
            title.Visible = editing && !tiny;
            status.Y = Math.Max(0, height - 5);

            if (editing && state.Draft is TuiDraft draft && (this.loadedMode != state.Mode || this.loadedDraftId != draft.SessionId || this.loadedDraftRevision != draft.Revision))
            {
                input.Text = draft.TimerInput;
                title.Text = draft.TimerTitle;
                input.SetFocus();
                this.loadedDraftId = draft.SessionId;
                this.loadedDraftRevision = draft.Revision;
            }
            this.loadedMode = state.Mode;

            if (tiny)
            {
                detail.Text = "Terminal too small. Resize for timers.\n? help   q quit";
            }
            else if (state.Mode == TuiMode.Help)
            {
                detail.Text = "n new   e edit   Enter start\nSpace/Ctrl+P pause or resume\ns/Ctrl+S stop   r/Ctrl+R restart\nTab/Shift+Tab select   Esc dismiss\nv saved   c recent   , settings   o options\nu unlock   q/Ctrl+Q quit\nEsc returns to the dashboard";
            }
            else if (state.Mode == TuiMode.ConfirmQuit)
            {
                detail.Text = "Close your running timers and quit?\ny confirm   n/Esc cancel";
            }
            else if (state.Mode == TuiMode.Conflict)
            {
                detail.Text = "The session changed while editing.\nl reload current values   c keep your draft";
            }
            else if (state.Mode == TuiMode.ConfirmChange)
            {
                detail.Text = $"Confirm {controller.PendingChange}?\ny confirm   n/Esc cancel";
            }
            else if (editing)
            {
                detail.Text = string.Empty;
            }
            else if (menu)
            {
                string[] items = state.Mode switch
                {
                    TuiMode.Saved => controller.SavedTimers.Select(timer => $"{timer.Header} [{timer.Id}]").ToArray(),
                    TuiMode.Recent => controller.RecentInputs.ToArray(),
                    _ => SharedOptionRegistry.Keys.Select(key => $"{key} = {SharedOptionRegistry.Get(
                        state.Mode == TuiMode.Options && state.Selected is TimerSessionSnapshot session
                            ? new LinuxSettingsSnapshot([], session.Preferences, session.Options).ToSettings() : controller.Settings, key)}").ToArray()
                };
                string heading = state.Mode.ToString();
                int first = Math.Clamp(controller.MenuIndex - Math.Max(1, height - 9), 0, Math.Max(0, items.Length - 1));
                detail.Text = heading + "\n" + (items.Length == 0 ? "(empty)" : string.Join('\n', items.Skip(first).Take(Math.Max(1, height - 8))
                    .Select((item, index) => $"{(index + first == controller.MenuIndex ? '>' : ' ')} {item}")))
                    + (state.Mode switch
                    {
                        TuiMode.Saved => "\nEnter run  A run all  a add  d delete  x clear",
                        TuiMode.Recent => "\nEnter use  x clear",
                        _ => "\nEnter toggle/cycle  p preview sound"
                    });
            }
            else if (state.Selected is TimerSessionSnapshot selected)
            {
                bool elapsedMode = selected.Options.ShowTimeElapsed;
                TimeSpan? time = elapsedMode ? selected.Countdown.TimeElapsed : selected.Countdown.TimeLeft;
                string display = time is TimeSpan value ? TimerDisplay.FormatTimerTime(value) : "--:--:--";
                double progress = TimerDisplay.GetProgressPercent(selected.Countdown, selected.Options.ReverseProgressBar);
                detail.Text = $"{selected.TimerTitle}\n{selected.Countdown.State}   {(elapsedMode ? "Elapsed" : "Remaining")}: {display}\nProgress: {progress:0}%\n{selected.TimerInput}\n\nEnter start   Space pause/resume\ns stop   r restart   e edit\nEsc dismiss   u unlock   ? help";
            }
            else { detail.Text = "No live timers. Press n to create one.\n? help   q quit"; }

            if (list.Visible)
            {
                list.Text = "Sessions\n" + string.Join('\n', state.Sessions.Take(Math.Max(1, height - 8)).Select(session =>
                    $"{(session.SessionId == state.SelectedId ? '>' : ' ')} {session.TimerTitle} [{session.Countdown.State}]"));
            }
            string modeHelp = editing ? "Enter save   Tab fields   Esc cancel" : menu ? "Up/Down select   Esc back   q quit" : "n new   Tab select   ? help   q quit";
            status.Text = $"{(state.Busy ? "Working..." : modeHelp)}\n{state.Error ?? string.Empty}";
        }

        void Changed(TuiState state)
        {
            if (this.running) { app.Invoke(() => Render(state)); }
        }

        async Task ExecuteAsync(Func<Task> action)
        {
            try { await action().ConfigureAwait(false); }
            catch (OperationCanceledException) when (interruption.IsCancellationRequested) { }
            catch (Exception exception) { controller.ShowError(exception.Message); }
        }

        async Task QuitAsync()
        {
            if (this.closing) { return; }
            this.closing = true;
            try
            {
                bool closed = await controller.RequestQuitAsync().ConfigureAwait(false);
                if (closed) { this.cleanQuit = true; app.Invoke(app.RequestStop); }
                else if (controller.State.Mode != TuiMode.ConfirmQuit) { app.Invoke(app.RequestStop); }
            }
            finally { this.closing = false; }
        }

        app.Keyboard.KeyDown += OnKeyDown;
        controller.Changed += Changed;
        using CancellationTokenRegistration stop = interruption.Register(() => app.Invoke(app.RequestStop));
        this.running = true;
        try
        {
            Render(controller.State);
            app.AddTimeout(TimeSpan.FromMilliseconds(200), () =>
            {
                if (!this.running) { return false; }
                tickHook?.Invoke();
                _ = ExecuteAsync(() => controller.RefreshAsync(interruption));
                Render(controller.State);
                return true;
            });
            app.Run(window);
            return this.cleanQuit;
        }
        finally
        {
            this.running = false;
            controller.Changed -= Changed;
            app.Keyboard.KeyDown -= OnKeyDown;
        }

        void OnKeyDown(object? sender, Key key)
        {
            TuiMode mode = controller.State.Mode;
            if (key == Key.Q.WithCtrl) { key.Handled = true; _ = ExecuteAsync(QuitAsync); return; }
            if ((app.Screen.Width < 40 || app.Screen.Height < 12) && key == Key.Q)
            {
                key.Handled = true;
                _ = ExecuteAsync(QuitAsync);
                return;
            }
            if (mode is TuiMode.New or TuiMode.Edit or TuiMode.Save)
            {
                if (key == Key.Esc) { controller.Back(); key.Handled = true; }
                else if (key == Key.Enter)
                {
                    controller.SetDraft(input.Text, title.Text);
                    key.Handled = true;
                    _ = ExecuteAsync(() => mode == TuiMode.Save ? controller.SaveDraftAsync(interruption) : controller.SubmitDraftAsync(interruption));
                }
                return;
            }
            if (mode == TuiMode.Conflict)
            {
                if (key == Key.L) { controller.ResolveConflict(reload: true); key.Handled = true; }
                else if (key == Key.C || key == Key.Esc) { controller.ResolveConflict(reload: false); key.Handled = true; }
                return;
            }
            if (mode == TuiMode.Help)
            {
                if (key == Key.Esc || key == Key.Enter) { controller.Back(); key.Handled = true; }
                return;
            }
            if (mode == TuiMode.ConfirmQuit)
            {
                if (key == Key.Y) { key.Handled = true; _ = ExecuteAsync(QuitAsync); }
                else if (key == Key.N || key == Key.Esc) { controller.Back(); key.Handled = true; }
                return;
            }
            if (mode == TuiMode.ConfirmChange)
            {
                if (key == Key.Y) { key.Handled = true; _ = ExecuteAsync(() => controller.ConfirmChangeAsync(interruption)); }
                else if (key == Key.N || key == Key.Esc) { controller.CancelChange(); key.Handled = true; }
                return;
            }
            if (mode is TuiMode.Saved or TuiMode.Recent or TuiMode.Settings or TuiMode.Options)
            {
                if (key == Key.Esc) { controller.Back(); key.Handled = true; }
                else if (key.ToString() == "?" || key == Key.F1) { controller.ShowHelp(); key.Handled = true; }
                else if (key == Key.CursorDown || key == Key.Tab) { controller.SelectMenu(1); key.Handled = true; }
                else if (key == Key.CursorUp || key == Key.Tab.WithShift) { controller.SelectMenu(-1); key.Handled = true; }
                else if (key == Key.Q) { key.Handled = true; _ = ExecuteAsync(QuitAsync); }
                else if (mode == TuiMode.Saved && key == Key.Enter) { key.Handled = true; _ = ExecuteAsync(() => controller.RunSavedAsync(false, interruption)); }
                else if (mode == TuiMode.Saved && key == Key.A.WithShift) { key.Handled = true; _ = ExecuteAsync(() => controller.RunSavedAsync(true, interruption)); }
                else if (mode == TuiMode.Saved && key == Key.A) { controller.OpenSaveDraft(); key.Handled = true; }
                else if (mode == TuiMode.Saved && key == Key.D && controller.MenuIndex < controller.SavedTimers.Length)
                { controller.RequestChange("saved:" + controller.SavedTimers[controller.MenuIndex].Id); key.Handled = true; }
                else if (mode == TuiMode.Saved && key == Key.X) { controller.RequestChange("saved:clear"); key.Handled = true; }
                else if (mode == TuiMode.Recent && key == Key.Enter) { controller.UseRecent(); key.Handled = true; }
                else if (mode == TuiMode.Recent && key == Key.X) { controller.RequestChange("recent:clear"); key.Handled = true; }
                else if (mode is TuiMode.Settings or TuiMode.Options && key == Key.Enter)
                { key.Handled = true; _ = ExecuteAsync(() => controller.ToggleSettingAsync(interruption)); }
                else if (mode is TuiMode.Settings or TuiMode.Options && key == Key.P)
                { key.Handled = true; _ = ExecuteAsync(() => controller.PreviewSoundAsync(interruption)); }
                return;
            }
            if (key == Key.N) { controller.OpenNew(); key.Handled = true; }
            else if (key == Key.E) { controller.OpenEdit(); key.Handled = true; }
            else if (key == Key.V) { key.Handled = true; _ = ExecuteAsync(() => controller.OpenSavedAsync(interruption)); }
            else if (key == Key.C) { key.Handled = true; _ = ExecuteAsync(() => controller.OpenRecentAsync(interruption)); }
            else if (key.ToString() == ",") { key.Handled = true; _ = ExecuteAsync(() => controller.OpenSettingsAsync(false, interruption)); }
            else if (key == Key.O) { key.Handled = true; _ = ExecuteAsync(() => controller.OpenSettingsAsync(true, interruption)); }
            else if (key == Key.Tab) { controller.SelectNext(1); key.Handled = true; }
            else if (key == Key.Tab.WithShift) { controller.SelectNext(-1); key.Handled = true; }
            else if (key == Key.Enter) { key.Handled = true; _ = ExecuteAsync(() => controller.ActAsync("start", interruption)); }
            else if (key == Key.Space || key == Key.P.WithCtrl) { key.Handled = true; _ = ExecuteAsync(() => controller.ActAsync("toggle", interruption)); }
            else if (key == Key.S || key == Key.S.WithCtrl) { key.Handled = true; _ = ExecuteAsync(() => controller.ActAsync("stop", interruption)); }
            else if (key == Key.R || key == Key.R.WithCtrl) { key.Handled = true; _ = ExecuteAsync(() => controller.ActAsync("restart", interruption)); }
            else if (key == Key.Esc) { key.Handled = true; _ = ExecuteAsync(() => controller.ActAsync("dismiss", interruption)); }
            else if (key == Key.U) { key.Handled = true; _ = ExecuteAsync(() => controller.ActAsync("unlock", interruption)); }
            else if (key.ToString() == "?" || key == Key.F1) { controller.ShowHelp(); key.Handled = true; }
            else if (key == Key.Q) { key.Handled = true; _ = ExecuteAsync(QuitAsync); }
        }
    }
}
