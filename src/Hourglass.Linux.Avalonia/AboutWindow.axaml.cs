using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Hourglass.Linux.Services;
using Hourglass.Platform;

namespace Hourglass.Linux.Avalonia;

public sealed partial class AboutWindow : Window
{
    private readonly ApplicationInfo applicationInfo;
    private readonly IExternalUriLauncher externalUriLauncher;

    public AboutWindow()
        : this(new ApplicationInfoProvider().GetApplicationInfo(), new LinuxExternalUriLauncher())
    {
    }

    internal AboutWindow(ApplicationInfo applicationInfo, IExternalUriLauncher externalUriLauncher)
    {
        this.applicationInfo = applicationInfo ?? throw new ArgumentNullException(nameof(applicationInfo));
        this.externalUriLauncher = externalUriLauncher ?? throw new ArgumentNullException(nameof(externalUriLauncher));

        InitializeComponent();
        this.AddHandler(KeyDownEvent, this.WindowKeyDown, RoutingStrategies.Tunnel);
        this.PopulateApplicationInfo();
    }

    private void PopulateApplicationInfo()
    {
        this.ProductNameText.Text = this.applicationInfo.ProductName;
        this.DescriptionText.Text = this.applicationInfo.Description;
        this.VersionText.Text = this.applicationInfo.Version;
        this.BuildText.Text = this.applicationInfo.BuildConfiguration;
        this.CommitText.Text = this.applicationInfo.DisplaySourceRevision;
        this.RuntimeText.Text = this.applicationInfo.RuntimeDescription;
        this.PlatformText.Text = $"{this.applicationInfo.OperatingSystemDescription} {this.applicationInfo.ProcessArchitecture}";
        this.DeveloperText.Text = $"Developed by {this.applicationInfo.DeveloperName}";
        this.LicenseText.Text = $"{this.applicationInfo.LicenseName}. Hourglass Linux is distributed under the MIT license.";
    }

    private async void RepositoryButtonClick(object? sender, RoutedEventArgs e)
    {
        await this.OpenUriAsync(this.applicationInfo.RepositoryUri);
    }

    private async void DeveloperWebsiteButtonClick(object? sender, RoutedEventArgs e)
    {
        await this.OpenUriAsync(this.applicationInfo.DeveloperWebsiteUri);
    }

    private async void OriginalProjectButtonClick(object? sender, RoutedEventArgs e)
    {
        await this.OpenUriAsync(this.applicationInfo.OriginalProjectUri);
    }

    private async Task OpenUriAsync(Uri uri)
    {
        this.LinkStatusText.Text = string.Empty;

        bool opened;
        try
        {
            opened = await this.externalUriLauncher.OpenAsync(uri).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            opened = false;
        }

        if (!opened)
        {
            this.LinkStatusText.Text = "The link could not be opened.";
        }
    }

    private async void CopyBuildInformationButtonClick(object? sender, RoutedEventArgs e)
    {
        this.CopyStatusText.Text = string.Empty;
        IClipboard? clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard == null)
        {
            this.CopyStatusText.Text = "Build information could not be copied.";
            return;
        }

        await clipboard.SetTextAsync(this.CreateBuildInformationText()).ConfigureAwait(true);
        this.CopyStatusText.Text = "Build information copied.";
    }

    private string CreateBuildInformationText()
    {
        var builder = new StringBuilder();
        builder.AppendLine(this.applicationInfo.ProductName);
        builder.AppendLine($"Version: {this.applicationInfo.Version}");
        builder.AppendLine($"Informational version: {this.applicationInfo.InformationalVersion}");
        builder.AppendLine($"Build configuration: {this.applicationInfo.BuildConfiguration}");
        builder.AppendLine($"Commit: {this.applicationInfo.DisplaySourceRevision}");
        builder.AppendLine($"Runtime: {this.applicationInfo.RuntimeDescription}");
        builder.AppendLine($"Operating system: {this.applicationInfo.OperatingSystemDescription}");
        builder.AppendLine($"Architecture: {this.applicationInfo.ProcessArchitecture}");
        builder.AppendLine($"Repository: {this.applicationInfo.RepositoryUri}");
        return builder.ToString();
    }

    private void CloseButtonClick(object? sender, RoutedEventArgs e)
    {
        this.Close();
    }

    private void WindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        e.Handled = true;
        this.Close();
    }
}
