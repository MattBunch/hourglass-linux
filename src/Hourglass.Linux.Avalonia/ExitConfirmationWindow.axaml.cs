using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Hourglass.Linux.Avalonia;

public sealed partial class ExitConfirmationWindow : Window
{
    public ExitConfirmationWindow()
    {
        InitializeComponent();
    }

    private void CancelButtonClick(object? sender, RoutedEventArgs e)
    {
        this.Close(false);
    }

    private void ExitButtonClick(object? sender, RoutedEventArgs e)
    {
        this.Close(true);
    }
}
