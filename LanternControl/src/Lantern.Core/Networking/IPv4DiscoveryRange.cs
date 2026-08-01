using System.Net;
using System.Net.Sockets;

namespace Lantern.Core.Networking;

public static class IPv4DiscoveryRange
{
    public static IReadOnlyList<IPAddress> EnumerateHosts(
        IPAddress localAddress,
        int prefixLength)
    {
        ArgumentNullException.ThrowIfNull(localAddress);
        if (localAddress.AddressFamily != AddressFamily.InterNetwork)
        {
            throw new ArgumentException("Only IPv4 addresses are supported.", nameof(localAddress));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(prefixLength);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(prefixLength, 32);

        // A full scan of a large corporate subnet would be noisy. Home networks
        // are normally /24 or smaller, so constrain larger ranges to the local /24.
        var effectivePrefix = Math.Max(prefixLength, 24);
        if (effectivePrefix >= 31)
        {
            return [];
        }

        var localValue = ToUInt32(localAddress);
        var mask = uint.MaxValue << (32 - effectivePrefix);
        var network = localValue & mask;
        var broadcast = network | ~mask;
        var hosts = new List<IPAddress>((int)(broadcast - network - 2));

        for (var value = network + 1; value < broadcast; value++)
        {
            if (value != localValue)
            {
                hosts.Add(FromUInt32(value));
            }
        }

        return hosts;
    }

    private static uint ToUInt32(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return ((uint)bytes[0] << 24) |
               ((uint)bytes[1] << 16) |
               ((uint)bytes[2] << 8) |
               bytes[3];
    }

    private static IPAddress FromUInt32(uint value)
    {
        return new IPAddress(
        [
            (byte)(value >> 24),
            (byte)(value >> 16),
            (byte)(value >> 8),
            (byte)value,
        ]);
    }
}
