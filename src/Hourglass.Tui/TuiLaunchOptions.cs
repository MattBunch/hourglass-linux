namespace Hourglass.Tui;

public sealed record TuiLaunchOptions(bool Accessible = false, bool NoColor = false)
{
    public bool Monochrome => this.Accessible || this.NoColor;
    public TimeSpan RefreshInterval => TimeSpan.FromMilliseconds(this.Accessible ? 1000 : 200);

    public static bool TryParse(IReadOnlyList<string> arguments, string? noColorEnvironment, out TuiLaunchOptions options)
    {
        options = new(NoColor: !string.IsNullOrEmpty(noColorEnvironment));
        foreach (string argument in arguments)
        {
            if (argument == "--accessible") { options = options with { Accessible = true }; }
            else if (argument == "--no-color") { options = options with { NoColor = true }; }
            else { return false; }
        }
        return true;
    }
}
