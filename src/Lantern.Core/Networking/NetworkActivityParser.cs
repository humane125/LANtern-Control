using System.Net;
using System.Text;

namespace Lantern.Core.Networking;

public enum DomainObservationSource
{
    Dns,
    Tls,
    Http,
}

public readonly record struct DomainObservation(
    string Domain,
    DomainObservationSource Source,
    IPAddress DestinationAddress);

public readonly record struct TransportFlow(
    IPAddress SourceAddress,
    ushort SourcePort,
    IPAddress DestinationAddress,
    ushort DestinationPort,
    byte Protocol);

internal readonly record struct DnsResolvedAddress(
    IPAddress Address,
    TimeSpan Lifetime);

internal readonly record struct DnsResolution(
    string Domain,
    IReadOnlyList<DnsResolvedAddress> Addresses);

public static class NetworkActivityParser
{
    private const ushort EtherTypeIpv4 = 0x0800;
    private const ushort EtherTypeVlan = 0x8100;

    public static bool TryParseOutbound(
        ReadOnlySpan<byte> frame,
        out DomainObservation observation)
    {
        observation = default;
        if (!TryGetTransportPayload(
                frame,
                out var protocol,
                out _,
                out var destinationPort,
                out _,
                out var destinationAddress,
                out var payload))
        {
            return false;
        }

        string? domain = null;
        DomainObservationSource source = default;
        if (destinationPort == 53 && TryReadDnsQuery(payload, protocol == 6, out domain))
        {
            source = DomainObservationSource.Dns;
        }
        else if (protocol == 6 && TryReadTlsServerName(payload, out domain))
        {
            source = DomainObservationSource.Tls;
        }
        else if (protocol == 6 &&
                 destinationPort is 80 or 8000 or 8080 or 8888 &&
                 TryReadHttpHost(payload, out domain))
        {
            source = DomainObservationSource.Http;
        }

        if (!TryNormalizeDomain(domain, out var normalized))
        {
            return false;
        }

        observation = new DomainObservation(normalized, source, destinationAddress);
        return true;
    }

    public static bool IsOutboundUdpToPort(ReadOnlySpan<byte> frame, ushort port) =>
        TryGetTransportPayload(
            frame,
            out var protocol,
            out _,
            out var destinationPort,
            out _,
            out _,
            out _) &&
        protocol == 17 &&
        destinationPort == port;

    public static bool TryParseTransportFlow(
        ReadOnlySpan<byte> frame,
        out TransportFlow flow)
    {
        flow = default;
        if (!TryGetTransportPayload(
                frame,
                out var protocol,
                out var sourcePort,
                out var destinationPort,
                out var sourceAddress,
                out var destinationAddress,
                out _))
        {
            return false;
        }

        flow = new TransportFlow(
            sourceAddress,
            sourcePort,
            destinationAddress,
            destinationPort,
            protocol);
        return true;
    }

    internal static bool TryParseDnsResponse(
        ReadOnlySpan<byte> frame,
        out DnsResolution resolution)
    {
        resolution = default;
        if (!TryGetTransportPayload(
                frame,
                out var protocol,
                out var sourcePort,
                out _,
                out _,
                out _,
                out var payload) ||
            sourcePort != 53)
        {
            return false;
        }

        if (protocol == 6)
        {
            if (payload.Length < 2)
            {
                return false;
            }

            var messageLength = ReadUInt16(payload, 0);
            payload = payload[2..];
            if (messageLength > payload.Length)
            {
                return false;
            }

            payload = payload[..messageLength];
        }

        if (payload.Length < 12 ||
            (ReadUInt16(payload, 2) & 0x8000) == 0 ||
            ReadUInt16(payload, 4) == 0 ||
            ReadUInt16(payload, 6) == 0 ||
            !TryReadDnsName(payload, 12, out var domain, out var offset) ||
            offset + 4 > payload.Length ||
            !TryNormalizeDomain(domain, out var normalizedDomain))
        {
            return false;
        }

        offset += 4;
        var answerCount = ReadUInt16(payload, 6);
        var addresses = new List<DnsResolvedAddress>(answerCount);
        for (var index = 0; index < answerCount; index++)
        {
            if (!TrySkipDnsName(payload, offset, out offset) || offset + 10 > payload.Length)
            {
                return false;
            }

            var type = ReadUInt16(payload, offset);
            var dnsClass = ReadUInt16(payload, offset + 2);
            var ttlSeconds = ReadUInt32(payload, offset + 4);
            var dataLength = ReadUInt16(payload, offset + 8);
            offset += 10;
            if (offset + dataLength > payload.Length)
            {
                return false;
            }

            if (type == 1 && dnsClass == 1 && dataLength == 4)
            {
                addresses.Add(new DnsResolvedAddress(
                    new IPAddress(payload.Slice(offset, 4)),
                    TimeSpan.FromSeconds(Math.Clamp(ttlSeconds, 1u, 3600u))));
            }

            offset += dataLength;
        }

        resolution = new DnsResolution(normalizedDomain, addresses);
        return true;
    }

    private static bool TryGetTransportPayload(
        ReadOnlySpan<byte> frame,
        out byte protocol,
        out ushort sourcePort,
        out ushort destinationPort,
        out IPAddress sourceAddress,
        out IPAddress destinationAddress,
        out ReadOnlySpan<byte> payload)
    {
        protocol = 0;
        sourcePort = 0;
        destinationPort = 0;
        sourceAddress = IPAddress.None;
        destinationAddress = IPAddress.None;
        payload = default;
        if (frame.Length < 14)
        {
            return false;
        }

        var etherType = ReadUInt16(frame, 12);
        var ipv4Offset = 14;
        if (etherType == EtherTypeVlan)
        {
            if (frame.Length < 18)
            {
                return false;
            }

            etherType = ReadUInt16(frame, 16);
            ipv4Offset = 18;
        }

        if (etherType != EtherTypeIpv4 ||
            frame.Length < ipv4Offset + 20 ||
            (frame[ipv4Offset] >> 4) != 4)
        {
            return false;
        }

        var ipv4HeaderLength = (frame[ipv4Offset] & 0x0f) * 4;
        var totalLength = ReadUInt16(frame, ipv4Offset + 2);
        if (ipv4HeaderLength < 20 ||
            totalLength < ipv4HeaderLength ||
            frame.Length < ipv4Offset + ipv4HeaderLength ||
            (ReadUInt16(frame, ipv4Offset + 6) & 0x1fff) != 0)
        {
            return false;
        }

        protocol = frame[ipv4Offset + 9];
        if (protocol is not 6 and not 17)
        {
            return false;
        }

        sourceAddress = new IPAddress(frame.Slice(ipv4Offset + 12, 4));
        destinationAddress = new IPAddress(frame.Slice(ipv4Offset + 16, 4));
        var transportOffset = ipv4Offset + ipv4HeaderLength;
        var networkEnd = ipv4Offset + Math.Min(totalLength, frame.Length - ipv4Offset);
        if (networkEnd < transportOffset + 8)
        {
            return false;
        }

        sourcePort = ReadUInt16(frame, transportOffset);
        destinationPort = ReadUInt16(frame, transportOffset + 2);
        int payloadOffset;
        if (protocol == 6)
        {
            if (networkEnd < transportOffset + 20)
            {
                return false;
            }

            var tcpHeaderLength = (frame[transportOffset + 12] >> 4) * 4;
            if (tcpHeaderLength < 20 || networkEnd < transportOffset + tcpHeaderLength)
            {
                return false;
            }

            payloadOffset = transportOffset + tcpHeaderLength;
        }
        else
        {
            payloadOffset = transportOffset + 8;
        }

        payload = frame.Slice(payloadOffset, networkEnd - payloadOffset);
        return payload.Length > 0;
    }

    private static bool TryReadDnsQuery(
        ReadOnlySpan<byte> payload,
        bool tcp,
        out string? domain)
    {
        domain = null;
        if (tcp)
        {
            if (payload.Length < 2)
            {
                return false;
            }

            var messageLength = ReadUInt16(payload, 0);
            payload = payload[2..];
            if (messageLength > payload.Length)
            {
                return false;
            }

            payload = payload[..messageLength];
        }

        if (payload.Length < 13 ||
            (ReadUInt16(payload, 2) & 0x8000) != 0 ||
            ReadUInt16(payload, 4) == 0)
        {
            return false;
        }

        return TryReadDnsName(payload, 12, out domain);
    }

    private static bool TryReadDnsName(
        ReadOnlySpan<byte> message,
        int offset,
        out string? domain)
        => TryReadDnsName(message, offset, out domain, out _);

    private static bool TryReadDnsName(
        ReadOnlySpan<byte> message,
        int offset,
        out string? domain,
        out int nextOffset)
    {
        domain = null;
        nextOffset = offset;
        var labels = new List<string>();
        var totalLength = 0;
        while (offset < message.Length)
        {
            var labelLength = message[offset++];
            if (labelLength == 0)
            {
                domain = string.Join('.', labels);
                nextOffset = offset;
                return labels.Count > 0;
            }

            if ((labelLength & 0xc0) != 0 ||
                labelLength > 63 ||
                offset + labelLength > message.Length ||
                totalLength + labelLength + 1 > 254)
            {
                return false;
            }

            labels.Add(Encoding.ASCII.GetString(message.Slice(offset, labelLength)));
            offset += labelLength;
            totalLength += labelLength + 1;
        }

        return false;
    }

    private static bool TrySkipDnsName(
        ReadOnlySpan<byte> message,
        int offset,
        out int nextOffset)
    {
        nextOffset = offset;
        while (offset < message.Length)
        {
            var labelLength = message[offset++];
            if (labelLength == 0)
            {
                nextOffset = offset;
                return true;
            }

            if ((labelLength & 0xc0) == 0xc0)
            {
                if (offset >= message.Length)
                {
                    return false;
                }

                nextOffset = offset + 1;
                return true;
            }

            if ((labelLength & 0xc0) != 0 ||
                labelLength > 63 ||
                offset + labelLength > message.Length)
            {
                return false;
            }

            offset += labelLength;
        }

        return false;
    }

    private static bool TryReadTlsServerName(
        ReadOnlySpan<byte> payload,
        out string? domain)
    {
        domain = null;
        if (payload.Length < 9 || payload[0] != 0x16 || payload[5] != 0x01)
        {
            return false;
        }

        var recordEnd = 5 + ReadUInt16(payload, 3);
        var handshakeLength = ReadUInt24(payload, 6);
        var handshakeEnd = 9 + handshakeLength;
        if (handshakeEnd > recordEnd || handshakeEnd < 44 || payload.Length < 44)
        {
            return false;
        }

        var availableHandshakeEnd = Math.Min(payload.Length, handshakeEnd);

        var offset = 9 + 2 + 32;
        var sessionIdLength = payload[offset++];
        if (!TryAdvance(ref offset, sessionIdLength, availableHandshakeEnd))
        {
            return false;
        }

        if (!TryReadLengthPrefixedBlock(payload, ref offset, availableHandshakeEnd, 2, out _))
        {
            return false;
        }

        if (!TryReadLengthPrefixedBlock(payload, ref offset, availableHandshakeEnd, 1, out _))
        {
            return false;
        }

        if (offset + 2 > availableHandshakeEnd)
        {
            return false;
        }

        var declaredExtensionsLength = ReadUInt16(payload, offset);
        offset += 2;
        var availableExtensionsLength = Math.Min(
            declaredExtensionsLength,
            availableHandshakeEnd - offset);
        var extensions = payload.Slice(offset, availableExtensionsLength);

        var extensionOffset = 0;
        while (extensionOffset + 4 <= extensions.Length)
        {
            var type = ReadUInt16(extensions, extensionOffset);
            var length = ReadUInt16(extensions, extensionOffset + 2);
            extensionOffset += 4;
            if (extensionOffset + length > extensions.Length)
            {
                return false;
            }

            if (type == 0 && TryReadServerNameExtension(
                    extensions.Slice(extensionOffset, length),
                    out domain))
            {
                return true;
            }

            extensionOffset += length;
        }

        return false;
    }

    private static bool TryReadServerNameExtension(
        ReadOnlySpan<byte> extension,
        out string? domain)
    {
        domain = null;
        if (extension.Length < 5)
        {
            return false;
        }

        var listLength = ReadUInt16(extension, 0);
        if (listLength + 2 > extension.Length)
        {
            return false;
        }

        var offset = 2;
        var end = 2 + listLength;
        while (offset + 3 <= end)
        {
            var nameType = extension[offset++];
            var nameLength = ReadUInt16(extension, offset);
            offset += 2;
            if (offset + nameLength > end)
            {
                return false;
            }

            if (nameType == 0)
            {
                domain = Encoding.ASCII.GetString(extension.Slice(offset, nameLength));
                return true;
            }

            offset += nameLength;
        }

        return false;
    }

    private static bool TryReadHttpHost(
        ReadOnlySpan<byte> payload,
        out string? domain)
    {
        domain = null;
        if (payload.Length < 16)
        {
            return false;
        }

        var text = Encoding.ASCII.GetString(payload[..Math.Min(payload.Length, 8192)]);
        var firstLineEnd = text.IndexOf("\r\n", StringComparison.Ordinal);
        if (firstLineEnd <= 0 ||
            !text.AsSpan(0, firstLineEnd).Contains(" HTTP/", StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var line in text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("Host:", StringComparison.OrdinalIgnoreCase))
            {
                domain = line[5..].Trim();
                return domain.Length > 0;
            }
        }

        return false;
    }

    private static bool TryNormalizeDomain(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        value = value.Trim().TrimEnd('.');
        var colon = value.LastIndexOf(':');
        if (colon > 0 && value.IndexOf(':') == colon)
        {
            value = value[..colon];
        }

        normalized = value.ToLowerInvariant();
        if (normalized.Length is 0 or > 253 || IPAddress.TryParse(normalized, out _))
        {
            normalized = string.Empty;
            return false;
        }

        foreach (var label in normalized.Split('.'))
        {
            if (label.Length is 0 or > 63 ||
                label[0] == '-' ||
                label[^1] == '-' ||
                label.Any(character =>
                    !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
            {
                normalized = string.Empty;
                return false;
            }
        }

        return normalized.Contains('.', StringComparison.Ordinal);
    }

    private static bool TryReadLengthPrefixedBlock(
        ReadOnlySpan<byte> source,
        ref int offset,
        int end,
        int lengthBytes,
        out ReadOnlySpan<byte> block)
    {
        block = default;
        if (offset + lengthBytes > end)
        {
            return false;
        }

        var length = lengthBytes == 1 ? source[offset] : ReadUInt16(source, offset);
        offset += lengthBytes;
        if (offset + length > end)
        {
            return false;
        }

        block = source.Slice(offset, length);
        offset += length;
        return true;
    }

    private static bool TryAdvance(ref int offset, int count, int end)
    {
        if (offset + count > end)
        {
            return false;
        }

        offset += count;
        return true;
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> bytes, int offset) =>
        (ushort)((bytes[offset] << 8) | bytes[offset + 1]);

    private static int ReadUInt24(ReadOnlySpan<byte> bytes, int offset) =>
        (bytes[offset] << 16) | (bytes[offset + 1] << 8) | bytes[offset + 2];

    private static uint ReadUInt32(ReadOnlySpan<byte> bytes, int offset) =>
        ((uint)bytes[offset] << 24) |
        ((uint)bytes[offset + 1] << 16) |
        ((uint)bytes[offset + 2] << 8) |
        bytes[offset + 3];
}
