namespace Hourglass.Linux.Services;

using System.Text;
using Hourglass.Platform;

public sealed class LinuxFileLockSingleInstanceService : ISingleInstanceService
{
    private const string AppDirectoryName = "hourglass-linux";
    private const string ApplicationId = "hourglass-linux";
    private const string LockFileName = "hourglass-linux.lock";
    private const long LockLength = 1;
    private const long LockOffset = 0;

    private readonly ILockFileSystem fileSystem;
    private readonly Func<DateTimeOffset> getTimestamp;
    private readonly Func<int> getProcessId;
    private readonly string lockPath;
    private readonly object syncRoot = new();

    private ILockFileHandle? lockHandle;
    private bool disposed;

    public LinuxFileLockSingleInstanceService()
        : this(
            LinuxSingleInstanceLockPath.Resolve(
                Environment.GetEnvironmentVariable,
                () => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
            new LockFileSystem(),
            () => Environment.ProcessId,
            () => DateTimeOffset.UtcNow)
    {
    }

    internal LinuxFileLockSingleInstanceService(
        string lockPath,
        ILockFileSystem fileSystem,
        Func<int> getProcessId,
        Func<DateTimeOffset> getTimestamp)
    {
        this.lockPath = string.IsNullOrWhiteSpace(lockPath)
            ? throw new ArgumentException("Lock path must not be empty.", nameof(lockPath))
            : lockPath;
        this.fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        this.getProcessId = getProcessId ?? throw new ArgumentNullException(nameof(getProcessId));
        this.getTimestamp = getTimestamp ?? throw new ArgumentNullException(nameof(getTimestamp));
    }

    public Task<bool> TryAcquireAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (this.syncRoot)
        {
            ObjectDisposedException.ThrowIf(this.disposed, this);

            if (this.lockHandle != null)
            {
                return Task.FromResult(true);
            }

            string directory = Path.GetDirectoryName(this.lockPath)
                ?? throw new InvalidOperationException("Lock path must include a directory.");
            this.fileSystem.CreateDirectory(directory);

            ILockFileHandle candidate = this.fileSystem.OpenLockFile(this.lockPath);
            bool candidateDisposed = false;

            try
            {
                try
                {
                    candidate.Lock(LockOffset, LockLength);
                }
                catch (IOException)
                {
                    candidate.Dispose();
                    candidateDisposed = true;
                    return Task.FromResult(false);
                }

                try
                {
                    candidate.WriteDiagnostics(CreateDiagnostics(this.getProcessId(), this.getTimestamp()));
                }
                catch
                {
                    candidate.Unlock(LockOffset, LockLength);
                    candidate.Dispose();
                    candidateDisposed = true;
                    throw;
                }

                this.lockHandle = candidate;
                return Task.FromResult(true);
            }
            catch
            {
                if (!candidateDisposed && !ReferenceEquals(this.lockHandle, candidate))
                {
                    candidate.Dispose();
                }

                throw;
            }
        }
    }

    public void Dispose()
    {
        lock (this.syncRoot)
        {
            if (this.disposed)
            {
                return;
            }

            this.disposed = true;

            if (this.lockHandle == null)
            {
                return;
            }

            try
            {
                this.lockHandle.Unlock(LockOffset, LockLength);
            }
            finally
            {
                this.lockHandle.Dispose();
                this.lockHandle = null;
            }
        }
    }

    internal static string CreateDiagnostics(int processId, DateTimeOffset timestamp)
    {
        return $"app={ApplicationId}{Environment.NewLine}pid={processId}{Environment.NewLine}acquiredUtc={timestamp:O}{Environment.NewLine}";
    }

    internal interface ILockFileSystem
    {
        void CreateDirectory(string path);

        ILockFileHandle OpenLockFile(string path);
    }

    internal interface ILockFileHandle : IDisposable
    {
        void Lock(long offset, long length);

        void Unlock(long offset, long length);

        void WriteDiagnostics(string contents);
    }

    private sealed class LockFileSystem : ILockFileSystem
    {
        public void CreateDirectory(string path)
        {
            Directory.CreateDirectory(path);
        }

        public ILockFileHandle OpenLockFile(string path)
        {
            var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
            return new FileStreamLockFileHandle(stream);
        }
    }

    private sealed class FileStreamLockFileHandle(FileStream stream) : ILockFileHandle
    {
        private readonly FileStream stream = stream;

        public void Lock(long offset, long length)
        {
#pragma warning disable CA1416
            this.stream.Lock(offset, length);
#pragma warning restore CA1416
        }

        public void Unlock(long offset, long length)
        {
#pragma warning disable CA1416
            this.stream.Unlock(offset, length);
#pragma warning restore CA1416
        }

        public void WriteDiagnostics(string contents)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(contents);
            this.stream.SetLength(0);
            this.stream.Position = 0;
            this.stream.Write(bytes, 0, bytes.Length);
            this.stream.Flush(flushToDisk: true);
        }

        public void Dispose()
        {
            this.stream.Dispose();
        }
    }
}
