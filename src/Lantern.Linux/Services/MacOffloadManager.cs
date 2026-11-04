using System.Text.RegularExpressions;

namespace Lantern.Linux.Services;

// Only LRO is disabled: it coalesces inbound segments before they are metered.
// TSO affects this host's own outbound TCP, not forwarded frames.
public static partial class MacOffloadManager
{
    public static bool IsLroEnabled(string ifconfigOutput)
    {
        ArgumentNullException.ThrowIfNull(ifconfigOutput);
        var options = OptionsList().Match(ifconfigOutput);
        return options.Success && options.Groups[1].Value
            .Split(',', StringSplitOptions.TrimEntries)
            .Contains("LRO", StringComparer.OrdinalIgnoreCase);
    }

    public static async Task<IAsyncDisposable> DisableAsync(
        string interfaceName,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(interfaceName);
        try
        {
            var query = await ProcessRunner.RunAsync("/sbin/ifconfig", [interfaceName], cancellationToken);
            if (query.ExitCode == 0 && IsLroEnabled(query.StandardOutput))
            {
                var result = await ProcessRunner.RunAsync(
                    "/sbin/ifconfig",
                    [interfaceName, "-lro"],
                    cancellationToken);
                return new MacOffloadSession(interfaceName, restoreLro: result.ExitCode == 0);
            }
        }
        catch (InvalidOperationException)
        {
        }

        return new MacOffloadSession(interfaceName, restoreLro: false);
    }

    [GeneratedRegex(@"options=[0-9a-f]*<([^>]*)>", RegexOptions.IgnoreCase)]
    private static partial Regex OptionsList();

    private sealed class MacOffloadSession(string interfaceName, bool restoreLro) : IAsyncDisposable
    {
        private int restored;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref restored, 1) != 0 || !restoreLro)
            {
                return;
            }

            var result = await ProcessRunner.RunAsync(
                "/sbin/ifconfig",
                [interfaceName, "lro"],
                CancellationToken.None);
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Could not re-enable LRO on '{interfaceName}': {result.ErrorSummary}");
            }
        }
    }
}
