namespace Hourglass.Application;

using Hourglass.Settings;

internal static class CustomThemeChanges
{
    public static CustomThemesDocument Merge(
        CustomThemesDocument previous,
        CustomThemesDocument requested,
        CustomThemesDocument latest)
    {
        CustomThemeDefinition[] previousThemes = previous.Themes;
        CustomThemeDefinition[] requestedThemes = requested.Themes;
        CustomThemeDefinition[] latestThemes = latest.Themes;

        var previousIds = previousThemes
            .Select(theme => theme.Id)
            .ToHashSet(StringComparer.Ordinal);
        var previousById = previousThemes
            .ToDictionary(theme => theme.Id, StringComparer.Ordinal);
        var requestedById = requestedThemes
            .ToDictionary(theme => theme.Id, StringComparer.Ordinal);
        var latestIds = latestThemes
            .Select(theme => theme.Id)
            .ToHashSet(StringComparer.Ordinal);

        IEnumerable<CustomThemeDefinition> mergedLatest = latestThemes
            .Select(theme => SelectMergedCustomTheme(theme, previousById, requestedById))
            .OfType<CustomThemeDefinition>();

        IEnumerable<CustomThemeDefinition> requestedAdditions = requestedThemes
            .Where(theme => !latestIds.Contains(theme.Id) && !previousIds.Contains(theme.Id));

        return new CustomThemesDocument(themes: requestedAdditions.Concat(mergedLatest).ToArray());
    }

    private static CustomThemeDefinition? SelectMergedCustomTheme(
        CustomThemeDefinition latestTheme,
        Dictionary<string, CustomThemeDefinition> previousById,
        Dictionary<string, CustomThemeDefinition> requestedById)
    {
        if (requestedById.TryGetValue(latestTheme.Id, out CustomThemeDefinition? requestedTheme))
        {
            return previousById.TryGetValue(latestTheme.Id, out CustomThemeDefinition? previousTheme)
                && requestedTheme == previousTheme
                    ? latestTheme
                    : requestedTheme;
        }

        return previousById.ContainsKey(latestTheme.Id)
                    ? null
                    : latestTheme;
    }

}
