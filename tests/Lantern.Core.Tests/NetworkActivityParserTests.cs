using System.Net;
using System.Text;
using Lantern.Core.Networking;

namespace Lantern.Core.Tests;

public sealed class NetworkActivityParserTests
{
    [Fact]
    public void TryParseOutbound_ReadsDnsQueryDomain()
    {
        var query = BuildDnsQuery("www.youtube.com");
        var frame = BuildUdpFrame(53, query);

        var parsed = NetworkActivityParser.TryParseOutbound(frame, out var observation);

        Assert.True(parsed);
        Assert.Equal("www.youtube.com", observation.Domain);
        Assert.Equal(DomainObservationSource.Dns, observation.Source);
    }

    [Fact]
    public void TryParseOutbound_ReadsTlsServerName()
    {
        var frame = BuildTcpFrame(443, BuildTlsClientHello("discord.com"));

        var parsed = NetworkActivityParser.TryParseOutbound(frame, out var observation);

        Assert.True(parsed);
        Assert.Equal("discord.com", observation.Domain);
        Assert.Equal(DomainObservationSource.Tls, observation.Source);
    }

    [Fact]
    public void TryParseOutbound_ReadsTlsServerNameFromFirstFragmentOfClientHello()
    {
        var payload = BuildTlsClientHello("graph.facebook.com");
        var declaredRecordLength = payload.Length - 5 + 128;
        payload[3] = (byte)(declaredRecordLength >> 8);
        payload[4] = (byte)declaredRecordLength;
        var declaredHandshakeLength = payload.Length - 9 + 128;
        payload[6] = (byte)(declaredHandshakeLength >> 16);
        payload[7] = (byte)(declaredHandshakeLength >> 8);
        payload[8] = (byte)declaredHandshakeLength;
        var frame = BuildTcpFrame(443, payload);

        var parsed = NetworkActivityParser.TryParseOutbound(frame, out var observation);

        Assert.True(parsed);
        Assert.Equal("graph.facebook.com", observation.Domain);
        Assert.Equal(DomainObservationSource.Tls, observation.Source);
    }

    [Fact]
    public void TryParseOutbound_ReadsPlainHttpHostWithoutPort()
    {
        var request = Encoding.ASCII.GetBytes(
            "GET /news HTTP/1.1\r\nHost: example.com:8080\r\nConnection: close\r\n\r\n");
        var frame = BuildTcpFrame(80, request);

        var parsed = NetworkActivityParser.TryParseOutbound(frame, out var observation);

        Assert.True(parsed);
        Assert.Equal("example.com", observation.Domain);
        Assert.Equal(DomainObservationSource.Http, observation.Source);
    }

    [Fact]
    public void TryParseOutbound_IgnoresMalformedOrUnrelatedTraffic()
    {
        var frame = BuildUdpFrame(123, [1, 2, 3, 4]);

        Assert.False(NetworkActivityParser.TryParseOutbound(frame, out _));
        Assert.False(NetworkActivityParser.TryParseOutbound([1, 2, 3], out _));
    }

    private static byte[] BuildDnsQuery(string domain)
    {
        var bytes = new List<byte>
        {
            0x12, 0x34,
            0x01, 0x00,
            0x00, 0x01,
            0x00, 0x00,
            0x00, 0x00,
            0x00, 0x00,
        };
        foreach (var label in domain.Split('.'))
        {
            bytes.Add((byte)label.Length);
            bytes.AddRange(Encoding.ASCII.GetBytes(label));
        }

        bytes.Add(0);
        bytes.AddRange([0, 1, 0, 1]);
        return [.. bytes];
    }

    private static byte[] BuildTlsClientHello(string domain)
    {
        var host = Encoding.ASCII.GetBytes(domain);
        var serverName = new List<byte>
        {
            0x00, (byte)(host.Length + 3),
            0x00, 0x00, (byte)host.Length,
        };
        serverName.AddRange(host);
        var extension = new List<byte>
        {
            0x00, 0x00,
            0x00, (byte)serverName.Count,
        };
        extension.AddRange(serverName);

        var body = new List<byte> { 0x03, 0x03 };
        body.AddRange(new byte[32]);
        body.Add(0);
        body.AddRange([0, 2, 0x13, 0x01]);
        body.AddRange([1, 0]);
        body.AddRange([(byte)(extension.Count >> 8), (byte)extension.Count]);
        body.AddRange(extension);

        var handshake = new List<byte>
        {
            0x01,
            (byte)(body.Count >> 16),
            (byte)(body.Count >> 8),
            (byte)body.Count,
        };
        handshake.AddRange(body);
        var record = new List<byte>
        {
            0x16, 0x03, 0x01,
            (byte)(handshake.Count >> 8),
            (byte)handshake.Count,
        };
        record.AddRange(handshake);
        return [.. record];
    }

    private static byte[] BuildUdpFrame(ushort destinationPort, byte[] payload) =>
        BuildIpv4Frame(17, 8, destinationPort, payload);

    private static byte[] BuildTcpFrame(ushort destinationPort, byte[] payload) =>
        BuildIpv4Frame(6, 20, destinationPort, payload);

    private static byte[] BuildIpv4Frame(
        byte protocol,
        int transportHeaderLength,
        ushort destinationPort,
        byte[] payload)
    {
        const int ethernetLength = 14;
        const int ipv4Length = 20;
        var frame = new byte[ethernetLength + ipv4Length + transportHeaderLength + payload.Length];
        Convert.FromHexString("345A6063C052E261190DBD540800").CopyTo(frame, 0);
        frame[14] = 0x45;
        var totalLength = ipv4Length + transportHeaderLength + payload.Length;
        frame[16] = (byte)(totalLength >> 8);
        frame[17] = (byte)totalLength;
        frame[22] = 64;
        frame[23] = protocol;
        IPAddress.Parse("192.168.31.61").GetAddressBytes().CopyTo(frame, 26);
        IPAddress.Parse("1.1.1.1").GetAddressBytes().CopyTo(frame, 30);
        var transportOffset = ethernetLength + ipv4Length;
        frame[transportOffset] = 0xcf;
        frame[transportOffset + 1] = 0x08;
        frame[transportOffset + 2] = (byte)(destinationPort >> 8);
        frame[transportOffset + 3] = (byte)destinationPort;
        if (protocol == 6)
        {
            frame[transportOffset + 12] = 0x50;
        }
        else
        {
            var udpLength = transportHeaderLength + payload.Length;
            frame[transportOffset + 4] = (byte)(udpLength >> 8);
            frame[transportOffset + 5] = (byte)udpLength;
        }

        payload.CopyTo(frame, transportOffset + transportHeaderLength);
        return frame;
    }
}
