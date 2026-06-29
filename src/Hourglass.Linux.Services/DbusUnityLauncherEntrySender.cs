namespace Hourglass.Linux.Services;

using Tmds.DBus.Protocol;

public sealed class DbusUnityLauncherEntrySender : IUnityLauncherEntrySender
{
    private const string LauncherEntryPath = "/com/canonical/Unity/LauncherEntry";
    private const string LauncherEntryInterface = "com.canonical.Unity.LauncherEntry";
    private const string UpdateSignal = "Update";
    private const string UpdateSignature = "sa{sv}";

    private readonly DBusConnection connection;

    private DbusUnityLauncherEntrySender(DBusConnection connection)
    {
        this.connection = connection ?? throw new ArgumentNullException(nameof(connection));
    }

    public static bool TryCreate(out IUnityLauncherEntrySender sender)
    {
        try
        {
            DBusConnection connection = DBusConnection.Session;
            connection.ConnectAsync().GetAwaiter().GetResult();
            sender = new DbusUnityLauncherEntrySender(connection);
            return true;
        }
        catch (Exception)
        {
            sender = new NullUnityLauncherEntrySender();
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
