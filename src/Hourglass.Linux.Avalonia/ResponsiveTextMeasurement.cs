using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace Hourglass.Linux.Avalonia;

internal static class ResponsiveTextMeasurement
{
    internal static double MeasureWidth(
        string text,
        Typeface typeface,
        double fontSize,
        FlowDirection flowDirection,
        double letterSpacing)
    {
        var textLayout = new TextLayout(
            text,
            typeface,
            fontSize,
            foreground: null,
            textAlignment: TextAlignment.Left,
            textWrapping: TextWrapping.NoWrap,
            textTrimming: TextTrimming.None,
            flowDirection: flowDirection,
            letterSpacing: letterSpacing);

        return textLayout.WidthIncludingTrailingWhitespace;
    }
}
