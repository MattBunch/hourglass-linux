namespace Hourglass.Linux.Avalonia;

internal static class ResponsiveTextSizing
{
    internal const double BaseWindowWidth = 350;
    internal const double BaseWindowHeight = 150;
    internal const double PrimaryBaseFontSize = 18;
    internal const double PrimaryMinimumFontSize = 8;
    internal const double PrimaryMaximumFontSize = 72;
    internal const double SecondaryBaseFontSize = 12;
    internal const double SecondaryMinimumFontSize = 8;
    internal const double SecondaryMaximumFontSize = 32;
    internal const double CommandMinimumFontSize = 9;
    internal const double CommandMaximumFontSize = 24;

    private const double MinimumScaleFactor = 0.5;
    private const double MaximumScaleFactor = 4;
    private const double SecondaryScaleReduction = 0.5;

    internal static ResponsiveInterfaceScale CalculateInterfaceScale(double availableWidth, double availableHeight)
    {
        double width = IsPositiveFinite(availableWidth) ? availableWidth : BaseWindowWidth;
        double height = IsPositiveFinite(availableHeight) ? availableHeight : BaseWindowHeight;
        double widthFactor = width / BaseWindowWidth;
        double heightFactor = height / BaseWindowHeight;
        double scaleFactor = Math.Clamp(Math.Min(widthFactor, heightFactor), MinimumScaleFactor, MaximumScaleFactor);
        double reducedScaleFactor = 1 + ((scaleFactor - 1) * SecondaryScaleReduction);

        return new ResponsiveInterfaceScale(
            ScaleFactor: scaleFactor,
            ReducedScaleFactor: reducedScaleFactor,
            PrimaryMaximumFontSize: Math.Clamp(
                PrimaryBaseFontSize * scaleFactor,
                PrimaryMinimumFontSize,
                PrimaryMaximumFontSize),
            SecondaryMaximumFontSize: Math.Clamp(
                SecondaryBaseFontSize * reducedScaleFactor,
                SecondaryMinimumFontSize,
                SecondaryMaximumFontSize),
            CommandFontSize: Math.Clamp(
                SecondaryBaseFontSize * reducedScaleFactor,
                CommandMinimumFontSize,
                CommandMaximumFontSize),
            InnerMargin: 10 * reducedScaleFactor,
            ContentHorizontalMargin: 20 * reducedScaleFactor,
            ContentSpacing: 4 * reducedScaleFactor,
            PrimaryRowMinimumHeight: 42 * scaleFactor,
            CommandHorizontalPadding: 7 * reducedScaleFactor);
    }

    internal static double CalculateFittedFontSize(
        double availableWidth,
        double measuredTextWidth,
        double minimumFontSize,
        double maximumFontSize)
    {
        double safeMinimum = IsPositiveFinite(minimumFontSize)
            ? minimumFontSize
            : PrimaryMinimumFontSize;
        double safeMaximum = IsPositiveFinite(maximumFontSize)
            ? Math.Max(maximumFontSize, safeMinimum)
            : safeMinimum;

        if (!IsPositiveFinite(availableWidth) || !IsPositiveFinite(measuredTextWidth))
        {
            return safeMinimum;
        }

        double fittedFontSize = safeMaximum * availableWidth / measuredTextWidth;
        return double.IsFinite(fittedFontSize)
            ? Math.Clamp(fittedFontSize, safeMinimum, safeMaximum)
            : safeMinimum;
    }

    internal static string GetMeasurementText(string? text, string? placeholderText = null)
    {
        if (!string.IsNullOrEmpty(text))
        {
            return text;
        }

        return !string.IsNullOrEmpty(placeholderText) ? placeholderText : "M";
    }

    private static bool IsPositiveFinite(double value)
    {
        return double.IsFinite(value) && value > 0;
    }
}

internal readonly record struct ResponsiveInterfaceScale(
    double ScaleFactor,
    double ReducedScaleFactor,
    double PrimaryMaximumFontSize,
    double SecondaryMaximumFontSize,
    double CommandFontSize,
    double InnerMargin,
    double ContentHorizontalMargin,
    double ContentSpacing,
    double PrimaryRowMinimumHeight,
    double CommandHorizontalPadding);
