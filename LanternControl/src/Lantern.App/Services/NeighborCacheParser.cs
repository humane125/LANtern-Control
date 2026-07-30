using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace Lantern.App.Services;

public sealed record NeighborCacheEntry(
    IPAddress Address,
    PhysicalAddress MacAddress);

public static partial class NeighborCacheParser
{
    public static IReadOnlyList<NeighborCacheEntry> Parse(
        string output,
        IPAddress localAddress,
        int prefixLength)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(localAddress);
        if (localAddress.AddressFamily != AddressFamily.InterNetwork)
        {
            throw new ArgumentException("Only IPv4 adapters are supported.", nameof(localAddress));
        }

        if (prefixLength is < 0 or > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(prefixLength));
        }

        var result = new List<NeighborCacheEntry>();
        var seenAddresses = new HashSet<IPAddress>();
        foreach (Match match in NeighborLine().Matches(output))
        {
            if (!IPAddress.TryParse(match.Groups["ip"].Value, out var address) ||
                !PhysicalAddress.TryParse(
                    match.Groups["mac"].Value.Replace("-", string.Empty).Replace(":", string.Empty),
                    out var macAddress) ||
                !IsUsableNeighbor(address, macAddress, localAddress, prefixLength) ||
                !seenAddresses.Add(address))
            {
                continue;
            }

            result.Add(new NeighborCacheEntry(address, macAddress));
        }

        return result;
    }

    private static bool IsUsableNeighbor(
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

        var macBytes = macAddress.GetAddressBytes();
        if (macBytes.Length != 6 ||
            macBytes.All(value => value == 0) ||
            macBytes.All(value => value == byte.MaxValue) ||
            (macBytes[0] & 1) != 0)
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

    [GeneratedRegex(
        @"(?m)^\s*(?<ip>(?:\d{1,3}\.){3}\d{1,3})\s+(?<mac>(?:[0-9a-f]{2}[-:]){5}[0-9a-f]{2})(?:\s|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NeighborLine();
}
