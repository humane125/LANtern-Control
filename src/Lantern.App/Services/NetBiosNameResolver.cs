using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Lantern.App.Services;

public static class NetBiosNameResolver
{
    private const int NodeStatusPort = 137;
    private static readonly TimeSpan ReplyTimeout = TimeSpan.FromMilliseconds(750);

    public static async Task<string?> ResolveAsync(
        IPAddress address,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            return null;
        }

        try
        {
            using var client = new UdpClient(AddressFamily.InterNetwork);
            client.Connect(address, NodeStatusPort);
            var query = BuildNodeStatusQuery((ushort)Random.Shared.Next(ushort.MaxValue + 1));
            await client.SendAsync(query, cancellationToken);
            var reply = await client.ReceiveAsync(cancellationToken)
                .AsTask()
                .WaitAsync(ReplyTimeout, cancellationToken);
            return ParseNodeStatusResponse(reply.Buffer);
        }
        catch (Exception exception) when (
            exception is SocketException or TimeoutException or OperationCanceledException)
        {
            return null;
        }
    }

    public static string? ParseNodeStatusResponse(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < 12)
        {
            return null;
        }

        var questionCount = ReadUInt16(packet, 4);
        var answerCount = ReadUInt16(packet, 6);
        var offset = 12;
        for (var index = 0; index < questionCount; index++)
        {
            if (!TrySkipName(packet, ref offset) || offset + 4 > packet.Length)
            {
                return null;
            }

            offset += 4;
        }

        for (var answer = 0; answer < answerCount; answer++)
        {
            if (!TrySkipName(packet, ref offset) || offset + 10 > packet.Length)
            {
                return null;
            }

            var type = ReadUInt16(packet, offset);
            var dataLength = ReadUInt16(packet, offset + 8);
            offset += 10;
            if (offset + dataLength > packet.Length)
            {
                return null;
            }

            if (type == 0x0021)
            {
                var name = ParseNodeStatusData(packet.Slice(offset, dataLength));
                if (!string.IsNullOrWhiteSpace(name))
                {
                    return name;
                }
            }

            offset += dataLength;
        }

        return null;
    }

    private static byte[] BuildNodeStatusQuery(ushort transactionId)
    {
        var query = new byte[50];
        WriteUInt16(query, 0, transactionId);
        WriteUInt16(query, 4, 1);
        query[12] = 32;
        Span<byte> netBiosName = stackalloc byte[16];
        netBiosName[0] = (byte)'*';
        for (var index = 0; index < netBiosName.Length; index++)
        {
            query[13 + (index * 2)] = (byte)('A' + (netBiosName[index] >> 4));
            query[14 + (index * 2)] = (byte)('A' + (netBiosName[index] & 0x0f));
        }

        query[45] = 0;
        WriteUInt16(query, 46, 0x0021);
        WriteUInt16(query, 48, 0x0001);
        return query;
    }

    private static string? ParseNodeStatusData(ReadOnlySpan<byte> data)
    {
        if (data.Length < 1)
        {
            return null;
        }

        var count = data[0];
        for (var index = 0; index < count; index++)
        {
            var entryOffset = 1 + (index * 18);
            if (entryOffset + 18 > data.Length)
            {
                return null;
            }

            var suffix = data[entryOffset + 15];
            var flags = ReadUInt16(data, entryOffset + 16);
            var isGroup = (flags & 0x8000) != 0;
            if (suffix != 0 || isGroup)
            {
                continue;
            }

            var name = Encoding.ASCII.GetString(data.Slice(entryOffset, 15)).Trim();
            if (name.Length > 0 && name != "*")
            {
                return name;
            }
        }

        return null;
    }

    private static bool TrySkipName(ReadOnlySpan<byte> packet, ref int offset)
    {
        while (offset < packet.Length)
        {
            var length = packet[offset++];
            if (length == 0)
            {
                return true;
            }

            if ((length & 0xc0) == 0xc0)
            {
                if (offset >= packet.Length)
                {
                    return false;
                }

                offset++;
                return true;
            }

            if ((length & 0xc0) != 0 || offset + length > packet.Length)
            {
                return false;
            }

            offset += length;
        }

        return false;
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> bytes, int offset) =>
        (ushort)((bytes[offset] << 8) | bytes[offset + 1]);

    private static void WriteUInt16(Span<byte> bytes, int offset, ushort value)
    {
        bytes[offset] = (byte)(value >> 8);
        bytes[offset + 1] = (byte)value;
    }
}
