using Hourglass.Settings;

namespace Hourglass.Linux.Avalonia;

internal readonly record struct WindowWorkArea(double X, double Y, double Width, double Height, double Scaling = 1)
{
    public double Right => this.X + this.Width;

    public double Bottom => this.Y + this.Height;

    public double NormalizedScaling => NormalizeScaling(this.Scaling);

    public double DeviceIndependentWidth => this.Width / this.NormalizedScaling;

    public double DeviceIndependentHeight => this.Height / this.NormalizedScaling;

    public bool IsUsable =>
        double.IsFinite(this.X)
        && double.IsFinite(this.Y)
        && double.IsFinite(this.Width)
        && double.IsFinite(this.Height)
        && this.DeviceIndependentWidth >= WindowGeometrySnapshot.MinimumWidth
        && this.DeviceIndependentHeight >= WindowGeometrySnapshot.MinimumHeight;

    public double ToPhysicalWidth(double width)
    {
        return width * this.NormalizedScaling;
    }

    public double ToPhysicalHeight(double height)
    {
        return height * this.NormalizedScaling;
    }

    public static double NormalizeScaling(double scaling)
    {
        return double.IsFinite(scaling) && scaling > 0
            ? scaling
            : 1.0;
    }
}

internal static class WindowGeometryValidator
{
    public static WindowGeometrySnapshot Validate(
        WindowGeometrySnapshot geometry,
        IReadOnlyList<WindowWorkArea> workAreas)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(workAreas);

        WindowWorkArea[] usableAreas = workAreas.Where(area => area.IsUsable).ToArray();
        if (usableAreas.Length == 0)
        {
            return geometry;
        }

        WindowGeometrySnapshot sizedGeometry = ClampSize(geometry, usableAreas);
        if (IntersectsAnyWorkArea(sizedGeometry, usableAreas))
        {
            return sizedGeometry;
        }

        WindowWorkArea targetArea = FindNearestWorkArea(sizedGeometry, usableAreas);
        return MoveIntoWorkArea(sizedGeometry, targetArea);
    }

    public static WindowGeometrySnapshot Cascade(
        WindowGeometrySnapshot geometry,
        WindowGeometrySnapshot? previousGeometry,
        IReadOnlyList<WindowWorkArea> workAreas,
        double offset = 32)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(workAreas);

        if (previousGeometry == null)
        {
            return Validate(geometry, workAreas);
        }

        WindowWorkArea[] usableAreas = workAreas.Where(area => area.IsUsable).ToArray();
        double physicalOffset = offset * FindCascadeScale(previousGeometry, usableAreas);
        var cascaded = new WindowGeometrySnapshot(
            previousGeometry.X + physicalOffset,
            previousGeometry.Y + physicalOffset,
            geometry.Width,
            geometry.Height,
            geometry.State);
        return Validate(cascaded, workAreas);
    }

    private static WindowGeometrySnapshot ClampSize(
        WindowGeometrySnapshot geometry,
        IReadOnlyList<WindowWorkArea> workAreas)
    {
        double maxWidth = Math.Max(
            WindowGeometrySnapshot.MinimumWidth,
            workAreas.Max(area => area.DeviceIndependentWidth));
        double maxHeight = Math.Max(
            WindowGeometrySnapshot.MinimumHeight,
            workAreas.Max(area => area.DeviceIndependentHeight));
        return new WindowGeometrySnapshot(
            geometry.X,
            geometry.Y,
            Math.Clamp(geometry.Width, WindowGeometrySnapshot.MinimumWidth, maxWidth),
            Math.Clamp(geometry.Height, WindowGeometrySnapshot.MinimumHeight, maxHeight),
            geometry.State);
    }

    private static bool IntersectsAnyWorkArea(
        WindowGeometrySnapshot geometry,
        IEnumerable<WindowWorkArea> workAreas)
    {
        return workAreas.Any(area =>
        {
            double physicalWidth = area.ToPhysicalWidth(geometry.Width);
            double physicalHeight = area.ToPhysicalHeight(geometry.Height);
            return geometry.X < area.Right
                && geometry.X + physicalWidth > area.X
                && geometry.Y < area.Bottom
                && geometry.Y + physicalHeight > area.Y;
        });
    }

    private static WindowWorkArea FindNearestWorkArea(
        WindowGeometrySnapshot geometry,
        IReadOnlyList<WindowWorkArea> workAreas)
    {
        return workAreas
            .OrderBy(area =>
            {
                double centerX = geometry.X + area.ToPhysicalWidth(geometry.Width) / 2;
                double centerY = geometry.Y + area.ToPhysicalHeight(geometry.Height) / 2;
                double areaCenterX = area.X + area.Width / 2;
                double areaCenterY = area.Y + area.Height / 2;
                double xDistance = centerX - areaCenterX;
                double yDistance = centerY - areaCenterY;
                return xDistance * xDistance + yDistance * yDistance;
            })
            .First();
    }

    private static WindowGeometrySnapshot MoveIntoWorkArea(
        WindowGeometrySnapshot geometry,
        WindowWorkArea workArea)
    {
        WindowGeometrySnapshot fittedGeometry = ClampSizeToWorkArea(geometry, workArea);
        double physicalWidth = workArea.ToPhysicalWidth(fittedGeometry.Width);
        double physicalHeight = workArea.ToPhysicalHeight(fittedGeometry.Height);
        double x = Math.Clamp(fittedGeometry.X, workArea.X, workArea.Right - physicalWidth);
        double y = Math.Clamp(fittedGeometry.Y, workArea.Y, workArea.Bottom - physicalHeight);
        return new WindowGeometrySnapshot(
            x,
            y,
            fittedGeometry.Width,
            fittedGeometry.Height,
            fittedGeometry.State);
    }

    private static WindowGeometrySnapshot ClampSizeToWorkArea(
        WindowGeometrySnapshot geometry,
        WindowWorkArea workArea)
    {
        return new WindowGeometrySnapshot(
            geometry.X,
            geometry.Y,
            Math.Clamp(geometry.Width, WindowGeometrySnapshot.MinimumWidth, workArea.DeviceIndependentWidth),
            Math.Clamp(geometry.Height, WindowGeometrySnapshot.MinimumHeight, workArea.DeviceIndependentHeight),
            geometry.State);
    }

    private static double FindCascadeScale(
        WindowGeometrySnapshot previousGeometry,
        IReadOnlyList<WindowWorkArea> workAreas)
    {
        if (workAreas.Count == 0)
        {
            return 1.0;
        }

        return FindNearestWorkArea(previousGeometry, workAreas).NormalizedScaling;
    }
}
