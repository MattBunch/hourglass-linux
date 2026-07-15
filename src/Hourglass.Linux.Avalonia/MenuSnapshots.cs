namespace Hourglass.Linux.Avalonia;

public sealed record RecentInputMenuItem(string TimerInput);

public sealed record SavedTimerMenuItem(string Id, string Header);

public sealed record CustomThemeMenuItem(string Id, string Name, bool IsSelected);
