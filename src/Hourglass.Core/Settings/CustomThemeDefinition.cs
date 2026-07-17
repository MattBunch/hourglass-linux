#nullable enable

namespace Hourglass.Settings;

using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

public sealed record CustomThemeColors
{
    public const string DefaultBackground = "#FFFFFFFF";
    public const string DefaultPrimaryText = "#FF1F1F1F";
    public const string DefaultSecondaryText = "#B31F1F1F";
    public const string DefaultCommandText = "#FF1F1F1F";
    public const string DefaultAccent = "#FF3665B3";
    public const string DefaultProgressFill = "#733665B3";
    public const string DefaultValidationFlash = "#29D13438";
    public const string DefaultCompletionBorder = "#FF3665B3";
    public const string DefaultLockedBorder = "#661F1F1F";

    [JsonConstructor]
    public CustomThemeColors(
        string background = DefaultBackground,
        string primaryText = DefaultPrimaryText,
        string secondaryText = DefaultSecondaryText,
        string commandText = DefaultCommandText,
        string accent = DefaultAccent,
        string progressFill = DefaultProgressFill,
        string validationFlash = DefaultValidationFlash,
        string completionBorder = DefaultCompletionBorder,
        string lockedBorder = DefaultLockedBorder)
    {
        this.Background = NormalizeColor(background, DefaultBackground);
        this.PrimaryText = NormalizeColor(primaryText, DefaultPrimaryText);
        this.SecondaryText = NormalizeColor(secondaryText, DefaultSecondaryText);
        this.CommandText = NormalizeColor(commandText, DefaultCommandText);
        this.Accent = NormalizeColor(accent, DefaultAccent);
        this.ProgressFill = NormalizeColor(progressFill, DefaultProgressFill);
        this.ValidationFlash = NormalizeColor(validationFlash, DefaultValidationFlash);
        this.CompletionBorder = NormalizeColor(completionBorder, DefaultCompletionBorder);
        this.LockedBorder = NormalizeColor(lockedBorder, DefaultLockedBorder);
    }

    public string Background { get; }

    public string PrimaryText { get; }

    public string SecondaryText { get; }

    public string CommandText { get; }

    public string Accent { get; }

    public string ProgressFill { get; }

    public string ValidationFlash { get; }

    public string CompletionBorder { get; }

    public string LockedBorder { get; }

    public static CustomThemeColors Default { get; } = new();

    public bool IsValid =>
        IsValidColor(this.Background)
        && IsValidColor(this.PrimaryText)
        && IsValidColor(this.SecondaryText)
        && IsValidColor(this.CommandText)
        && IsValidColor(this.Accent)
        && IsValidColor(this.ProgressFill)
        && IsValidColor(this.ValidationFlash)
        && IsValidColor(this.CompletionBorder)
        && IsValidColor(this.LockedBorder);

    public static bool IsValidColor(string? color)
    {
        if (color is not { Length: 7 or 9 } || color[0] != '#')
        {
            return false;
        }

        for (int index = 1; index < color.Length; index++)
        {
            if (!Uri.IsHexDigit(color[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static string NormalizeColor(string? color, string fallback)
    {
        return string.IsNullOrWhiteSpace(color)
            ? fallback
            : color.Trim().ToUpperInvariant();
    }
}

public sealed record CustomThemeDefinition
{
    public const int MaximumNameLength = 80;

    [JsonConstructor]
    public CustomThemeDefinition(
        string id,
        string name,
        LinuxThemePreference baseThemePreference = LinuxThemePreference.System,
        CustomThemeColors? colors = null)
    {
        this.Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id.Trim();
        this.Name = NormalizeName(name);
        this.BaseThemePreference = baseThemePreference is LinuxThemePreference.Light or LinuxThemePreference.Dark
            ? baseThemePreference
            : LinuxThemePreference.System;
        this.Colors = colors ?? CustomThemeColors.Default;
    }

    public string Id { get; }

    public string Name { get; }

    public LinuxThemePreference BaseThemePreference { get; }

    public CustomThemeColors Colors { get; }

    [JsonIgnore]
    public bool IsValid => !string.IsNullOrWhiteSpace(this.Id) && !string.IsNullOrWhiteSpace(this.Name) && this.Colors.IsValid;

    public CustomThemeDefinition Duplicate()
    {
        return new CustomThemeDefinition(Guid.NewGuid().ToString("N"), $"{this.Name} copy", this.BaseThemePreference, this.Colors);
    }

    private static string NormalizeName(string? name)
    {
        string trimmed = string.IsNullOrWhiteSpace(name) ? "Custom theme" : name.Trim();
        return trimmed.Length <= MaximumNameLength
            ? trimmed
            : trimmed[..MaximumNameLength];
    }
}

public sealed record CustomThemesDocument
{
    public const int CurrentVersion = 1;

    private readonly CustomThemeDefinition[] themes;

    public CustomThemesDocument()
        : this(CurrentVersion, null)
    {
    }

    [JsonConstructor]
    public CustomThemesDocument(int version = CurrentVersion, CustomThemeDefinition[]? themes = null)
    {
        this.Version = version;
        this.themes = Normalize(version, themes);
    }

    public int Version { get; }

    public CustomThemeDefinition[] Themes => this.themes.ToArray();

    public static CustomThemesDocument Empty { get; } = new();

    public CustomThemesDocument AddOrReplace(CustomThemeDefinition theme)
    {
        ArgumentNullException.ThrowIfNull(theme);

        if (!theme.IsValid)
        {
            return this;
        }

        CustomThemeDefinition[] updated = new[] { theme }
            .Concat(this.themes.Where(candidate => !StringComparer.Ordinal.Equals(candidate.Id, theme.Id)))
            .OrderBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new CustomThemesDocument(CurrentVersion, updated);
    }

    public CustomThemesDocument Remove(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return this;
        }

        return new CustomThemesDocument(
            CurrentVersion,
            this.themes.Where(theme => !StringComparer.Ordinal.Equals(theme.Id, id.Trim())).ToArray());
    }

    public CustomThemeDefinition? Find(string? id)
    {
        return string.IsNullOrWhiteSpace(id)
            ? null
            : this.themes.FirstOrDefault(theme => StringComparer.Ordinal.Equals(theme.Id, id.Trim()));
    }

    private static CustomThemeDefinition[] Normalize(int version, CustomThemeDefinition[]? themes)
    {
        if (version > CurrentVersion || themes == null)
        {
            return [];
        }

        return themes
            .Where(theme => theme is { IsValid: true })
            .GroupBy(theme => theme.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(theme => theme.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
