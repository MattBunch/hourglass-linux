namespace Hourglass.Linux.Avalonia;

using System.Collections.Generic;
using System.Globalization;
using global::Avalonia.Data.Converters;

public sealed class ProgressWidthConverter : IMultiValueConverter
{
    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2 ||
            values[0] is not double progressPercent ||
            values[1] is not double availableWidth)
        {
            return 0d;
        }

        if (!double.IsFinite(progressPercent) || !double.IsFinite(availableWidth))
        {
            return 0d;
        }

        var clampedProgress = Math.Clamp(progressPercent, 0d, 100d);
        var clampedWidth = Math.Max(0d, availableWidth);

        return clampedWidth * clampedProgress / 100d;
    }
}
