namespace Hourglass.Application;

using System.Collections;
using System.Diagnostics.CodeAnalysis;

/// <summary>Logical registrations accessed exclusively by HourglassRuntime's mutation queue.</summary>
internal sealed class TimerSessionManager : IEnumerable<KeyValuePair<string, SessionRegistration>>
{
    private readonly Dictionary<string, SessionRegistration> registrations = new(StringComparer.Ordinal);
    public int Count => this.registrations.Count;
    public Dictionary<string, SessionRegistration>.ValueCollection Values => this.registrations.Values;
    public SessionRegistration this[string id] { get => this.registrations[id]; set => this.registrations[id] = value; }
    public bool ContainsKey(string id) => this.registrations.ContainsKey(id);
    public bool TryGetValue(string id, [NotNullWhen(true)] out SessionRegistration? registration) => this.registrations.TryGetValue(id, out registration);
    public void Add(string id, SessionRegistration registration) => this.registrations.Add(id, registration);
    public bool Remove(string id) => this.registrations.Remove(id);
    public bool Remove(string id, [NotNullWhen(true)] out SessionRegistration? registration) => this.registrations.Remove(id, out registration);
    public void Clear() => this.registrations.Clear();
    public IEnumerator<KeyValuePair<string, SessionRegistration>> GetEnumerator() => this.registrations.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
}

internal sealed record SessionRegistration(TimerSession Session, Action<SessionTick> Publish, bool TickEnabled = true, SessionEffects? Effects = null, string Id = "", bool Authoritative = false)
{
    public List<SessionSubscription> Subscriptions { get; } = [];
    public long PersistenceRevision { get; set; } = -1;
    public long Sequence { get; set; }
    public long CompletionGeneration { get; set; }
    public Task LifecycleWork { get; set; } = Task.CompletedTask;
}
