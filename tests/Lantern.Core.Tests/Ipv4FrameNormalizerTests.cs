using Lantern.Core.Networking;

namespace Lantern.Core.Tests;

public sealed class Ipv4FrameNormalizerTests
{
    [Fact]
    public void HeaderOnlyEthernetCapture_IsLeftUnchanged()
    {
        var captured = new byte[14];
        captured[12] = 0x08;
        captured[13] = 0x00;

        var normalized = Ipv4FrameNormalizer.Normalize(captured);

        Assert.Single(normalized);
        Assert.Equal(captured, normalized[0]);
    }

    [Fact]
    public void OversizedTcpCapture_IsSplitIntoMtuSizedFrames()
    {
        var captured = BuildTcpFrame(payloadLength: 3_000, sequence: 0x01020304);

        var normalized = Ipv4FrameNormalizer.Normalize(captured, ipMtu: 1_500);

        Assert.Equal(3, normalized.Count);
        Assert.Equal([1_514, 1_514, 134], normalized.Select(frame => frame.Length));
        Assert.Equal(
            [0x01020304u, 0x010208B8u, 0x01020E6Cu],
            normalized.Select(ReadTcpSequence));
        Assert.Equal([0x10, 0x10, 0x18], normalized.Select(frame => frame[47]));
        Assert.All(normalized, frame =>
        {
            Assert.Equal(0, SumChecksumWords(frame.AsSpan(14, 20)));
            Assert.Equal(0, SumTcpChecksumWords(frame));
        });
    }

    private static byte[] BuildTcpFrame(int payloadLength, uint sequence)
    {
        const int ethernetLength = 14;
        const int ipv4Length = 20;
        const int tcpLength = 20;
        var frame = new byte[ethernetLength + ipv4Length + tcpLength + payloadLength];
        frame[12] = 0x08;
        frame[13] = 0x00;
        frame[14] = 0x45;
        WriteUInt16(frame, 16, (ushort)(ipv4Length + tcpLength + payloadLength));
        WriteUInt16(frame, 18, 0x1234);
        frame[22] = 64;
        frame[23] = 6;
        frame[26] = 192;
        frame[27] = 168;
        frame[28] = 1;
        frame[29] = 10;
        frame[30] = 192;
        frame[31] = 168;
        frame[32] = 1;
        frame[33] = 20;
        WriteUInt16(frame, 34, 12_345);
        WriteUInt16(frame, 36, 443);
        WriteUInt32(frame, 38, sequence);
        frame[46] = 0x50;
        frame[47] = 0x18;
        WriteUInt16(frame, 48, 65_535);
        for (var index = 0; index < payloadLength; index++)
        {
            frame[54 + index] = (byte)(index % 251);
        }

        return frame;
    }

    private static uint ReadTcpSequence(byte[] frame) =>
        ((uint)frame[38] << 24) |
        ((uint)frame[39] << 16) |
        ((uint)frame[40] << 8) |
        frame[41];

    private static ushort SumTcpChecksumWords(byte[] frame)
    {
        var tcpLength = frame.Length - 34;
        var pseudoHeader = new byte[12 + tcpLength];
        frame.AsSpan(26, 8).CopyTo(pseudoHeader);
        pseudoHeader[9] = 6;
        WriteUInt16(pseudoHeader, 10, (ushort)tcpLength);
        frame.AsSpan(34).CopyTo(pseudoHeader.AsSpan(12));
        return SumChecksumWords(pseudoHeader);
    }

    private static ushort SumChecksumWords(ReadOnlySpan<byte> bytes)
    {
        uint sum = 0;
        var index = 0;
        for (; index + 1 < bytes.Length; index += 2)
        {
            sum += (uint)((bytes[index] << 8) | bytes[index + 1]);
        }

        if (index < bytes.Length)
        {
            sum += (uint)(bytes[index] << 8);
        }

        while ((sum >> 16) != 0)
        {
            sum = (sum & 0xffff) + (sum >> 16);
        }

        return (ushort)~sum;
    }

    private static void WriteUInt16(byte[] bytes, int offset, ushort value)
    {
        bytes[offset] = (byte)(value >> 8);
        bytes[offset + 1] = (byte)value;
    }

    private static void WriteUInt32(byte[] bytes, int offset, uint value)
    {
        bytes[offset] = (byte)(value >> 24);
        bytes[offset + 1] = (byte)(value >> 16);
        bytes[offset + 2] = (byte)(value >> 8);
        bytes[offset + 3] = (byte)value;
    }
}
