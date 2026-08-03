using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Lantern.Linux.Services;

public sealed record LinuxArpCacheEntry(
    IPAddress Address,
    PhysicalAddress MacAddress);

public static class LinuxArpCache
{
    private const string DefaultPath = "/proc/net/arp";

    public static async Task<IReadOnlyList<LinuxArpCacheEntry>> ReadAsync(
        string interfaceName,
        IPAddress localAddress,
        int prefixLength,
        CancellationToken cancellationToken)
    {
        var output = await File.ReadAllTextAsync(DefaultPath, cancellationToken);
        return Parse(output, interfaceName, localAddress, prefixLength);
    }

    public static IReadOnlyList<LinuxArpCacheEntry> Parse(
        string output,
        string interfaceName,
        IPAddress localAddress,
        int prefixLength)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentException.ThrowIfNullOrWhiteSpace(interfaceName);
        ArgumentNullException.ThrowIfNull(localAddress);
        if (localAddress.AddressFamily != AddressFamily.InterNetwork)
        {
            throw new ArgumentException("Only IPv4 adapters are supported.", nameof(localAddress));
        }

        if (prefixLength is < 0 or > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(prefixLength));
        }

        var entries = new List<LinuxArpCacheEntry>();
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
                !IsUsable(address, macAddress, localAddress, prefixLength) ||
                !seen.Add(address))
            {
                continue;
            }

            entries.Add(new LinuxArpCacheEntry(address, macAddress));
        }

        return entries;
    }

    private static bool TryParseFlags(string value, out int flags) =>
        int.TryParse(
            value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..] : value,
            NumberStyles.HexNumber,
            CultureInfo.InvariantCulture,
            out flags);

    private static bool IsUsable(
        IPAddress address,
        PhysicalAddress macAddress,
        IPAddress localAddress,
        int prefixLength)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork ||
            address.Equals(localAddress) ||
            !IsInSubnet(address, localAddress, prefixLength))
        {
            return false;
        }

        var mac = macAddress.GetAddressBytes();
        if (mac.Length != 6 ||
            mac.All(value => value == 0) ||
            mac.All(value => value == byte.MaxValue) ||
            (mac[0] & 1) != 0)
        {
            return false;
        }

        if (prefixLength >= 31)
        {
            return true;
        }

        var hostMask = uint.MaxValue >> prefixLength;
        var value = ToUInt32(address);
        return (value & hostMask) is not 0 && (value & hostMask) != hostMask;
    }

    private static bool IsInSubnet(
        IPAddress address,
        IPAddress localAddress,
        int prefixLength)
    {
        var mask = prefixLength == 0 ? 0U : uint.MaxValue << (32 - prefixLength);
        return (ToUInt32(address) & mask) == (ToUInt32(localAddress) & mask);
    }

    private static uint ToUInt32(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return ((uint)bytes[0] << 24) |
               ((uint)bytes[1] << 16) |
               ((uint)bytes[2] << 8) |
               bytes[3];
    }
}
