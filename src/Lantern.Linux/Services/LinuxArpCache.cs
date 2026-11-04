using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;

namespace Lantern.Linux.Services;

public static class LinuxArpCache
{
    private const string DefaultPath = "/proc/net/arp";

    public static async Task<IReadOnlyList<ArpCacheEntry>> ReadAsync(
        string interfaceName,
        IPAddress localAddress,
        int prefixLength,
        CancellationToken cancellationToken)
    {
        var output = await File.ReadAllTextAsync(DefaultPath, cancellationToken);
        return Parse(output, interfaceName, localAddress, prefixLength);
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
            var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 6 ||
                !fields[5].Equals(interfaceName, StringComparison.Ordinal) ||
                !IPAddress.TryParse(fields[0], out var address) ||
                !TryParseFlags(fields[2], out var flags) ||
                (flags & 0x2) == 0 ||
                !PhysicalAddress.TryParse(
                    fields[3].Replace(":", string.Empty, StringComparison.Ordinal),
                    out var macAddress) ||
                !ArpEntryFilter.IsUsableNeighbor(address, macAddress, localAddress, prefixLength) ||
                !seen.Add(address))
            {
                continue;
            }

            entries.Add(new ArpCacheEntry(address, macAddress));
        }

        return entries;
    }

    private static bool TryParseFlags(string value, out int flags) =>
        int.TryParse(
            value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..] : value,
            NumberStyles.HexNumber,
            CultureInfo.InvariantCulture,
            out flags);
}
