namespace Hourglass.Cli;

using System.Globalization;
using System.Text;
using System.Text.Json;
using Hourglass.Application;
using Hourglass.Settings;

public enum CliOutputMode { Human, Plain, Json }

public sealed record CliSession(string SessionId, long Revision, string State, string Input, string Title,
    double? RemainingMilliseconds, double? ElapsedMilliseconds, double? TotalMilliseconds,
    double ProgressPercent, TimerDefaults Options, ApplicationPreferences Preferences, string Lifetime)
{
    public static CliSession FromSnapshot(TimerSessionSnapshot session) => new(session.SessionId, session.Revision,
        session.Countdown.State.ToString().ToLowerInvariant(), session.TimerInput, session.TimerTitle,
        session.Countdown.TimeLeft?.TotalMilliseconds, session.Countdown.TimeElapsed?.TotalMilliseconds, session.Countdown.TotalTime?.TotalMilliseconds,
        TimerDisplay.GetProgressPercent(session.Countdown, session.Options.ReverseProgressBar), session.Options, session.Preferences, session.Lifetime.ToString().ToLowerInvariant());
}

internal sealed class CliOutput(TextWriter output, TextWriter error, CliOutputMode mode)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public int Failure(string command, ApplicationError failure, int? exitCode = null)
    {
        int code = exitCode ?? ExitCode(failure.Code);
        error.WriteLine(mode == CliOutputMode.Json
            ? JsonSerializer.Serialize(new { schemaVersion = 1, command, error = new { code, kind = failure.Code.ToString(), message = failure.Message } }, JsonOptions)
            : Escape(failure.Message));
        return code;
    }

    public int Sessions(string command, IEnumerable<TimerSessionSnapshot> sessions, string? outcome = null)
    {
        CliSession[] values = sessions.OrderBy(session => session.SessionId, StringComparer.Ordinal).Select(CliSession.FromSnapshot).ToArray();
        if (mode == CliOutputMode.Json)
        {
            output.WriteLine(JsonSerializer.Serialize(new { schemaVersion = 1, command, result = new { sessions = values, outcome } }, JsonOptions));
        }
        else if (mode == CliOutputMode.Plain)
        {
            output.WriteLine("sessionId\tstate\ttitle\tinput\tremainingMilliseconds\telapsedMilliseconds\ttotalMilliseconds\tprogressPercent\trevision\tlifetime");
            foreach (CliSession session in values)
            {
                output.WriteLine(string.Join('\t', Escape(session.SessionId), session.State, Escape(session.Title), Escape(session.Input),
                    Number(session.RemainingMilliseconds), Number(session.ElapsedMilliseconds), Number(session.TotalMilliseconds),
                    Number(session.ProgressPercent), session.Revision.ToString(CultureInfo.InvariantCulture), session.Lifetime));
            }
        }
        else
        {
            if (values.Length == 0) { output.WriteLine("No live sessions."); }
            foreach (CliSession session in values)
            {
                output.WriteLine($"{Escape(session.SessionId)}  {session.State} [{session.Lifetime}]  {Escape(session.Title)}  {Escape(session.Input)}  {Number(session.RemainingMilliseconds)} ms remaining");
            }
        }
        return 0;
    }

    public int Version(string version)
    {
        output.WriteLine(mode == CliOutputMode.Json ? JsonSerializer.Serialize(new { schemaVersion = 1, command = "version", result = new { version } }, JsonOptions) : version);
        return 0;
    }

    public int Data(string command, object result, IEnumerable<string> lines)
    {
        if (mode == CliOutputMode.Json)
        {
            output.WriteLine(JsonSerializer.Serialize(new { schemaVersion = 1, command, result }, JsonOptions));
        }
        else
        {
            foreach (string line in lines) { output.WriteLine(line); }
        }
        return 0;
    }

    public void Diagnostic(ApplicationDiagnostic diagnostic)
    {
        error.WriteLine(mode == CliOutputMode.Json
            ? JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                diagnostic = new
                {
                    severity = diagnostic.Severity.ToString(),
                    diagnostic.Category,
                    diagnostic.Operation,
                    diagnostic.Message
                }
            }, JsonOptions)
            : $"{Escape(diagnostic.Category)}: {Escape(diagnostic.Message)}");
    }

    public static int ExitCode(ApplicationErrorCode code) => code switch
    {
        ApplicationErrorCode.Validation => 2,
        ApplicationErrorCode.NotFound => 3,
        ApplicationErrorCode.RuntimeUnavailable => 4,
        ApplicationErrorCode.TransportFailure => 5,
        ApplicationErrorCode.Unsupported => 6,
        ApplicationErrorCode.PersistenceFailure => 7,
        ApplicationErrorCode.Locked or ApplicationErrorCode.Conflict or ApplicationErrorCode.InvalidTransition => 8,
        _ => 1
    };

    private static string Number(double? value) => value?.ToString("R", CultureInfo.InvariantCulture) ?? string.Empty;

    public static string Escape(string value)
    {
        StringBuilder result = new();
        foreach (char character in value)
        {
            result.Append(character switch
            {
                '\\' => "\\\\",
                '\t' => "\\t",
                '\r' => "\\r",
                '\n' => "\\n",
                _ when char.IsControl(character) => $"\\u{(int)character:x4}",
                _ => character.ToString()
            });
        }
        return result.ToString();
    }
}
