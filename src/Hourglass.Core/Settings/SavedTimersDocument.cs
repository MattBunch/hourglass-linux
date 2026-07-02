#nullable enable

namespace Hourglass.Settings;

using System.Text.Json.Serialization;

public sealed record SavedTimersDocument
{
    public const int CurrentVersion = 1;

    private readonly SavedTimerDefinition[] timers;

    public SavedTimersDocument()
        : this(CurrentVersion, null)
    {
    }

    [JsonConstructor]
    public SavedTimersDocument(int version = CurrentVersion, SavedTimerDefinition[]? timers = null)
    {
        this.Version = version;
        this.timers = Normalize(timers);
    }

    public int Version { get; }

    public SavedTimerDefinition[] Timers => this.timers.ToArray();

    public static SavedTimersDocument Empty { get; } = new();

    public SavedTimersDocument AddOrReplace(SavedTimerDefinition timer)
    {
        ArgumentNullException.ThrowIfNull(timer);

        if (!timer.IsValid)
        {
            return this;
        }

        SavedTimerDefinition[] updated = new[] { timer }
            .Concat(this.timers.Where(candidate => !StringComparer.Ordinal.Equals(candidate.Id, timer.Id)))
            .ToArray();

        return new SavedTimersDocument(CurrentVersion, updated);
    }

    public SavedTimersDocument Remove(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return this;
        }

        return new SavedTimersDocument(
            CurrentVersion,
            this.timers.Where(timer => !StringComparer.Ordinal.Equals(timer.Id, id.Trim())).ToArray());
    }

    private static SavedTimerDefinition[] Normalize(SavedTimerDefinition[]? timers)
    {
        if (timers == null)
        {
            return [];
        }

        return timers
            .Where(timer => timer is { IsValid: true })
            .GroupBy(timer => timer.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
    }
}
