namespace Hourglass.Linux.Avalonia.Tests;

using global::Avalonia.Controls;
using Hourglass.Linux.Avalonia;
using Xunit;

public sealed class ResponsiveTextSizingTests
{
    [Fact]
    public void ResponsiveTextBoxUsesNativeTextBoxTheme()
    {
        var textBox = new ResponsiveTextBox();

        Assert.Equal(typeof(TextBox), textBox.StyleKey);
    }

    [Fact]
    public void BaseWindowUsesReferenceScaleAndFontSizes()
    {
        ResponsiveInterfaceScale scale = ResponsiveTextSizing.CalculateInterfaceScale(350, 150);

        Assert.Equal(1, scale.ScaleFactor);
        Assert.Equal(18, scale.PrimaryMaximumFontSize);
        Assert.Equal(12, scale.SecondaryMaximumFontSize);
        Assert.Equal(12, scale.CommandFontSize);
    }

    [Fact]
    public void LargerProportionalWindowGrowsPrimaryTextMoreThanSecondaryText()
    {
        ResponsiveInterfaceScale scale = ResponsiveTextSizing.CalculateInterfaceScale(700, 300);

        Assert.Equal(2, scale.ScaleFactor);
        Assert.Equal(36, scale.PrimaryMaximumFontSize);
        Assert.Equal(18, scale.SecondaryMaximumFontSize);
        Assert.True(
            scale.PrimaryMaximumFontSize - ResponsiveTextSizing.PrimaryBaseFontSize
            > scale.SecondaryMaximumFontSize - ResponsiveTextSizing.SecondaryBaseFontSize);
    }

    [Fact]
    public void SmallerWindowProducesSmallerText()
    {
        ResponsiveInterfaceScale scale = ResponsiveTextSizing.CalculateInterfaceScale(250, 150);

        Assert.Equal(250d / 350d, scale.ScaleFactor, precision: 10);
        Assert.InRange(scale.PrimaryMaximumFontSize, 12.85, 12.86);
        Assert.InRange(scale.SecondaryMaximumFontSize, 10.28, 10.29);
    }

    [Theory]
    [InlineData(700, 150)]
    [InlineData(350, 300)]
    public void LimitingDimensionConstrainsScale(double width, double height)
    {
        ResponsiveInterfaceScale scale = ResponsiveTextSizing.CalculateInterfaceScale(width, height);

        Assert.Equal(1, scale.ScaleFactor);
        Assert.Equal(18, scale.PrimaryMaximumFontSize);
    }

    [Fact]
    public void NarrowTallWindowUsesWidthAsLimitingDimension()
    {
        ResponsiveInterfaceScale scale = ResponsiveTextSizing.CalculateInterfaceScale(250, 300);

        Assert.Equal(250d / 350d, scale.ScaleFactor, precision: 10);
    }

    [Fact]
    public void ExtremeWindowSizeClampsAllFontSizes()
    {
        ResponsiveInterfaceScale small = ResponsiveTextSizing.CalculateInterfaceScale(1, 1);
        ResponsiveInterfaceScale large = ResponsiveTextSizing.CalculateInterfaceScale(100_000, 100_000);

        Assert.InRange(
            small.PrimaryMaximumFontSize,
            ResponsiveTextSizing.PrimaryMinimumFontSize,
            ResponsiveTextSizing.PrimaryBaseFontSize);
        Assert.Equal(ResponsiveTextSizing.CommandMinimumFontSize, small.CommandFontSize);
        Assert.Equal(ResponsiveTextSizing.PrimaryMaximumFontSize, large.PrimaryMaximumFontSize);
        Assert.InRange(
            large.SecondaryMaximumFontSize,
            ResponsiveTextSizing.SecondaryBaseFontSize,
            ResponsiveTextSizing.SecondaryMaximumFontSize);
        Assert.Equal(ResponsiveTextSizing.CommandMaximumFontSize, large.CommandFontSize);
    }

    [Theory]
    [InlineData(0, 150)]
    [InlineData(-1, 150)]
    [InlineData(double.NaN, 150)]
    [InlineData(double.PositiveInfinity, 150)]
    [InlineData(350, 0)]
    [InlineData(350, double.NegativeInfinity)]
    public void InvalidWindowBoundsReturnSafeBaseScale(double width, double height)
    {
        ResponsiveInterfaceScale scale = ResponsiveTextSizing.CalculateInterfaceScale(width, height);

        Assert.Equal(1, scale.ScaleFactor);
        Assert.Equal(18, scale.PrimaryMaximumFontSize);
        Assert.True(double.IsFinite(scale.SecondaryMaximumFontSize));
    }

    [Fact]
    public void LongerTextProducesSmallerFittedFontInSameWidth()
    {
        double shortTextSize = ResponsiveTextSizing.CalculateFittedFontSize(200, 100, 8, 18);
        double longTextSize = ResponsiveTextSizing.CalculateFittedFontSize(200, 400, 8, 18);

        Assert.Equal(18, shortTextSize);
        Assert.Equal(9, longTextSize);
    }

    [Fact]
    public void EmptyTextUsesPlaceholderThenSafeFallback()
    {
        Assert.Equal("5 minutes", ResponsiveTextSizing.GetMeasurementText(string.Empty, "5 minutes"));
        Assert.Equal("M", ResponsiveTextSizing.GetMeasurementText(string.Empty));
        Assert.Equal("M", ResponsiveTextSizing.GetMeasurementText(null, null));
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(100, 0)]
    [InlineData(double.NaN, 100)]
    [InlineData(100, double.PositiveInfinity)]
    public void InvalidFitInputsReturnSafeMinimum(double availableWidth, double measuredTextWidth)
    {
        double fontSize = ResponsiveTextSizing.CalculateFittedFontSize(availableWidth, measuredTextWidth, 8, 18);

        Assert.Equal(8, fontSize);
        Assert.True(double.IsFinite(fontSize));
    }

    [Fact]
    public void ChangedTextMeasurementRecalculatesFittedSize()
    {
        double original = ResponsiveTextSizing.CalculateFittedFontSize(240, 120, 8, 18);
        double changed = ResponsiveTextSizing.CalculateFittedFontSize(240, 480, 8, 18);

        Assert.Equal(18, original);
        Assert.Equal(9, changed);
    }

    [Fact]
    public void ChangedAvailableWidthRecalculatesFittedSize()
    {
        double wide = ResponsiveTextSizing.CalculateFittedFontSize(400, 400, 8, 18);
        double narrow = ResponsiveTextSizing.CalculateFittedFontSize(200, 400, 8, 18);

        Assert.Equal(18, wide);
        Assert.Equal(9, narrow);
    }

    [Fact]
    public void FittedSizeRemainsWithinConfiguredLimits()
    {
        Assert.Equal(8, ResponsiveTextSizing.CalculateFittedFontSize(1, 1_000, 8, 18));
        Assert.Equal(18, ResponsiveTextSizing.CalculateFittedFontSize(1_000, 1, 8, 18));
    }
}
