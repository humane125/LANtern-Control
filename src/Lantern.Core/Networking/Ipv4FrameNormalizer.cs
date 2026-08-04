namespace Lantern.Core.Networking;

public static class Ipv4FrameNormalizer
{
    private const ushort Ipv4EtherType = 0x0800;
    private const ushort VlanEtherType = 0x8100;
    private const byte TcpProtocol = 6;

    public static IReadOnlyList<byte[]> Normalize(
        ReadOnlySpan<byte> frame,
        int ipMtu = 1_500)
    {
        if (ipMtu is < 576 or > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(ipMtu));
        }

        if (!TryGetIpv4Offset(frame, out var ipv4Offset) ||
            frame.Length < ipv4Offset + 20)
        {
            return [frame.ToArray()];
        }

        var ipv4HeaderLength = (frame[ipv4Offset] & 0x0f) * 4;
        var totalLength = ReadUInt16(frame, ipv4Offset + 2);
        if (ipv4HeaderLength < 20 ||
            totalLength < ipv4HeaderLength ||
            frame.Length < ipv4Offset + totalLength)
        {
            return [frame.ToArray()];
        }

        var canonicalFrame = frame[..(ipv4Offset + totalLength)];
        if (totalLength <= ipMtu)
        {
            return [canonicalFrame.ToArray()];
        }

        var fragmentField = ReadUInt16(frame, ipv4Offset + 6);
        var isFragmented = (fragmentField & 0x3fff) != 0;
        if (frame[ipv4Offset + 9] != TcpProtocol || isFragmented)
        {
            throw new InvalidOperationException(
                $"Npcap captured an oversized IPv4 frame ({totalLength} byte IP packet) " +
                "that cannot be safely segmented.");
        }

        var tcpOffset = ipv4Offset + ipv4HeaderLength;
        if (totalLength < ipv4HeaderLength + 20)
        {
            throw new InvalidOperationException("Npcap captured an invalid oversized TCP frame.");
        }

        var tcpHeaderLength = (frame[tcpOffset + 12] >> 4) * 4;
        if (tcpHeaderLength < 20 ||
            totalLength < ipv4HeaderLength + tcpHeaderLength)
        {
            throw new InvalidOperationException("Npcap captured an invalid oversized TCP header.");
        }

        var maximumPayloadLength = ipMtu - ipv4HeaderLength - tcpHeaderLength;
        if (maximumPayloadLength <= 0)
        {
            throw new InvalidOperationException("The adapter MTU is too small for this TCP header.");
        }

        var tcpPayloadOffset = tcpOffset + tcpHeaderLength;
        var tcpPayloadLength = totalLength - ipv4HeaderLength - tcpHeaderLength;
        var originalSequence = ReadUInt32(frame, tcpOffset + 4);
        var originalFlags = frame[tcpOffset + 13];
        var segmentCount = (tcpPayloadLength + maximumPayloadLength - 1) /
                           maximumPayloadLength;
        var segments = new List<byte[]>(segmentCount);

        for (var payloadPosition = 0; payloadPosition < tcpPayloadLength;)
        {
            var payloadLength = Math.Min(
                maximumPayloadLength,
                tcpPayloadLength - payloadPosition);
            var segmentNetworkLength = ipv4HeaderLength + tcpHeaderLength + payloadLength;
            var segment = new byte[ipv4Offset + segmentNetworkLength];
            frame[..tcpPayloadOffset].CopyTo(segment);
            frame.Slice(tcpPayloadOffset + payloadPosition, payloadLength)
                .CopyTo(segment.AsSpan(tcpPayloadOffset));

            WriteUInt16(segment, ipv4Offset + 2, (ushort)segmentNetworkLength);
            WriteUInt16(
                segment,
                ipv4Offset + 4,
                (ushort)(ReadUInt16(frame, ipv4Offset + 4) + segments.Count));
            WriteUInt32(segment, tcpOffset + 4, originalSequence + (uint)payloadPosition);
            if (payloadPosition + payloadLength < tcpPayloadLength)
            {
                segment[tcpOffset + 13] = (byte)(originalFlags & ~0x09);
            }

            WriteUInt16(segment, ipv4Offset + 10, 0);
            WriteUInt16(
                segment,
                ipv4Offset + 10,
                ComputeChecksum(segment.AsSpan(ipv4Offset, ipv4HeaderLength)));
            WriteUInt16(segment, tcpOffset + 16, 0);
            WriteUInt16(
                segment,
                tcpOffset + 16,
                ComputeTcpChecksum(
                    segment,
                    ipv4Offset,
                    tcpOffset,
                    tcpHeaderLength + payloadLength));

            segments.Add(segment);
            payloadPosition += payloadLength;
        }

        return segments;
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

        return etherType == Ipv4EtherType &&
               frame.Length > offset &&
               (frame[offset] >> 4) == 4;
    }

    private static ushort ComputeTcpChecksum(
        ReadOnlySpan<byte> frame,
        int ipv4Offset,
        int tcpOffset,
        int tcpLength)
    {
        uint sum = 0;
        sum = AddWords(sum, frame.Slice(ipv4Offset + 12, 8));
        sum += TcpProtocol;
        sum += (uint)tcpLength;
        sum = AddWords(sum, frame.Slice(tcpOffset, tcpLength));
        return FinishChecksum(sum);
    }

    private static ushort ComputeChecksum(ReadOnlySpan<byte> bytes) =>
        FinishChecksum(AddWords(0, bytes));

    private static uint AddWords(uint sum, ReadOnlySpan<byte> bytes)
    {
        var index = 0;
        for (; index + 1 < bytes.Length; index += 2)
        {
            sum += (uint)((bytes[index] << 8) | bytes[index + 1]);
        }

        if (index < bytes.Length)
        {
            sum += (uint)(bytes[index] << 8);
        }

        return sum;
    }

    private static ushort FinishChecksum(uint sum)
    {
        while ((sum >> 16) != 0)
        {
            sum = (sum & 0xffff) + (sum >> 16);
        }

        return (ushort)~sum;
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> bytes, int offset) =>
        (ushort)((bytes[offset] << 8) | bytes[offset + 1]);

    private static uint ReadUInt32(ReadOnlySpan<byte> bytes, int offset) =>
        ((uint)bytes[offset] << 24) |
        ((uint)bytes[offset + 1] << 16) |
        ((uint)bytes[offset + 2] << 8) |
        bytes[offset + 3];

    private static void WriteUInt16(Span<byte> bytes, int offset, ushort value)
    {
        bytes[offset] = (byte)(value >> 8);
        bytes[offset + 1] = (byte)value;
    }

    private static void WriteUInt32(Span<byte> bytes, int offset, uint value)
    {
        bytes[offset] = (byte)(value >> 24);
        bytes[offset + 1] = (byte)(value >> 16);
        bytes[offset + 2] = (byte)(value >> 8);
        bytes[offset + 3] = (byte)value;
    }
}
