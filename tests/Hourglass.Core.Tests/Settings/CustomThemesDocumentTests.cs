namespace Hourglass.Core.Tests.Settings;

using System.Text.Json;
using Hourglass.Settings;
using Xunit;

public sealed class CustomThemesDocumentTests
{
    [Fact]
    public void CustomThemeDocumentFiltersInvalidUnsupportedAndDuplicateThemes()
    {
        var valid = new CustomThemeDefinition("theme-1", "Tea");
        var duplicate = new CustomThemeDefinition("theme-1", "Coffee");
        var invalid = new CustomThemeDefinition("theme-2", "Invalid", colors: new CustomThemeColors(background: "nope"));
        var unsupported = new CustomThemesDocument(version: CustomThemesDocument.CurrentVersion + 1, themes: [valid]);

        var document = new CustomThemesDocument(themes: [valid, duplicate, invalid]);

        CustomThemeDefinition theme = Assert.Single(document.Themes);
        Assert.Equal("theme-1", theme.Id);
        Assert.Equal("Tea", theme.Name);
        Assert.Empty(unsupported.Themes);
    }

    [Fact]
    public void CustomThemeDocumentRoundTripsTheme()
    {
        var theme = new CustomThemeDefinition(
            "theme-1",
            "Evening",
            LinuxThemePreference.Dark,
            new CustomThemeColors(
                background: "#FF101010",
                primaryText: "#FFFFFFFF",
                secondaryText: "#CCFFFFFF",
                commandText: "#FFFFFFFF",
                accent: "#FF5E9EFF",
                progressFill: "#665E9EFF",
                validationFlash: "#66D13438",
                completionBorder: "#FF5E9EFF",
                lockedBorder: "#80FFFFFF"));
        var document = new CustomThemesDocument(themes: [theme]);

        string json = JsonSerializer.Serialize(document);
        CustomThemesDocument? roundTripped = JsonSerializer.Deserialize<CustomThemesDocument>(json);

        Assert.NotNull(roundTripped);
        Assert.Equal(theme, Assert.Single(roundTripped.Themes));
    }

    [Theory]
    [InlineData("#FFFFFF", true)]
    [InlineData("#FFFFFFFF", true)]
    [InlineData("#fff", false)]
    [InlineData("FFFFFF", false)]
    [InlineData("#GGFFFFFF", false)]
    public void ValidatesSupportedHexColorForms(string color, bool expected)
    {
        Assert.Equal(expected, CustomThemeColors.IsValidColor(color));
    }

    [Fact]
    public void LinuxAppSettingsFallsBackFromCustomThemeWithoutIdentifier()
    {
        var settings = new LinuxAppSettings(themePreference: LinuxThemePreference.Custom);

        Assert.Equal(LinuxThemePreference.System, settings.ThemePreference);
        Assert.Null(settings.CustomThemeId);
    }
}
