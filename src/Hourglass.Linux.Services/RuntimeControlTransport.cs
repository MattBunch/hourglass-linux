namespace Hourglass.Linux.Services;

using System.Buffers.Binary;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using Hourglass.Application;

internal static class RuntimeControlTransport
{
    internal const int Version = 1;
    private const uint CredentialLength = 12;
    private const int SocketLevel = 1;
    private const int PeerCredentialsOption = 17;
    internal const int RequestLimit = 64 * 1024;
    internal const int ResponseLimit = 1024 * 1024;
    internal static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);
    internal static readonly TimeSpan ReadinessDeadline = TimeSpan.FromSeconds(10);
    internal static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);
    internal static string DefaultPath => Path.Combine(Path.GetDirectoryName(LinuxSingleInstanceLockPath.Resolve(
        Environment.GetEnvironmentVariable, () => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)))
        ?? throw new InvalidOperationException("Missing runtime directory."), "hourglass-control.sock");

    internal static async Task<T?> ReadAsync<T>(Stream stream, int limit, CancellationToken token) where T : class
    {
        byte[] header = new byte[4];
        int first = await stream.ReadAsync(header.AsMemory(0, 1), token).ConfigureAwait(false);
        if (first == 0) { return null; }
        await stream.ReadExactlyAsync(header.AsMemory(1), token).ConfigureAwait(false);
        int length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length <= 0 || length > limit) { throw new InvalidDataException("Control frame exceeds its size limit."); }
        byte[] payload = new byte[length];
        await stream.ReadExactlyAsync(payload, token).ConfigureAwait(false);
        return JsonSerializer.Deserialize<T>(payload, RuntimeControlJson.Options) ?? throw new JsonException("Empty control frame.");
    }

    internal static async Task WriteAsync<T>(Stream stream, T value, int limit, CancellationToken token)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(value, RuntimeControlJson.Options);
        if (payload.Length > limit) { throw new InvalidDataException("Control frame exceeds its size limit."); }
        byte[] header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, payload.Length);
        await stream.WriteAsync(header, token).ConfigureAwait(false);
        await stream.WriteAsync(payload, token).ConfigureAwait(false);
    }

    internal static void VerifyPeer(Socket socket)
    {
        uint length = CredentialLength;
        if (GetSocketOption(socket.Handle.ToInt32(), SocketLevel, PeerCredentialsOption, out PeerCredentials credentials, ref length) != 0
             )
        { throw new UnauthorizedAccessException("Cannot verify control peer credentials."); }
        VerifyPeerIdentity(credentials.UserId, GetUserId(), length);
    }

    internal static void VerifyPeerIdentity(uint peerUserId, uint currentUserId, uint credentialLength)
    {
        if (credentialLength != CredentialLength || peerUserId != currentUserId)
        { throw new UnauthorizedAccessException("Control peer must belong to the current user."); }
    }

    internal static void VerifyPath(string path, bool directory, bool requirePrivate = true)
    {
        if (Stat(-100, path, 0x100, 0x7ff, out FileStatus status) != 0 || status.UserId != GetUserId()
            || (status.Mode & 0xf000) != (directory ? 0x4000 : 0xc000)
            || (requirePrivate && (status.Mode & 0x3f) != 0))
        { throw new UnauthorizedAccessException("Control endpoint must be private, owned by the current user, and have the expected type."); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PeerCredentials { internal int ProcessId; internal uint UserId; internal uint GroupId; }
    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct FileStatus { [FieldOffset(20)] internal uint UserId; [FieldOffset(28)] internal ushort Mode; }
    [DllImport("libc", EntryPoint = "getsockopt", SetLastError = true)]
    private static extern int GetSocketOption(int socket, int level, int option, out PeerCredentials value, ref uint length);
    [DllImport("libc", EntryPoint = "geteuid")]
    private static extern uint GetUserId();
    [DllImport("libc", EntryPoint = "statx", SetLastError = true)]
    private static extern int Stat(int directory, string path, int flags, uint mask, out FileStatus status);
}
