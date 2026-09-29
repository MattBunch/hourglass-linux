namespace Hourglass.Application;

using System.Collections.Immutable;
using Hourglass.Settings;

public abstract record SavedTimerSelection
{
    public sealed record ByNameOrId(string Value) : SavedTimerSelection;
    public sealed record All : SavedTimerSelection;
}

public static class SavedTimerSelector
{
    public static ApplicationResult<ImmutableArray<SavedTimerDefinition>> Resolve(
        ImmutableArray<SavedTimerDefinition> timers, SavedTimerSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (selection is SavedTimerSelection.All) { return new ApplicationResult<ImmutableArray<SavedTimerDefinition>>.Success(timers); }
        if (selection is not SavedTimerSelection.ByNameOrId named || string.IsNullOrWhiteSpace(named.Value))
        {
            return new ApplicationResult<ImmutableArray<SavedTimerDefinition>>.Failure(new(ApplicationErrorCode.Validation, "A saved timer selector is required."));
        }
        string value = named.Value.Trim();
        SavedTimerDefinition? idMatch = timers.FirstOrDefault(timer => timer.Id == value);
        if (idMatch != null) { return new ApplicationResult<ImmutableArray<SavedTimerDefinition>>.Success([idMatch]); }
        SavedTimerDefinition[] matches = timers.Where(timer => timer.DisplayName == value || timer.Header == value).ToArray();
        return matches.Length switch
        {
            0 => new ApplicationResult<ImmutableArray<SavedTimerDefinition>>.Failure(new(ApplicationErrorCode.NotFound, "Unknown saved timer.")),
            1 => new ApplicationResult<ImmutableArray<SavedTimerDefinition>>.Success([matches[0]]),
            _ => new ApplicationResult<ImmutableArray<SavedTimerDefinition>>.Failure(new(ApplicationErrorCode.Conflict,
                $"Ambiguous saved timer. Matching IDs: {string.Join(", ", matches.Select(timer => timer.Id))}"))
        };
    }
}
