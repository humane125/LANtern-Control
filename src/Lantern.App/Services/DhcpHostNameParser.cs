using System.Net.NetworkInformation;
using System.Text;

namespace Lantern.App.Services;

public readonly record struct DhcpHostNameInfo(
    PhysicalAddress MacAddress,
    string HostName);

public static class DhcpHostNameParser
{
    private const ushort Ipv4EtherType = 0x0800;
    private const ushort VlanEtherType = 0x8100;

    public static bool TryParse(
        ReadOnlySpan<byte> frame,
        out DhcpHostNameInfo result)
    {
        result = default;
        if (!TryGetIpv4Offset(frame, out var ipOffset) ||
            frame.Length < ipOffset + 20 ||
            (frame[ipOffset] >> 4) != 4 ||
            frame[ipOffset + 9] != 17)
        {
            return false;
        }

        var ipHeaderLength = (frame[ipOffset] & 0x0f) * 4;
        var udpOffset = ipOffset + ipHeaderLength;
        if (ipHeaderLength < 20 || frame.Length < udpOffset + 8)
        {
            return false;
        }

        var sourcePort = ReadUInt16(frame, udpOffset);
        var destinationPort = ReadUInt16(frame, udpOffset + 2);
        if (!IsDhcpPortPair(sourcePort, destinationPort))
        {
            return false;
        }

        var dhcpOffset = udpOffset + 8;
        if (frame.Length < dhcpOffset + 240 ||
            frame[dhcpOffset + 1] != 1 ||
            frame[dhcpOffset + 2] < 6 ||
            frame[dhcpOffset + 236] != 99 ||
            frame[dhcpOffset + 237] != 130 ||
            frame[dhcpOffset + 238] != 83 ||
            frame[dhcpOffset + 239] != 99)
        {
            return false;
        }

        var macBytes = frame.Slice(dhcpOffset + 28, 6).ToArray();
        if (macBytes.All(value => value == 0) || macBytes.All(value => value == 0xff))
        {
            return false;
        }

        var options = frame[(dhcpOffset + 240)..];
        var optionOffset = 0;
        while (optionOffset < options.Length)
        {
            var code = options[optionOffset++];
            if (code == 255)
            {
                break;
            }

            if (code == 0)
            {
                continue;
            }

            if (optionOffset >= options.Length)
            {
                return false;
            }

            var length = options[optionOffset++];
            if (optionOffset + length > options.Length)
            {
                return false;
            }

            if (code == 12)
            {
                var hostName = NormalizeHostName(
                    Encoding.UTF8.GetString(options.Slice(optionOffset, length)));
                if (hostName is not null)
                {
                    result = new DhcpHostNameInfo(
                        new PhysicalAddress(macBytes),
                        hostName);
                    return true;
                }
            }

            optionOffset += length;
        }

        return false;
    }

    private static bool TryGetIpv4Offset(ReadOnlySpan<byte> frame, out int offset)
    {
        offset = 0;
        if (frame.Length < 14)
        {
            return false;
        }

        var etherType = ReadUInt16(frame, 12);
        offset = 14;
        if (etherType == VlanEtherType)
        {
            if (frame.Length < 18)
            {
                return false;
            }

            etherType = ReadUInt16(frame, 16);
            offset = 18;
        }

        return etherType == Ipv4EtherType;
    }

    private static bool IsDhcpPortPair(ushort source, ushort destination) =>
        (source == 67 && destination == 68) ||
        (source == 68 && destination == 67);

    private static string? NormalizeHostName(string value)
    {
        var name = value.Trim().TrimEnd('.');
        if (name.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^6];
        }

        return name.Length is > 0 and <= 253 &&
               name.All(character => !char.IsControl(character))
            ? name
            : null;
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> bytes, int offset) =>
        (ushort)((bytes[offset] << 8) | bytes[offset + 1]);
}
