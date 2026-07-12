using Hourglass.Settings;

namespace Hourglass.Linux.Avalonia;

internal readonly record struct WindowWorkArea(double X, double Y, double Width, double Height)
{
    public double Right => this.X + this.Width;

    public double Bottom => this.Y + this.Height;

    public bool IsUsable =>
        double.IsFinite(this.X)
        && double.IsFinite(this.Y)
        && double.IsFinite(this.Width)
        && double.IsFinite(this.Height)
        && this.Width >= WindowGeometrySnapshot.MinimumWidth
        && this.Height >= WindowGeometrySnapshot.MinimumHeight;
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

        if (previousGeometry == null)
        {
            return Validate(geometry, workAreas);
        }

        var cascaded = new WindowGeometrySnapshot(
            previousGeometry.X + offset,
            previousGeometry.Y + offset,
            geometry.Width,
            geometry.Height,
            geometry.State);
        return Validate(cascaded, workAreas);
    }

    private static WindowGeometrySnapshot ClampSize(
        WindowGeometrySnapshot geometry,
        IReadOnlyList<WindowWorkArea> workAreas)
    {
        double maxWidth = Math.Max(WindowGeometrySnapshot.MinimumWidth, workAreas.Max(area => area.Width));
        double maxHeight = Math.Max(WindowGeometrySnapshot.MinimumHeight, workAreas.Max(area => area.Height));
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
            geometry.X < area.Right
            && geometry.X + geometry.Width > area.X
            && geometry.Y < area.Bottom
            && geometry.Y + geometry.Height > area.Y);
    }

    private static WindowWorkArea FindNearestWorkArea(
        WindowGeometrySnapshot geometry,
        IReadOnlyList<WindowWorkArea> workAreas)
    {
        double centerX = geometry.X + geometry.Width / 2;
        double centerY = geometry.Y + geometry.Height / 2;
        return workAreas
            .OrderBy(area =>
            {
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
        double x = Math.Clamp(geometry.X, workArea.X, workArea.Right - geometry.Width);
        double y = Math.Clamp(geometry.Y, workArea.Y, workArea.Bottom - geometry.Height);
        return new WindowGeometrySnapshot(x, y, geometry.Width, geometry.Height, geometry.State);
    }
}
