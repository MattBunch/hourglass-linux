namespace Hourglass.Linux.Avalonia.Tests;

using Hourglass.Platform;
using Xunit;

public sealed class LinuxCommandLineParserTests
{
    [Fact]
    public void EmptyArgumentsCreateActivationRequest()
    {
        CommandLineParseResult result = LinuxCommandLineParser.Parse([]);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Request);
        Assert.Equal(SingleInstanceLaunchRequestKind.Activate, result.Request.Kind);
        Assert.Empty(result.Request.Arguments);
    }

    [Theory]
    [InlineData("10 seconds")]
    [InlineData("10", "seconds")]
    [InlineData("1", "hour", "30", "minutes")]
    public void TimerArgumentsCreateStartTimerRequest(params string[] args)
    {
        CommandLineParseResult result = LinuxCommandLineParser.Parse(args);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Request);
        Assert.Equal(SingleInstanceLaunchRequestKind.StartTimer, result.Request.Kind);
        Assert.Equal(string.Join(" ", args), result.Request.TimerInput);
        Assert.Equal(args, result.Request.Arguments);
    }

    [Theory]
    [InlineData("--title")]
    [InlineData("-t")]
    public void TitleOptionSetsTimerTitle(string titleOption)
    {
        CommandLineParseResult result = LinuxCommandLineParser.Parse([titleOption, "Tea", "5", "minutes"]);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Request);
        Assert.Equal("Tea", result.Request.TimerTitle);
        Assert.Equal("5 minutes", result.Request.TimerInput);
    }

    [Theory]
    [InlineData("not a timer")]
    [InlineData("--title")]
    [InlineData("--title", "Tea")]
    [InlineData("--title", "Tea", "--title", "Coffee", "5 minutes")]
    [InlineData("--unknown", "5 minutes")]
    public void InvalidArgumentsFail(params string[] args)
    {
        CommandLineParseResult result = LinuxCommandLineParser.Parse(args);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Request);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
    }

    [Theory]
    [InlineData("--title", "The --title option requires a value.")]
    [InlineData("--title|Tea", "A timer expression is required when --title is specified.")]
    [InlineData("--title|Tea|--title|Coffee|5 minutes", "The --title option can only be specified once.")]
    [InlineData("--unknown|5 minutes", "Unrecognized option: --unknown")]
    [InlineData("not a timer", "Invalid timer expression: not a timer")]
    public void InvalidArgumentsUseResourceBackedErrorText(string rawArgs, string expectedError)
    {
        string[] args = rawArgs.Split('|');

        CommandLineParseResult result = LinuxCommandLineParser.Parse(args);

        Assert.False(result.IsSuccess);
        Assert.Equal(expectedError, result.ErrorMessage);
    }

    [Fact]
    public void PastAbsoluteTimerArgumentFails()
    {
        CommandLineParseResult result = LinuxCommandLineParser.Parse(
            ["January", "1", "2020"],
            new DateTime(2026, 1, 1, 12, 0, 0));

        Assert.False(result.IsSuccess);
        Assert.Null(result.Request);
        Assert.Equal("Invalid timer expression: January 1 2020", result.ErrorMessage);
    }

    [Fact]
    public void LaunchRequestArgumentsAreDefensiveReadOnlySnapshot()
    {
        string[] args = ["10", "seconds"];
        var request = new SingleInstanceLaunchRequest(
            SingleInstanceLaunchRequestKind.StartTimer,
            args,
            "10 seconds");

        args[0] = "20";

        Assert.Equal(["10", "seconds"], request.Arguments);
        Assert.IsNotType<string[]>(request.Arguments);
    }
}
