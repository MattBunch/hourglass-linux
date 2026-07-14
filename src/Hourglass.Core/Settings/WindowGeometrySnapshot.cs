#nullable enable

namespace Hourglass.Settings;

using System.Text.Json.Serialization;

public enum WindowGeometryState
{
    Normal,
    Maximized
}

public sealed record WindowGeometrySnapshot
{
    public const double MinimumWidth = 250;
    public const double MinimumHeight = 150;

    [JsonConstructor]
    public WindowGeometrySnapshot(
        double x,
        double y,
        double width,
        double height,
        WindowGeometryState state = WindowGeometryState.Normal)
    {
        this.X = NormalizeCoordinate(x);
        this.Y = NormalizeCoordinate(y);
        this.Width = NormalizeDimension(width, MinimumWidth);
        this.Height = NormalizeDimension(height, MinimumHeight);
        this.State = state;
    }

    public double X { get; init; }

    public double Y { get; init; }

    public double Width { get; init; }

    public double Height { get; init; }

    public WindowGeometryState State { get; init; }

    private static double NormalizeCoordinate(double value)
    {
        return double.IsFinite(value) ? value : 0;
    }

    private static double NormalizeDimension(double value, double minimum)
    {
        return double.IsFinite(value) && value > minimum ? value : minimum;
    }
}
