using Avalonia.Controls;
using Avalonia.Interactivity;
using Hourglass.Settings;

namespace Hourglass.Linux.Avalonia;

public sealed partial class CustomThemeEditorWindow : Window
{
    private readonly string themeId;

    public CustomThemeEditorWindow()
        : this(null)
    {
    }

    public CustomThemeEditorWindow(CustomThemeDefinition? theme)
    {
        InitializeComponent();

        this.themeId = theme?.Id ?? Guid.NewGuid().ToString("N");
        CustomThemeDefinition source = theme ?? new CustomThemeDefinition(
            this.themeId,
            "Custom theme",
            LinuxThemePreference.System,
            CustomThemeColors.Default);
        this.NameBox.Text = source.Name;
        this.BaseThemeBox.SelectedIndex = source.BaseThemePreference switch
        {
            LinuxThemePreference.Light => 1,
            LinuxThemePreference.Dark => 2,
            _ => 0
        };
        this.BackgroundBox.Text = source.Colors.Background;
        this.PrimaryTextBox.Text = source.Colors.PrimaryText;
        this.SecondaryTextBox.Text = source.Colors.SecondaryText;
        this.CommandTextBox.Text = source.Colors.CommandText;
        this.AccentBox.Text = source.Colors.Accent;
        this.ProgressFillBox.Text = source.Colors.ProgressFill;
        this.ValidationFlashBox.Text = source.Colors.ValidationFlash;
        this.CompletionBorderBox.Text = source.Colors.CompletionBorder;
        this.LockedBorderBox.Text = source.Colors.LockedBorder;
    }

    private void CancelButtonClick(object? sender, RoutedEventArgs e)
    {
        this.Close(null);
    }

    private void SaveButtonClick(object? sender, RoutedEventArgs e)
    {
        string background = this.BackgroundBox.Text ?? string.Empty;
        string primaryText = this.PrimaryTextBox.Text ?? string.Empty;
        string secondaryText = this.SecondaryTextBox.Text ?? string.Empty;
        string commandText = this.CommandTextBox.Text ?? string.Empty;
        string accent = this.AccentBox.Text ?? string.Empty;
        string progressFill = this.ProgressFillBox.Text ?? string.Empty;
        string validationFlash = this.ValidationFlashBox.Text ?? string.Empty;
        string completionBorder = this.CompletionBorderBox.Text ?? string.Empty;
        string lockedBorder = this.LockedBorderBox.Text ?? string.Empty;
        string[] colors =
        [
            background,
            primaryText,
            secondaryText,
            commandText,
            accent,
            progressFill,
            validationFlash,
            completionBorder,
            lockedBorder
        ];
        if (colors.Any(color => !CustomThemeColors.IsValidColor(color)))
        {
            this.ValidationText.Text = "Colors must use #RRGGBB or #AARRGGBB.";
            return;
        }

        string name = this.NameBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            this.ValidationText.Text = "Name is required.";
            return;
        }

        var theme = new CustomThemeDefinition(
            this.themeId,
            name,
            this.BaseThemeBox.SelectedIndex switch
            {
                1 => LinuxThemePreference.Light,
                2 => LinuxThemePreference.Dark,
                _ => LinuxThemePreference.System
            },
            new CustomThemeColors(
                background,
                primaryText,
                secondaryText,
                commandText,
                accent,
                progressFill,
                validationFlash,
                completionBorder,
                lockedBorder));
        this.Close(theme);
    }
}
