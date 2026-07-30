using System.Net;
using System.Net.Sockets;

namespace Lantern.Core.Networking;

public static class SubnetScanner
{
    private const int MaximumAddressCount = 1024;

    public static IReadOnlyList<IPAddress> EnumerateHosts(IPAddress address, int prefixLength)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            throw new ArgumentException("Only IPv4 subnets are supported.", nameof(address));
        }

        if (prefixLength is < 0 or > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(prefixLength));
        }

        var addressCount = 1L << (32 - prefixLength);
        if (addressCount > MaximumAddressCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(prefixLength),
                $"Subnets larger than {MaximumAddressCount} addresses are not scanned.");
        }

        if (prefixLength >= 31)
        {
            return Array.Empty<IPAddress>();
        }

        var value = ToUInt32(address);
        var mask = prefixLength == 0 ? 0U : uint.MaxValue << (32 - prefixLength);
        var network = value & mask;
        var hostCount = checked((int)addressCount - 2);
        var result = new IPAddress[hostCount];
        for (var index = 0; index < hostCount; index++)
        {
            result[index] = FromUInt32(network + (uint)index + 1U);
        }

        return result;
    }

    private static uint ToUInt32(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return ((uint)bytes[0] << 24) |
               ((uint)bytes[1] << 16) |
               ((uint)bytes[2] << 8) |
               bytes[3];
    }

    private static IPAddress FromUInt32(uint value) =>
        new(
            new[]
            {
                (byte)(value >> 24),
                (byte)(value >> 16),
                (byte)(value >> 8),
                (byte)value,
            });
}
