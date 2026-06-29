namespace Hourglass.Linux.Services;

using Tmds.DBus.Protocol;

public sealed class DbusUnityLauncherEntrySender : IUnityLauncherEntrySender
{
    private readonly IUnityLauncherEntryBus bus;
    private readonly object lastUpdateGate = new();
    private LauncherEntrySnapshot? lastUpdate;

    internal DbusUnityLauncherEntrySender(IUnityLauncherEntryBus bus)
    {
        this.bus = bus ?? throw new ArgumentNullException(nameof(bus));
        this.WatchUnityOwnerChanges();
    }

    public static bool TryCreate(out IUnityLauncherEntrySender sender)
    {
        try
        {
            if (!TmdsUnityLauncherEntryBus.TryCreate(out IUnityLauncherEntryBus bus))
            {
                sender = new NullUnityLauncherEntrySender();
                return false;
            }

            sender = new DbusUnityLauncherEntrySender(bus);
            return true;
        }
        catch (Exception)
        {
            sender = new NullUnityLauncherEntrySender();
            return false;
        }
    }

    public async Task SendUpdateAsync(
        string applicationUri,
        UnityLauncherEntryUpdate update,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationUri);
        cancellationToken.ThrowIfCancellationRequested();

        await this.bus.SendUpdateAsync(applicationUri, update, cancellationToken).ConfigureAwait(false);

        lock (this.lastUpdateGate)
        {
            this.lastUpdate = new LauncherEntrySnapshot(applicationUri, update);
        }
    }

    private void WatchUnityOwnerChanges()
    {
        try
        {
            this.bus.WatchUnityOwnerChanged(this.UnityOwnerChanged);
        }
        catch (Exception)
        {
        }
    }

    private void UnityOwnerChanged()
    {
        LauncherEntrySnapshot? snapshot;

        lock (this.lastUpdateGate)
        {
            snapshot = this.lastUpdate;
        }

        if (snapshot is null)
        {
            return;
        }

        _ = this.ResendSnapshotAsync(snapshot.Value);
    }

    private async Task ResendSnapshotAsync(LauncherEntrySnapshot snapshot)
    {
        try
        {
            await this.bus.SendUpdateAsync(
                snapshot.ApplicationUri,
                snapshot.Update,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }

    private sealed class NullUnityLauncherEntrySender : IUnityLauncherEntrySender
    {
        public Task SendUpdateAsync(
            string applicationUri,
            UnityLauncherEntryUpdate update,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private readonly record struct LauncherEntrySnapshot(
        string ApplicationUri,
        UnityLauncherEntryUpdate Update);
}

internal interface IUnityLauncherEntryBus
{
    Task SendUpdateAsync(
        string applicationUri,
        UnityLauncherEntryUpdate update,
        CancellationToken cancellationToken = default);

    void WatchUnityOwnerChanged(Action ownerChanged);
}

internal sealed class TmdsUnityLauncherEntryBus : IUnityLauncherEntryBus
{
    private const string LauncherEntryPath = "/com/canonical/Unity/LauncherEntry";
    private const string LauncherEntryInterface = "com.canonical.Unity.LauncherEntry";
    private const string UpdateSignal = "Update";
    private const string UpdateSignature = "sa{sv}";
    private const string DBusServiceName = "org.freedesktop.DBus";
    private const string DBusObjectPath = "/org/freedesktop/DBus";
    private const string DBusInterface = "org.freedesktop.DBus";
    private const string UnityBusName = "com.canonical.Unity";
    private const string NameOwnerChangedSignal = "NameOwnerChanged";

    private readonly DBusConnection connection;
    private IDisposable? unityOwnerWatcher;

    private TmdsUnityLauncherEntryBus(DBusConnection connection)
    {
        this.connection = connection ?? throw new ArgumentNullException(nameof(connection));
    }

    public static bool TryCreate(out IUnityLauncherEntryBus bus)
    {
        try
        {
            DBusConnection connection = DBusConnection.Session;
            connection.ConnectAsync().GetAwaiter().GetResult();
            bus = new TmdsUnityLauncherEntryBus(connection);
            return true;
        }
        catch (Exception)
        {
            bus = new NullUnityLauncherEntryBus();
            return false;
        }
    }

    public Task SendUpdateAsync(
        string applicationUri,
        UnityLauncherEntryUpdate update,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationUri);
        cancellationToken.ThrowIfCancellationRequested();

        using MessageWriter writer = this.connection.GetMessageWriter();
        writer.WriteSignalHeader(
            destination: null,
            path: LauncherEntryPath,
            @interface: LauncherEntryInterface,
            member: UpdateSignal,
            signature: UpdateSignature);
        writer.WriteString(applicationUri);
        writer.WriteDictionary(new Dictionary<string, VariantValue>
        {
            ["progress-visible"] = VariantValue.Bool(update.ProgressVisible),
            ["progress"] = VariantValue.Double(Math.Clamp(update.Progress, 0, 1)),
            ["urgent"] = VariantValue.Bool(update.Urgent)
        });

        _ = this.connection.TrySendMessage(writer.CreateMessage());
        return Task.CompletedTask;
    }

    public void WatchUnityOwnerChanged(Action ownerChanged)
    {
        ArgumentNullException.ThrowIfNull(ownerChanged);

        this.unityOwnerWatcher = this.connection.WatchSignalAsync<NameOwnerChanged>(
            DBusServiceName,
            DBusObjectPath,
            DBusInterface,
            NameOwnerChangedSignal,
            ReadNameOwnerChanged,
            (_, signal) => HandleNameOwnerChanged(signal, ownerChanged),
            null,
            emitOnCapturedContext: false,
            ObserverFlags.None).GetAwaiter().GetResult();
    }

    private static NameOwnerChanged ReadNameOwnerChanged(Message message, object? state)
    {
        Reader reader = message.GetBodyReader();

        return new NameOwnerChanged(
            reader.ReadString(),
            reader.ReadString(),
            reader.ReadString());
    }

    private static void HandleNameOwnerChanged(NameOwnerChanged signal, Action ownerChanged)
    {
        if (!ShouldReemitForNameOwnerChanged(signal.Name, signal.NewOwner))
        {
            return;
        }

        try
        {
            ownerChanged();
        }
        catch (Exception)
        {
        }
    }

    internal static bool ShouldReemitForNameOwnerChanged(string name, string newOwner)
    {
        return string.Equals(name, UnityBusName, StringComparison.Ordinal)
            && !string.IsNullOrEmpty(newOwner);
    }

    private sealed class NullUnityLauncherEntryBus : IUnityLauncherEntryBus
    {
        public Task SendUpdateAsync(
            string applicationUri,
            UnityLauncherEntryUpdate update,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public void WatchUnityOwnerChanged(Action ownerChanged)
        {
        }
    }

    private readonly record struct NameOwnerChanged(
        string Name,
        string OldOwner,
        string NewOwner);
}

public sealed class DbusUnityLauncherEntrySenderFactory : IUnityLauncherEntrySenderFactory
{
    private readonly ISessionBusProbe sessionBusProbe;

    public DbusUnityLauncherEntrySenderFactory(ISessionBusProbe sessionBusProbe)
    {
        this.sessionBusProbe = sessionBusProbe ?? throw new ArgumentNullException(nameof(sessionBusProbe));
    }

    public bool TryCreate(out IUnityLauncherEntrySender sender)
    {
        if (!this.sessionBusProbe.IsSessionBusAvailable())
        {
            sender = new NullUnityLauncherEntrySender();
            return false;
        }

        return DbusUnityLauncherEntrySender.TryCreate(out sender);
    }

    private sealed class NullUnityLauncherEntrySender : IUnityLauncherEntrySender
    {
        public Task SendUpdateAsync(
            string applicationUri,
            UnityLauncherEntryUpdate update,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
