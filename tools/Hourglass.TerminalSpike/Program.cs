using System.CommandLine;
using System.Diagnostics;
using System.Text.Json;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Hourglass.TerminalSpike;

internal static class Program
{
    public static int Main(string[] args)
    {
        if (args is ["--verify-parser"])
        {
            return VerifyParserAsync().GetAwaiter().GetResult();
        }

        Option<int> hz = new("--hz") { DefaultValueFactory = _ => 5 };
        hz.Validators.Add(result =>
        {
            if (result.GetValueOrDefault<int>() is not (4 or 5 or 10))
            {
                result.AddError("Refresh frequency must be 4, 5 or 10 Hz.");
            }
        });
        Option<bool> fail = new("--throw");
        Option<string?> evidence = new("--evidence");
        Option<string?> echo = new("--echo");
        Option<bool> editor = new("--editor");
        RootCommand command = new("Isolated DEV-18 toolkit spike; no Hourglass timer logic.");
        command.Options.Add(hz);
        command.Options.Add(fail);
        command.Options.Add(evidence);
        command.Options.Add(echo);
        command.Options.Add(editor);
        command.SetAction(result =>
        {
            if (result.GetValue(echo) is string value)
            {
                Console.WriteLine(JsonSerializer.Serialize(new { value, hz = result.GetValue(hz) }));
                return 0;
            }

            return RunTerminal(result.GetValue(hz), result.GetValue(fail), result.GetValue(evidence), result.GetValue(editor));
        });
        return command.Parse(args).Invoke();
    }

    private static async Task<int> VerifyParserAsync()
    {
        using StringWriter output = new();
        using StringWriter error = new();
        InvocationConfiguration configuration = new() { Output = output, Error = error };
        Option<string> value = new("--value") { Required = true };
        RootCommand command = new();
        command.Options.Add(value);
        command.SetAction(result =>
        {
            result.InvocationConfiguration.Output.Write(result.GetValue(value));
            return 0;
        });
        int success = command.Parse(["--value", "Tea time"]).Invoke(configuration);
        if (success != 0 || output.ToString() != "Tea time" || error.ToString().Length != 0)
        {
            throw new InvalidOperationException("Parser output contract failed.");
        }

        output.GetStringBuilder().Clear();
        int invalid = command.Parse(["--unknown"]).Invoke(configuration);
        if (invalid == 0 || error.ToString().Length == 0)
        {
            throw new InvalidOperationException("Parser validation contract failed.");
        }

        using CancellationTokenSource cancellation = new();
        command.SetAction(async (_, token) =>
        {
            TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            using CancellationTokenRegistration registration = token.Register(() => completion.TrySetCanceled(token));
            cancellation.Cancel();
            try
            {
                await completion.Task;
                return 1;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return 130;
            }
        });
        int interrupted = await command.Parse(["--value", "unused"]).InvokeAsync(configuration, cancellation.Token);
        if (interrupted != 130)
        {
            throw new InvalidOperationException("Parser cancellation contract failed.");
        }

        Console.WriteLine("Parser validation, injected streams and cancellation passed.");
        return 0;
    }

    private static int RunTerminal(int hz, bool fail, string? evidencePath, bool editor)
    {
        List<string> keys = [];
        List<string> sizes = [];
        int updates = 0;
        bool cleanedUp = false;
        string? failure = null;
        string? editedText = null;
        Stopwatch elapsed = Stopwatch.StartNew();
        try
        {
            using (IApplication app = Application.Create())
            {
                app.Init();
                using Window window = new() { Title = "Hourglass DEV-18 toolkit spike" };
                Label clock = new() { Text = "Updates: 0", X = 0, Y = 0, Width = Dim.Fill() };
                Label status = new() { Text = "Space Esc Ctrl+P Ctrl+S Ctrl+R Tab Shift+Tab Alt+Enter; q quits", X = 0, Y = 2, Width = Dim.Fill() };
                window.Add(clock, status);
                using TextField input = new() { X = 0, Y = 4, Width = Dim.Fill() };
                if (editor)
                {
                    window.Add(input);
                    input.SetFocus();
                }

                app.Keyboard.KeyDown += (_, key) =>
                {
                    keys.Add(key.ToString());
                    status.Text = $"Key: {key}";
                    if ((!editor && key == Key.Q) || (editor && key == Key.Q.WithCtrl))
                    {
                        editedText = input.Text;
                        app.RequestStop();
                        key.Handled = true;
                    }

                    // Record delivery, including keys whose toolkit defaults would quit.
                    if (!editor)
                    {
                        key.Handled = true;
                    }
                };
                app.AddTimeout(TimeSpan.FromSeconds(1d / hz), () =>
                {
                    updates++;
                    clock.Text = $"Updates: {updates} at {hz} Hz";
                    string size = app.Screen.ToString();
                    if (sizes.Count == 0 || sizes[^1] != size)
                    {
                        sizes.Add(size);
                    }

                    if (fail && updates == hz)
                    {
                        throw new InvalidOperationException("Intentional DEV-18 spike failure.");
                    }

                    return true;
                });
                app.Run(window);
            }

            cleanedUp = true;
            return 0;
        }
        catch (Exception exception)
        {
            // The using scope has disposed the toolkit before reporting the failure.
            cleanedUp = true;
            failure = exception.Message;
            Console.Error.WriteLine(exception.Message);
            return 70;
        }
        finally
        {
            if (evidencePath != null)
            {
                File.WriteAllText(evidencePath, JsonSerializer.Serialize(new
                {
                    hz,
                    updates,
                    elapsedMilliseconds = elapsed.ElapsedMilliseconds,
                    keys,
                    sizes,
                    cleanedUp,
                    failure,
                    editedText
                }, new JsonSerializerOptions { WriteIndented = true }));
            }
        }
    }
}
