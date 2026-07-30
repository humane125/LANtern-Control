using System.Net;
using System.Net.NetworkInformation;

namespace Lantern.Core.Networking;

public enum ArpOperation : ushort
{
    Request = 1,
    Reply = 2,
}

public readonly record struct ArpFrameInfo(
    ArpOperation Operation,
    PhysicalAddress SenderMac,
    IPAddress SenderIp,
    PhysicalAddress TargetMac,
    IPAddress TargetIp);

public readonly record struct Ipv4FrameInfo(
    int HeaderOffset,
    PhysicalAddress SourceMac,
    PhysicalAddress DestinationMac,
    IPAddress Source,
    IPAddress Destination);

public static class EthernetFrameCodec
{
    private const ushort EtherTypeIpv4 = 0x0800;
    private const ushort EtherTypeArp = 0x0806;
    private const ushort EtherTypeVlan = 0x8100;
    private static readonly byte[] BroadcastMac = [0xff, 0xff, 0xff, 0xff, 0xff, 0xff];

    public static byte[] BuildArpRequest(
        PhysicalAddress senderMac,
        IPAddress senderIp,
        IPAddress targetIp) =>
        BuildArpFrame(
            BroadcastMac,
            senderMac.GetAddressBytes(),
            ArpOperation.Request,
            senderMac.GetAddressBytes(),
            senderIp.GetAddressBytes(),
            new byte[6],
            targetIp.GetAddressBytes());

    public static byte[] BuildArpReply(
        PhysicalAddress senderMac,
        IPAddress senderIp,
        PhysicalAddress targetMac,
        IPAddress targetIp) =>
        BuildArpFrame(
            targetMac.GetAddressBytes(),
            senderMac.GetAddressBytes(),
            ArpOperation.Reply,
            senderMac.GetAddressBytes(),
            senderIp.GetAddressBytes(),
            targetMac.GetAddressBytes(),
            targetIp.GetAddressBytes());

    public static bool TryParseArp(ReadOnlySpan<byte> frame, out ArpFrameInfo result)
    {
        result = default;
        if (!TryGetPayload(frame, out var etherType, out var payloadOffset) ||
            etherType != EtherTypeArp ||
            frame.Length < payloadOffset + 28)
        {
            return false;
        }

        var arp = frame[payloadOffset..];
        if (ReadUInt16(arp, 0) != 1 ||
            ReadUInt16(arp, 2) != EtherTypeIpv4 ||
            arp[4] != 6 ||
            arp[5] != 4)
        {
            return false;
        }

        var operationValue = ReadUInt16(arp, 6);
        if (operationValue is not ((ushort)ArpOperation.Request) and not ((ushort)ArpOperation.Reply))
        {
            return false;
        }

        result = new ArpFrameInfo(
            (ArpOperation)operationValue,
            new PhysicalAddress(arp.Slice(8, 6).ToArray()),
            new IPAddress(arp.Slice(14, 4)),
            new PhysicalAddress(arp.Slice(18, 6).ToArray()),
            new IPAddress(arp.Slice(24, 4)));
        return true;
    }

    public static bool TryParseIpv4(ReadOnlySpan<byte> frame, out Ipv4FrameInfo result)
    {
        result = default;
        if (!TryGetPayload(frame, out var etherType, out var payloadOffset) ||
            etherType != EtherTypeIpv4 ||
            frame.Length < payloadOffset + 20 ||
            (frame[payloadOffset] >> 4) != 4)
        {
            return false;
        }

        var headerLength = (frame[payloadOffset] & 0x0f) * 4;
        if (headerLength < 20 || frame.Length < payloadOffset + headerLength)
        {
            return false;
        }

        result = new Ipv4FrameInfo(
            payloadOffset,
            new PhysicalAddress(frame.Slice(6, 6).ToArray()),
            new PhysicalAddress(frame[..6].ToArray()),
            new IPAddress(frame.Slice(payloadOffset + 12, 4)),
            new IPAddress(frame.Slice(payloadOffset + 16, 4)));
        return true;
    }

    public static byte[] RewriteEthernetAddresses(
        ReadOnlySpan<byte> frame,
        PhysicalAddress source,
        PhysicalAddress destination)
    {
        if (frame.Length < 14)
        {
            throw new ArgumentException("An Ethernet frame must contain at least 14 bytes.", nameof(frame));
        }

        var rewritten = frame.ToArray();
        destination.GetAddressBytes().CopyTo(rewritten, 0);
        source.GetAddressBytes().CopyTo(rewritten, 6);
        return rewritten;
    }

    private static byte[] BuildArpFrame(
        byte[] ethernetDestination,
        byte[] ethernetSource,
        ArpOperation operation,
        byte[] senderMac,
        byte[] senderIp,
        byte[] targetMac,
        byte[] targetIp)
    {
        ValidateMac(ethernetDestination);
        ValidateMac(ethernetSource);
        ValidateMac(senderMac);
        ValidateMac(targetMac);
        ValidateIpv4(senderIp);
        ValidateIpv4(targetIp);

        var frame = new byte[42];
        ethernetDestination.CopyTo(frame, 0);
        ethernetSource.CopyTo(frame, 6);
        WriteUInt16(frame, 12, EtherTypeArp);
        WriteUInt16(frame, 14, 1);
        WriteUInt16(frame, 16, EtherTypeIpv4);
        frame[18] = 6;
        frame[19] = 4;
        WriteUInt16(frame, 20, (ushort)operation);
        senderMac.CopyTo(frame, 22);
        senderIp.CopyTo(frame, 28);
        targetMac.CopyTo(frame, 32);
        targetIp.CopyTo(frame, 38);
        return frame;
    }

    private static bool TryGetPayload(ReadOnlySpan<byte> frame, out ushort etherType, out int payloadOffset)
    {
        etherType = 0;
        payloadOffset = 0;
        if (frame.Length < 14)
        {
            return false;
        }

        etherType = ReadUInt16(frame, 12);
        payloadOffset = 14;
        if (etherType == EtherTypeVlan)
        {
            if (frame.Length < 18)
            {
                return false;
            }

            etherType = ReadUInt16(frame, 16);
            payloadOffset = 18;
        }

        return true;
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> bytes, int offset) =>
        (ushort)((bytes[offset] << 8) | bytes[offset + 1]);

    private static void WriteUInt16(Span<byte> bytes, int offset, ushort value)
    {
        bytes[offset] = (byte)(value >> 8);
        bytes[offset + 1] = (byte)value;
    }

    private static void ValidateMac(byte[] bytes)
    {
        if (bytes.Length != 6)
        {
            throw new ArgumentException("A MAC address must contain six bytes.");
        }
    }

    private static void ValidateIpv4(byte[] bytes)
    {
        if (bytes.Length != 4)
        {
            throw new ArgumentException("An IPv4 address must contain four bytes.");
        }
    }
}
