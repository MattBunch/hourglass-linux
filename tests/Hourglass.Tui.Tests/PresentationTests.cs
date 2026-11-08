namespace Hourglass.Tui.Tests;

using Xunit;

public sealed class PresentationTests
{
    [Theory]
    [InlineData(false, 200)]
    [InlineData(true, 1000)]
    public void RefreshPolicyDoesNotChangeRuntimeTiming(bool accessible, int milliseconds)
    {
        TuiLaunchOptions options = new(Accessible: accessible);
        Assert.Equal(TimeSpan.FromMilliseconds(milliseconds), options.RefreshInterval);
        Assert.Equal(accessible, options.Monochrome);
    }

    [Fact]
    public void OptionsAcceptFlagsAndEnvironmentButRejectUnknownArguments()
    {
        Assert.True(TuiLaunchOptions.TryParse(["--accessible", "--no-color"], null, out var accessible));
        Assert.True(accessible.Monochrome);
        Assert.True(TuiLaunchOptions.TryParse([], "1", out var plain));
        Assert.True(plain.NoColor);
        Assert.Equal(TimeSpan.FromMilliseconds(200), plain.RefreshInterval);
        Assert.False(TuiLaunchOptions.TryParse(["--unknown"], null, out _));
    }

    [Theory]
    [InlineData(80, 24, true, false)]
    [InlineData(80, 23, false, false)]
    [InlineData(79, 24, false, false)]
    [InlineData(40, 12, false, false)]
    [InlineData(39, 12, false, true)]
    [InlineData(40, 11, false, true)]
    public void LayoutUsesBothDimensions(int width, int height, bool list, bool tiny)
    {
        var layout = TuiLayout.Calculate(width, height, true);
        Assert.Equal(list, layout.ShowSessions);
        Assert.Equal(tiny, layout.Tiny);
        Assert.True(layout.ContentRows > 0);
        Assert.False(TuiLayout.Calculate(width, height, false).ShowSessions);
    }

    [Fact]
    public void SelectionAndLongContentRemainVisible()
    {
        Assert.Equal(7, TuiLayout.FirstVisible(9, 12, 3));
        Assert.Equal(0, TuiLayout.FirstVisible(0, 12, 3));
        Assert.Equal("second\nthird", TuiLayout.Scroll("first\nsecond\nthird\nfourth", 1, 2));
    }

    [Fact]
    public void WrappedErrorsRemainScrollableAndDoNotSplitTextElements()
    {
        Assert.Equal("a☕\nbc", TuiLayout.Wrap("a☕bc", 2));
        Assert.Equal("bc", TuiLayout.Scroll(TuiLayout.Wrap("a☕bc", 2), 1, 1));
    }

    [Fact]
    public void MonochromeUsesTerminalDefaultsAndVisibleFocus()
    {
        var scheme = MonochromeScheme.Create();
        Assert.Equal(Terminal.Gui.Drawing.Color.None, scheme.Normal.Foreground);
        Assert.Equal(Terminal.Gui.Drawing.Color.None, scheme.Focus.Background);
        Assert.Equal(Terminal.Gui.Drawing.TextStyle.Reverse, scheme.Focus.Style);
    }
}
