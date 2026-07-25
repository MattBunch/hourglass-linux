using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Hourglass.Linux.Avalonia;

public sealed partial class CustomThemeDeleteWindow : Window
{
    public CustomThemeDeleteWindow()
        : this(ApplicationStrings.CustomThemeDefaultName)
    {
    }

    public CustomThemeDeleteWindow(string themeName)
    {
        InitializeComponent();
        this.MessageText.Text = ApplicationStrings.FormatCustomThemeDeleteMessage(themeName);
    }

    private void CancelButtonClick(object? sender, RoutedEventArgs e)
    {
        this.Close(false);
    }

    private void DeleteButtonClick(object? sender, RoutedEventArgs e)
    {
        this.Close(true);
    }
}
