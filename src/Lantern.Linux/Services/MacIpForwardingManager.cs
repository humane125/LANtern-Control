namespace Lantern.Linux.Services;

public static class MacIpForwardingManager
{
    private const string Key = "net.inet.ip.forwarding";

    public static async Task<IAsyncDisposable> DisableAsync(CancellationToken cancellationToken)
    {
        var original = await ReadAsync(cancellationToken);
        if (original is not "0" and not "1")
        {
            throw new InvalidOperationException(
                $"macOS reported an unexpected value '{original}' for {Key}.");
        }

        if (original == "1")
        {
            await WriteAsync("0", cancellationToken);
            if (await ReadAsync(cancellationToken) != "0")
            {
                throw new InvalidOperationException(
                    "macOS IPv4 forwarding could not be disabled. " +
                    "Kernel and LANtern forwarding together would duplicate every packet.");
            }
        }

        return new MacIpForwardingSession(restoreEnabled: original == "1");
    }

    private static async Task<string> ReadAsync(CancellationToken cancellationToken)
    {
        var result = await ProcessRunner.RunAsync("/usr/sbin/sysctl", ["-n", Key], cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Could not read {Key}: {result.ErrorSummary}");
        }

        return result.StandardOutput.Trim();
    }

    private static async Task WriteAsync(string value, CancellationToken cancellationToken)
    {
        var result = await ProcessRunner.RunAsync(
            "/usr/sbin/sysctl",
            ["-w", $"{Key}={value}"],
            cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Could not set {Key}={value}: {result.ErrorSummary}. " +
                "Run LANtern Control with administrator privileges.");
        }
    }

    private sealed class MacIpForwardingSession(bool restoreEnabled) : IAsyncDisposable
    {
        private int restored;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref restored, 1) != 0 || !restoreEnabled)
            {
                return;
            }

            await WriteAsync("1", CancellationToken.None);
            if (await ReadAsync(CancellationToken.None) != "1")
            {
                throw new InvalidOperationException(
                    "macOS IPv4 forwarding could not be restored to its original state.");
            }
        }
    }
}
