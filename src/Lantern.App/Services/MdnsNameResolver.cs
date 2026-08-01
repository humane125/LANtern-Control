using System.Net;
using System.Net.Sockets;
using System.Text;
using System.IO;

namespace Lantern.App.Services;

public static class MdnsNameResolver
{
    private const int MdnsPort = 5353;
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
            client.Connect(address, MdnsPort);
            var query = BuildReverseLookupQuery(
                address,
                (ushort)Random.Shared.Next(ushort.MaxValue + 1));
            await client.SendAsync(query, cancellationToken);
            var reply = await client.ReceiveAsync(cancellationToken)
                .AsTask()
                .WaitAsync(ReplyTimeout, cancellationToken);
            return ParsePtrResponse(reply.Buffer);
        }
        catch (Exception exception) when (
            exception is SocketException or TimeoutException or OperationCanceledException)
        {
            return null;
        }
    }

    public static string? ParsePtrResponse(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < 12)
        {
            return null;
        }

        var questionCount = ReadUInt16(packet, 4);
        var recordCount = ReadUInt16(packet, 6) +
                          ReadUInt16(packet, 8) +
                          ReadUInt16(packet, 10);
        var offset = 12;
        for (var index = 0; index < questionCount; index++)
        {
            if (!TryReadName(packet, ref offset, out _) || offset + 4 > packet.Length)
            {
                return null;
            }

            offset += 4;
        }

        for (var index = 0; index < recordCount; index++)
        {
            if (!TryReadName(packet, ref offset, out _) || offset + 10 > packet.Length)
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

            if (type == 12)
            {
                var dataOffset = offset;
                if (TryReadName(packet, ref dataOffset, out var target))
                {
                    var normalized = NormalizeHostName(target);
                    if (normalized is not null)
                    {
                        return normalized;
                    }
                }
            }

            offset += dataLength;
        }

        return null;
    }

    private static byte[] BuildReverseLookupQuery(IPAddress address, ushort transactionId)
    {
        var labels = address.GetAddressBytes()
            .Reverse()
            .Select(value => value.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Concat(["in-addr", "arpa"])
            .ToArray();
        using var stream = new MemoryStream();
        WriteUInt16(stream, transactionId);
        WriteUInt16(stream, 0);
        WriteUInt16(stream, 1);
        WriteUInt16(stream, 0);
        WriteUInt16(stream, 0);
        WriteUInt16(stream, 0);
        foreach (var label in labels)
        {
            var bytes = Encoding.ASCII.GetBytes(label);
            stream.WriteByte((byte)bytes.Length);
            stream.Write(bytes);
        }

        stream.WriteByte(0);
        WriteUInt16(stream, 12);
        WriteUInt16(stream, 1);
        return stream.ToArray();
    }

    private static bool TryReadName(
        ReadOnlySpan<byte> packet,
        ref int offset,
        out string name)
    {
        var labels = new List<string>();
        var cursor = offset;
        var resumeOffset = -1;
        var jumps = 0;
        while (cursor < packet.Length && jumps <= packet.Length)
        {
            var length = packet[cursor++];
            if (length == 0)
            {
                offset = resumeOffset >= 0 ? resumeOffset : cursor;
                name = string.Join('.', labels);
                return true;
            }

            if ((length & 0xc0) == 0xc0)
            {
                if (cursor >= packet.Length)
                {
                    break;
                }

                var pointer = ((length & 0x3f) << 8) | packet[cursor++];
                if (pointer >= packet.Length)
                {
                    break;
                }

                resumeOffset = resumeOffset >= 0 ? resumeOffset : cursor;
                cursor = pointer;
                jumps++;
                continue;
            }

            if ((length & 0xc0) != 0 || cursor + length > packet.Length)
            {
                break;
            }

            labels.Add(Encoding.UTF8.GetString(packet.Slice(cursor, length)));
            cursor += length;
        }

        name = string.Empty;
        return false;
    }

    private static string? NormalizeHostName(string value)
    {
        var name = value.Trim().TrimEnd('.');
        if (name.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^6];
        }

        return name.Length is > 0 and <= 253 ? name : null;
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> bytes, int offset) =>
        (ushort)((bytes[offset] << 8) | bytes[offset + 1]);

    private static void WriteUInt16(Stream stream, ushort value)
    {
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)value);
    }
}
