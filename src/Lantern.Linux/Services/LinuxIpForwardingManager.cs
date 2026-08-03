using System.Text;

namespace Lantern.Linux.Services;

public static class LinuxIpForwardingManager
{
    private const string DefaultPath = "/proc/sys/net/ipv4/ip_forward";

    public static Task<LinuxIpForwardingSession> DisableAsync(
        CancellationToken cancellationToken) =>
        DisableAsync(DefaultPath, cancellationToken);

    public static async Task<LinuxIpForwardingSession> DisableAsync(
        string path,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var original = (await File.ReadAllTextAsync(path, cancellationToken)).Trim();
        if (original is not "0" and not "1")
        {
            throw new InvalidOperationException(
                $"Kernel IPv4 forwarding reported unexpected value '{original}'.");
        }

        if (original == "1")
        {
            await WriteValueAsync(path, "0\n", cancellationToken);
            var verified = (await File.ReadAllTextAsync(path, cancellationToken)).Trim();
            if (verified != "0")
            {
                throw new InvalidOperationException(
                    "Linux kernel IPv4 forwarding could not be disabled. " +
                    "Kernel and LANtern forwarding together would duplicate every packet.");
            }
        }

        return new LinuxIpForwardingSession(path, original == "1");
    }

    internal static async Task WriteValueAsync(
        string path,
        string value,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Write,
            FileShare.ReadWrite,
            bufferSize: 16,
            useAsync: true);
        await stream.WriteAsync(Encoding.ASCII.GetBytes(value), cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }
}

public sealed class LinuxIpForwardingSession(
    string path,
    bool restoreEnabled) : IAsyncDisposable
{
    private int restored;

    public bool WasEnabled { get; } = restoreEnabled;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref restored, 1) != 0 || !WasEnabled)
        {
            return;
        }

        await LinuxIpForwardingManager.WriteValueAsync(
            path,
            "1\n",
            CancellationToken.None);
    }
}
