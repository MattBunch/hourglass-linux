namespace Hourglass.Linux.Services;

using System.Text.Json;
using System.Globalization;
using Hourglass.Parsing;
using Hourglass.Serialization;
using System.Text.Json.Serialization;
using Hourglass.Timing;

internal static class RuntimeControlJson
{
    internal static readonly JsonSerializerOptions Options = CreateOptions();
    private static JsonSerializerOptions CreateOptions()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new CountdownConverter());
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
    internal static JsonElement Value<T>(T value) => JsonSerializer.SerializeToElement(value, Options);
    internal static T Read<T>(JsonElement value) => value.Deserialize<T>(Options) ?? throw new JsonException("Missing payload.");

    private sealed record CountdownValue(TimerState State, DateTime? StartTime, DateTime? EndTime,
        TimeSpan? TimeElapsed, TimeSpan? TimeLeft, TimeSpan? TimeExpired, TimeSpan? TotalTime,
        string? TimerStart, bool CanRestart, TimeSpan? RestartDuration, TimeSpan RunStartedAt, TimeSpan ElapsedBeforeRun);
    private sealed class CountdownConverter : JsonConverter<CountdownState>
    {
        public override CountdownState Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            CountdownValue value = JsonSerializer.Deserialize<CountdownValue>(ref reader, options) ?? throw new JsonException("Missing countdown.");
            TimerStart? start = value.TimerStart == null ? null : TimerStart.FromTimerStartInfo(new TimerStartInfo
            {
                TimerStartToken = TimerStartToken.FromString(value.TimerStart, CultureInfo.InvariantCulture) ?? throw new JsonException("Invalid timer start.")
            });
            return new(value.State, value.StartTime, value.EndTime, value.TimeElapsed, value.TimeLeft, value.TimeExpired,
                value.TotalTime, start, value.CanRestart, value.RestartDuration, value.RunStartedAt, value.ElapsedBeforeRun);
        }
        public override void Write(Utf8JsonWriter writer, CountdownState value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, new CountdownValue(value.State, value.StartTime, value.EndTime, value.TimeElapsed,
                value.TimeLeft, value.TimeExpired, value.TotalTime, value.TimerStart?.ToTimerStartInfo().TimerStartToken?.ToString(CultureInfo.InvariantCulture), value.CanRestart,
                value.RestartDuration, value.RunStartedAt, value.ElapsedBeforeRun), options);
    }
}
