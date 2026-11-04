using System.Net.NetworkInformation;

namespace Lantern.Linux.Services;

public static class MacInterfaceTable
{
    private static readonly TimeSpan IfconfigTimeout = TimeSpan.FromSeconds(4);

    // Synchronous because adapters are listed on the UI thread. Task.Run keeps the
    // wait off the Avalonia dispatcher so the awaited process cannot deadlock it.
    public static IReadOnlyDictionary<string, PhysicalAddress> ReadMacAddresses()
    {
        try
        {
            using var timeout = new CancellationTokenSource(IfconfigTimeout);
            var result = Task.Run(() => ProcessRunner.RunAsync("/sbin/ifconfig", ["-a"], timeout.Token))
                .GetAwaiter()
                .GetResult();
            return result.ExitCode == 0
                ? Parse(result.StandardOutput)
                : new Dictionary<string, PhysicalAddress>();
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or OperationCanceledException)
        {
            return new Dictionary<string, PhysicalAddress>();
        }
    }

    public static IReadOnlyDictionary<string, PhysicalAddress> Parse(string ifconfigOutput)
    {
        ArgumentNullException.ThrowIfNull(ifconfigOutput);
        var macs = new Dictionary<string, PhysicalAddress>(StringComparer.Ordinal);
        string? current = null;
        foreach (var line in ifconfigOutput.Split('\n'))
        {
            if (line.Length > 0 && !char.IsWhiteSpace(line[0]))
            {
                var colon = line.IndexOf(':');
                current = colon > 0 ? line[..colon] : null;
                continue;
            }

            var trimmed = line.Trim();
            if (current is not null &&
                trimmed.StartsWith("ether ", StringComparison.Ordinal) &&
                MacHardwareAddress.TryParse(trimmed["ether ".Length..].Trim(), out var mac))
            {
                macs.TryAdd(current, mac);
            }
        }

        return macs;
    }
}
