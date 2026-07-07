using Hourglass.Platform;
using Hourglass.Timing;

namespace Hourglass.Linux.Avalonia;

internal static class LinuxCommandLineParser
{
    public static CommandLineParseResult Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        string[] copiedArgs = args.ToArray();
        if (copiedArgs.Length == 0)
        {
            return CommandLineParseResult.Success(new SingleInstanceLaunchRequest(
                SingleInstanceLaunchRequestKind.Activate,
                copiedArgs));
        }

        string? title = null;
        var timerParts = new List<string>();
        for (int index = 0; index < copiedArgs.Length; index++)
        {
            string arg = copiedArgs[index];
            if (timerParts.Count == 0 && (arg == "--title" || arg == "-t"))
            {
                if (title != null)
                {
                    return CommandLineParseResult.Failure("The --title option can only be specified once.");
                }

                if (index + 1 >= copiedArgs.Length)
                {
                    return CommandLineParseResult.Failure("The --title option requires a value.");
                }

                title = copiedArgs[++index];
                continue;
            }

            if (timerParts.Count == 0 && arg.StartsWith("-", StringComparison.Ordinal))
            {
                return CommandLineParseResult.Failure($"Unrecognized option: {arg}");
            }

            timerParts.Add(arg);
        }

        if (timerParts.Count == 0)
        {
            return CommandLineParseResult.Failure("A timer expression is required when --title is specified.");
        }

        string timerInput = string.Join(" ", timerParts).Trim();
        TimerStart? timerStart = TimerStart.FromString(timerInput);
        if (timerStart == null || !timerStart.IsValid)
        {
            return CommandLineParseResult.Failure($"Invalid timer expression: {timerInput}");
        }

        return CommandLineParseResult.Success(new SingleInstanceLaunchRequest(
            SingleInstanceLaunchRequestKind.StartTimer,
            copiedArgs,
            timerInput,
            title));
    }
}

internal sealed record CommandLineParseResult(
    bool IsSuccess,
    SingleInstanceLaunchRequest? Request,
    string? ErrorMessage)
{
    public static CommandLineParseResult Success(SingleInstanceLaunchRequest request)
    {
        return new CommandLineParseResult(true, request, null);
    }

    public static CommandLineParseResult Failure(string errorMessage)
    {
        return new CommandLineParseResult(false, null, errorMessage);
    }
}
