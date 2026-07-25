using System.Globalization;
using System.Resources;

namespace Hourglass.Linux.Avalonia;

public static class ApplicationStrings
{
    private static readonly ResourceManager ResourceManager = new(
        "Hourglass.Linux.Avalonia.Properties.Resources",
        typeof(ApplicationStrings).Assembly);

    public static string AboutAutomationClose => GetString();
    public static string AboutAutomationCopyBuildInformation => GetString();
    public static string AboutAutomationDeveloperWebsite => GetString();
    public static string AboutAutomationIcon => GetString();
    public static string AboutAutomationOriginalProject => GetString();
    public static string AboutAutomationRepository => GetString();
    public static string AboutDeveloperWebsiteButton => GetString();
    public static string AboutOriginalProjectButton => GetString();
    public static string AboutRepositoryButton => GetString();
    public static string AboutArchitectureLineFormat => GetString();
    public static string AboutBuildConfigurationLineFormat => GetString();
    public static string AboutBuildInformationCopied => GetString();
    public static string AboutBuildInformationCopyFailed => GetString();
    public static string AboutBuildLineFormat => GetString();
    public static string AboutCommitLineFormat => GetString();
    public static string AboutDeveloperFormat => GetString();
    public static string AboutDialogTitle => GetString();
    public static string AboutInformationalVersionLineFormat => GetString();
    public static string AboutLicenseFormat => GetString();
    public static string AboutLinkOpenFailed => GetString();
    public static string AboutMenuHeader => GetString();
    public static string AboutPlatformLineFormat => GetString();
    public static string AboutPlatformValueFormat => GetString();
    public static string AboutRepositoryLineFormat => GetString();
    public static string AboutOperatingSystemLineFormat => GetString();
    public static string AboutRuntimeLineFormat => GetString();
    public static string AboutVersionLineFormat => GetString();
    public static string AdvancedOptionsMenuHeader => GetString();
    public static string ApplicationTitle => GetString();
    public static string ApplicationDescription => GetString();
    public static string ApplicationDeveloperName => GetString();
    public static string ApplicationLicenseName => GetString();
    public static string ApplicationProductName => GetString();
    public static string AudioAlertLoudBeep => GetString();
    public static string AudioAlertNoSound => GetString();
    public static string AudioAlertNormalBeep => GetString();
    public static string AudioAlertQuietBeep => GetString();
    public static string AudioAlertSoundMenuHeader => GetString();
    public static string AutomationCancelExit => GetString();
    public static string AutomationCancelThemeDelete => GetString();
    public static string AutomationCancelThemeEdit => GetString();
    public static string AutomationDeleteTheme => GetString();
    public static string AutomationExitApplication => GetString();
    public static string AutomationSaveTheme => GetString();
    public static string CommandCancel => GetString();
    public static string CommandClose => GetString();
    public static string CommandDelete => GetString();
    public static string CommandExit => GetString();
    public static string CommandHide => GetString();
    public static string CommandPause => GetString();
    public static string CommandReset => GetString();
    public static string CommandRestart => GetString();
    public static string CommandResume => GetString();
    public static string CommandSave => GetString();
    public static string CommandShow => GetString();
    public static string CommandStart => GetString();
    public static string CommandLineSingleInstanceContactFailedFormat => GetString();
    public static string CommandLineSingleInstanceListenerFailedFormat => GetString();
    public static string CommandLineSingleInstanceLockFailedFormat => GetString();
    public static string ContextMenuAlwaysOnTop => GetString();
    public static string ContextMenuCloseWhenExpired => GetString();
    public static string ContextMenuDoNotKeepComputerAwake => GetString();
    public static string ContextMenuFullScreen => GetString();
    public static string ContextMenuHideToNotificationArea => GetString();
    public static string ContextMenuLockInterface => GetString();
    public static string ContextMenuLoopSound => GetString();
    public static string ContextMenuLoopTimer => GetString();
    public static string ContextMenuNotifications => GetString();
    public static string ContextMenuOpenSavedTimersOnStartup => GetString();
    public static string ContextMenuPopUpWhenExpired => GetString();
    public static string ContextMenuPreviewSelectedSound => GetString();
    public static string ContextMenuPromptOnExit => GetString();
    public static string ContextMenuRestoreActiveSessionOnStartup => GetString();
    public static string ContextMenuReverseProgressBar => GetString();
    public static string ContextMenuShowInNotificationArea => GetString();
    public static string ContextMenuShowProgressInTaskbar => GetString();
    public static string ContextMenuShowTimeElapsed => GetString();
    public static string ContextMenuShutDownWhenExpired => GetString();
    public static string ContextMenuStopPreview => GetString();
    public static string CustomThemeBaseThemeLabel => GetString();
    public static string CustomThemeDefaultName => GetString();
    public static string CustomThemeNameAutomation => GetString();
    public static string CustomThemeDeleteMessageFormat => GetString();
    public static string CustomThemeDeleteTitle => GetString();
    public static string CustomThemeEditorTitle => GetString();
    public static string CustomThemeValidationColors => GetString();
    public static string CustomThemeValidationNameRequired => GetString();
    public static string ExitConfirmationMessage => GetString();
    public static string ExitConfirmationTitle => GetString();
    public static string LabelAccent => GetString();
    public static string LabelAccentColor => GetString();
    public static string LabelBackground => GetString();
    public static string LabelBackgroundColor => GetString();
    public static string LabelBuild => GetString();
    public static string LabelCommandText => GetString();
    public static string LabelCommandTextColor => GetString();
    public static string LabelCommit => GetString();
    public static string LabelCompletionBorder => GetString();
    public static string LabelCompletionBorderColor => GetString();
    public static string LabelLockedBorder => GetString();
    public static string LabelLockedBorderColor => GetString();
    public static string LabelName => GetString();
    public static string LabelPlatform => GetString();
    public static string LabelPrimaryText => GetString();
    public static string LabelPrimaryTextColor => GetString();
    public static string LabelProgressFill => GetString();
    public static string LabelProgressFillColor => GetString();
    public static string LabelRuntime => GetString();
    public static string LabelSecondaryText => GetString();
    public static string LabelSecondaryTextColor => GetString();
    public static string LabelValidationFlash => GetString();
    public static string LabelValidationFlashColor => GetString();
    public static string LabelVersion => GetString();
    public static string LocalBuild => GetString();
    public static string UnknownMetadataValue => GetString();
    public static string MainMenuNewTimer => GetString();
    public static string RecentInputsClear => GetString();
    public static string RecentInputsMenuHeader => GetString();
    public static string SavedTimersClear => GetString();
    public static string SavedTimersMenuHeader => GetString();
    public static string SavedTimersOpen => GetString();
    public static string SavedTimersOpenAll => GetString();
    public static string SavedTimersRemove => GetString();
    public static string SavedTimersSaveCurrent => GetString();
    public static string SessionInhibitionReason => GetString();
    public static string StatusInvalidTimer => GetString();
    public static string StatusPaused => GetString();
    public static string StatusReady => GetString();
    public static string StatusRunning => GetString();
    public static string StatusTimerComplete => GetString();
    public static string ThemeBaseDark => GetString();
    public static string ThemeBaseLight => GetString();
    public static string ThemeBaseSystem => GetString();
    public static string ThemeCommandDelete => GetString();
    public static string ThemeCommandDuplicate => GetString();
    public static string ThemeCommandEdit => GetString();
    public static string ThemeCommandExport => GetString();
    public static string ThemeCommandImport => GetString();
    public static string ThemeCommandNew => GetString();
    public static string ThemeCommandUse => GetString();
    public static string ThemeExportTitle => GetString();
    public static string ThemeFilePickerTitle => GetString();
    public static string ThemeImportTitle => GetString();
    public static string ThemeMenuHeader => GetString();
    public static string TimerAutomationCancel => GetString();
    public static string TimerAutomationCompletionHelp => GetString();
    public static string TimerAutomationInput => GetString();
    public static string TimerAutomationPause => GetString();
    public static string TimerAutomationRemainingHelp => GetString();
    public static string TimerAutomationRemainingTime => GetString();
    public static string TimerAutomationRestart => GetString();
    public static string TimerAutomationResume => GetString();
    public static string TimerAutomationStart => GetString();
    public static string TimerAutomationStatus => GetString();
    public static string TimerAutomationStop => GetString();
    public static string TimerAutomationTitle => GetString();
    public static string TimerAutomationTitleHelp => GetString();
    public static string TimerInputDefault => GetString();
    public static string TimerInputDefaultHelpText => GetString();
    public static string TimerTitlePlaceholder => GetString();
    public static string WindowTitleApplicationName => GetString();
    public static string WindowTitleMenuHeader => GetString();
    public static string WindowTitleTimeElapsed => GetString();
    public static string WindowTitleTimeElapsedPlusTimerTitle => GetString();
    public static string WindowTitleTimeLeft => GetString();
    public static string WindowTitleTimeLeftPlusTimerTitle => GetString();
    public static string WindowTitleTimerTitle => GetString();
    public static string WindowTitleTimerTitlePlusTimeElapsed => GetString();
    public static string WindowTitleTimerTitlePlusTimeLeft => GetString();

    public static string FormatAboutArchitectureLine(string architecture) =>
        Format(AboutArchitectureLineFormat, architecture);

    public static string FormatAboutBuildConfigurationLine(string buildConfiguration) =>
        Format(AboutBuildConfigurationLineFormat, buildConfiguration);

    public static string FormatAboutBuildLine(string build) => Format(AboutBuildLineFormat, build);

    public static string FormatAboutCommitLine(string commit) => Format(AboutCommitLineFormat, commit);

    public static string FormatAboutDeveloper(string developerName) => Format(AboutDeveloperFormat, developerName);

    public static string FormatAboutInformationalVersionLine(string informationalVersion) =>
        Format(AboutInformationalVersionLineFormat, informationalVersion);

    public static string FormatAboutLicense(string licenseName) => Format(AboutLicenseFormat, licenseName);

    public static string FormatAboutOperatingSystemLine(string operatingSystem) =>
        Format(AboutOperatingSystemLineFormat, operatingSystem);

    public static string FormatAboutPlatformLine(string operatingSystem, string architecture) =>
        Format(AboutPlatformLineFormat, operatingSystem, architecture);

    public static string FormatAboutPlatformValue(string operatingSystem, string architecture) =>
        Format(AboutPlatformValueFormat, operatingSystem, architecture);

    public static string FormatAboutRepositoryLine(Uri repositoryUri) => Format(AboutRepositoryLineFormat, repositoryUri);

    public static string FormatAboutRuntimeLine(string runtime) => Format(AboutRuntimeLineFormat, runtime);

    public static string FormatAboutVersionLine(string version) => Format(AboutVersionLineFormat, version);

    public static string FormatCustomThemeDeleteMessage(string themeName) =>
        Format(CustomThemeDeleteMessageFormat, themeName);

    public static string FormatSingleInstanceContactFailed(string message) =>
        Format(CommandLineSingleInstanceContactFailedFormat, message);

    public static string FormatSingleInstanceListenerFailed(string message) =>
        Format(CommandLineSingleInstanceListenerFailedFormat, message);

    public static string FormatSingleInstanceLockFailed(string message) =>
        Format(CommandLineSingleInstanceLockFailedFormat, message);

    private static string Format(string format, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, format, args);

    private static string GetString([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        string key = name ?? throw new ArgumentNullException(nameof(name));
        return ResourceManager.GetString(key, CultureInfo.CurrentUICulture)
            ?? throw new MissingManifestResourceException($"Missing application string resource '{key}'.");
    }
}
