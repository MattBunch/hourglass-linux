using Hourglass.Platform;

namespace Hourglass.Linux.Avalonia;

internal sealed class CoordinatedSessionInhibitor(ISessionInhibitor inner) : ISessionInhibitor, IAsyncDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly ISessionInhibitor inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private Task<IAsyncDisposable?>? acquisitionTask;
    private IAsyncDisposable? sharedLease;
    private bool backendReady;
    private bool disposed;
    private int referenceCount;

    public async ValueTask<IAsyncDisposable?> InhibitAsync(
        string reason,
        bool inhibitSuspend,
        bool inhibitIdle,
        CancellationToken cancellationToken = default)
    {
        Task<IAsyncDisposable?>? pendingAcquisition = null;

        await this.gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            this.ThrowIfDisposed();

            this.referenceCount++;
            if (this.backendReady)
            {
                return new Lease(this);
            }

            this.acquisitionTask ??= this.AcquireSharedLeaseAsync(reason, inhibitSuspend, inhibitIdle);
            pendingAcquisition = this.acquisitionTask;
        }
        finally
        {
            this.gate.Release();
        }

        try
        {
            await pendingAcquisition.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await this.ReleaseReferenceAfterUnsuccessfulAcquireAsync().ConfigureAwait(false);
            throw;
        }

        await this.gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (this.disposed)
            {
                await this.ReleaseReferenceAfterUnsuccessfulAcquireCoreAsync().ConfigureAwait(false);
                throw new ObjectDisposedException(nameof(CoordinatedSessionInhibitor));
            }

            return new Lease(this);
        }
        finally
        {
            this.gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task<IAsyncDisposable?>? pendingAcquisition;
        IAsyncDisposable? lease;

        await this.gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (this.disposed)
            {
                return;
            }

            this.disposed = true;
            this.referenceCount = 0;
            pendingAcquisition = this.acquisitionTask;
            lease = this.sharedLease;
            this.sharedLease = null;
            this.backendReady = false;
        }
        finally
        {
            this.gate.Release();
        }

        if (pendingAcquisition != null)
        {
            try
            {
                await pendingAcquisition.ConfigureAwait(false);
            }
            catch
            {
            }
        }

        if (lease != null)
        {
            await lease.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task<IAsyncDisposable?> AcquireSharedLeaseAsync(
        string reason,
        bool inhibitSuspend,
        bool inhibitIdle)
    {
        IAsyncDisposable? acquiredLease = null;
        try
        {
            acquiredLease = await this.inner.InhibitAsync(
                reason,
                inhibitSuspend,
                inhibitIdle,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            await this.gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                this.acquisitionTask = null;
                this.backendReady = false;
            }
            finally
            {
                this.gate.Release();
            }

            throw;
        }

        IAsyncDisposable? leaseToDispose = null;
        await this.gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            this.acquisitionTask = null;
            if (this.disposed || this.referenceCount == 0)
            {
                leaseToDispose = acquiredLease;
            }
            else
            {
                this.sharedLease = acquiredLease;
                this.backendReady = true;
            }
        }
        finally
        {
            this.gate.Release();
        }

        if (leaseToDispose != null)
        {
            await leaseToDispose.DisposeAsync().ConfigureAwait(false);
        }

        return acquiredLease;
    }

    private async ValueTask ReleaseAsync()
    {
        IAsyncDisposable? lease = null;

        await this.gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (this.referenceCount <= 0)
            {
                return;
            }

            this.referenceCount--;
            if (this.referenceCount == 0 && this.backendReady)
            {
                lease = this.sharedLease;
                this.sharedLease = null;
                this.backendReady = false;
            }
        }
        finally
        {
            this.gate.Release();
        }

        if (lease != null)
        {
            await lease.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async ValueTask ReleaseReferenceAfterUnsuccessfulAcquireAsync()
    {
        await this.gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            await this.ReleaseReferenceAfterUnsuccessfulAcquireCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            this.gate.Release();
        }
    }

    private ValueTask ReleaseReferenceAfterUnsuccessfulAcquireCoreAsync()
    {
        if (this.referenceCount > 0)
        {
            this.referenceCount--;
        }

        return ValueTask.CompletedTask;
    }

    private void ThrowIfDisposed()
    {
        if (this.disposed)
        {
            throw new ObjectDisposedException(nameof(CoordinatedSessionInhibitor));
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
