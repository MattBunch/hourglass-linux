namespace Hourglass.Properties;

using System.Globalization;
using System.Resources;

public static class Resources
{
    private static readonly ResourceManager Manager = new(
        "Hourglass.Properties.Resources",
        typeof(Resources).Assembly);

    public static ResourceManager ResourceManager => Manager;

    public static CultureInfo? Culture { get; set; }

    public static string TimerStartDefault =>
        Manager.GetString(nameof(TimerStartDefault), Culture) ?? "5 minutes";

    public static string TimerStartZero =>
        Manager.GetString(nameof(TimerStartZero), Culture) ?? "0 seconds";
}
