using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Lantern.Linux.Services;

public sealed record ArpCacheEntry(
    IPAddress Address,
    PhysicalAddress MacAddress);

public static class ArpEntryFilter
{
    public static void ValidateSelection(IPAddress localAddress, int prefixLength)
    {
        ArgumentNullException.ThrowIfNull(localAddress);
        if (localAddress.AddressFamily != AddressFamily.InterNetwork)
        {
            throw new ArgumentException("Only IPv4 adapters are supported.", nameof(localAddress));
        }

        if (prefixLength is < 0 or > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(prefixLength));
        }
    }

    public static bool IsUsableNeighbor(
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

    private static bool IsInSubnet(IPAddress address, IPAddress localAddress, int prefixLength)
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
