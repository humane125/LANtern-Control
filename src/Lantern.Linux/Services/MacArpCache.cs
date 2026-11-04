using System.Net;
using System.Net.NetworkInformation;

namespace Lantern.Linux.Services;

public static class MacArpCache
{
    public static async Task<IReadOnlyList<ArpCacheEntry>> ReadAsync(
        string interfaceName,
        IPAddress localAddress,
        int prefixLength,
        CancellationToken cancellationToken)
    {
        var result = await ProcessRunner.RunAsync("/usr/sbin/arp", ["-an"], cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Could not read the macOS ARP table: {result.ErrorSummary}");
        }

        return Parse(result.StandardOutput, interfaceName, localAddress, prefixLength);
    }

    public static IReadOnlyList<ArpCacheEntry> Parse(
        string output,
        string interfaceName,
        IPAddress localAddress,
        int prefixLength)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentException.ThrowIfNullOrWhiteSpace(interfaceName);
        ArpEntryFilter.ValidateSelection(localAddress, prefixLength);

        var entries = new List<ArpCacheEntry>();
        var seen = new HashSet<IPAddress>();
        foreach (var line in output.Split('\n', StringSplitOptions.TrimEntries))
        {
            if (!TryParseLine(line, out var address, out var macAddress, out var device) ||
                !device.Equals(interfaceName, StringComparison.Ordinal) ||
                !ArpEntryFilter.IsUsableNeighbor(address, macAddress, localAddress, prefixLength) ||
                !seen.Add(address))
            {
                continue;
            }

            entries.Add(new ArpCacheEntry(address, macAddress));
        }

        return entries;
    }

    private static bool TryParseLine(
        string line,
        out IPAddress address,
        out PhysicalAddress macAddress,
        out string device)
    {
        address = IPAddress.None;
        macAddress = PhysicalAddress.None;
        device = string.Empty;

        var tokens = line.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var atIndex = Array.IndexOf(tokens, "at");
        var onIndex = Array.IndexOf(tokens, "on");
        if (atIndex < 1 || onIndex < atIndex + 2 || onIndex == tokens.Length - 1)
        {
            return false;
        }

        device = tokens[onIndex + 1];
        return IPAddress.TryParse(tokens[atIndex - 1].Trim('(', ')'), out address!) &&
               MacHardwareAddress.TryParse(tokens[atIndex + 1], out macAddress);
    }
}
