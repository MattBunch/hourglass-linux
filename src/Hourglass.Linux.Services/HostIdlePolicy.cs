namespace Hourglass.Linux.Services;

/// <summary>Pure idle grace calculation; elapsed time comes from the host boundary.</summary>
public sealed record HostIdlePolicy(TimeSpan? IdleSince = null)
{
    public static readonly TimeSpan Grace = TimeSpan.FromSeconds(3);
    public HostIdlePolicy Observe(TimeSpan now, bool required) => required ? new() : this.IdleSince == null ? new(now) : this;
    public bool CanStop(TimeSpan now) => this.IdleSince is TimeSpan since && now - since >= Grace;
}
