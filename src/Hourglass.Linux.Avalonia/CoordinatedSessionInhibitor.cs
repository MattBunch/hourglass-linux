using Hourglass.Platform;

namespace Hourglass.Linux.Avalonia;

internal sealed class CoordinatedSessionInhibitor(ISessionInhibitor inner) : ISessionInhibitor, IAsyncDisposable
{
    private readonly object gate = new();
    private readonly ISessionInhibitor inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private IAsyncDisposable? sharedLease;
    private int referenceCount;

    public async ValueTask<IAsyncDisposable?> InhibitAsync(
        string reason,
        bool inhibitSuspend,
        bool inhibitIdle,
        CancellationToken cancellationToken = default)
    {
        bool shouldAcquire;
        lock (this.gate)
        {
            this.referenceCount++;
            shouldAcquire = this.referenceCount == 1;
        }

        if (!shouldAcquire)
        {
            return new Lease(this);
        }

        try
        {
            IAsyncDisposable? lease = await this.inner.InhibitAsync(
                reason,
                inhibitSuspend,
                inhibitIdle,
                cancellationToken).ConfigureAwait(false);
            lock (this.gate)
            {
                this.sharedLease = lease;
            }
        }
        catch
        {
            lock (this.gate)
            {
                this.referenceCount--;
            }

            throw;
        }

        return new Lease(this);
    }

    public async ValueTask DisposeAsync()
    {
        IAsyncDisposable? lease;
        lock (this.gate)
        {
            this.referenceCount = 0;
            lease = this.sharedLease;
            this.sharedLease = null;
        }

        if (lease != null)
        {
            await lease.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async ValueTask ReleaseAsync()
    {
        IAsyncDisposable? lease = null;
        lock (this.gate)
        {
            if (this.referenceCount <= 0)
            {
                return;
            }

            this.referenceCount--;
            if (this.referenceCount == 0)
            {
                lease = this.sharedLease;
                this.sharedLease = null;
            }
        }

        if (lease != null)
        {
            await lease.DisposeAsync().ConfigureAwait(false);
        }
    }

    private sealed class Lease(CoordinatedSessionInhibitor owner) : IAsyncDisposable
    {
        private CoordinatedSessionInhibitor? owner = owner;

        public async ValueTask DisposeAsync()
        {
            CoordinatedSessionInhibitor? current = Interlocked.Exchange(ref this.owner, null);
            if (current != null)
            {
                await current.ReleaseAsync().ConfigureAwait(false);
            }
        }
    }
}
