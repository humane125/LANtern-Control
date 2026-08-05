using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using Lantern.Core.Control;
using Lantern.Core.Networking;

namespace Lantern.Core.Tests;

public sealed class FrameRouterTests
{
    private static readonly PhysicalAddress LocalMac = PhysicalAddress.Parse("345A6063C052");
    private static readonly PhysicalAddress GatewayMac = PhysicalAddress.Parse("64644A380A15");
    private static readonly PhysicalAddress ClientMac = PhysicalAddress.Parse("E261190DBD54");
    private static readonly IPAddress LocalIp = IPAddress.Parse("192.168.31.247");
    private static readonly IPAddress ClientIp = IPAddress.Parse("192.168.31.61");

    [Fact]
    public void Route_UploadRewritesFrameTowardGateway()
    {
        var frame = BuildIpv4Frame(
            destinationMac: LocalMac,
            sourceMac: ClientMac,
            sourceIp: ClientIp,
            destinationIp: IPAddress.Parse("1.1.1.1"),
            payloadBytes: 100);
        var router = CreateRouter();

        var result = router.Route(frame);

        Assert.Equal(FrameAction.Forward, result.Action);
        Assert.Equal(TrafficDirection.Upload, result.Direction);
        Assert.Equal(ClientMac, result.ClientMac);
        Assert.Equal(GatewayMac.GetAddressBytes(), result.Frame![..6]);
        Assert.Equal(LocalMac.GetAddressBytes(), result.Frame[6..12]);
    }

    [Fact]
    public void Route_DownloadRewritesFrameTowardClient()
    {
        var frame = BuildIpv4Frame(
            destinationMac: LocalMac,
            sourceMac: GatewayMac,
            sourceIp: IPAddress.Parse("1.1.1.1"),
            destinationIp: ClientIp,
            payloadBytes: 100);
        var router = CreateRouter();

        var result = router.Route(frame);

        Assert.Equal(FrameAction.Forward, result.Action);
        Assert.Equal(TrafficDirection.Download, result.Direction);
        Assert.Equal(ClientMac.GetAddressBytes(), result.Frame![..6]);
        Assert.Equal(LocalMac.GetAddressBytes(), result.Frame[6..12]);
    }

    [Fact]
    public void Route_DownloadMetersTcpPayloadWithoutNetworkHeaders()
    {
        var frame = BuildTcpIpv4Frame(
            destinationMac: LocalMac,
            sourceMac: GatewayMac,
            sourceIp: IPAddress.Parse("1.1.1.1"),
            destinationIp: ClientIp,
            applicationPayloadBytes: 100);
        var router = CreateRouter();

        var result = router.Route(frame);

        Assert.Equal(FrameAction.Forward, result.Action);
        Assert.Equal(100, result.MeteredByteCount);
    }

    [Fact]
    public void Route_PausedClientDropsFrame()
    {
        var policy = new TrafficPolicy();
        policy.SetRule(ClientMac.ToString(), new TrafficRule(true, 0, 0));
        var router = CreateRouter(policy);
        var frame = BuildIpv4Frame(
            LocalMac,
            ClientMac,
            ClientIp,
            IPAddress.Parse("8.8.8.8"),
            40);

        var result = router.Route(frame);

        Assert.Equal(FrameAction.Drop, result.Action);
        Assert.Equal(0, result.MeteredByteCount);
        Assert.Null(result.Frame);
    }

    [Fact]
    public void Route_ExternalPacingDefersRateLimitButStillDropsPausedTraffic()
    {
        var policy = new TrafficPolicy();
        policy.SetRule(ClientMac.ToString(), new TrafficRule(false, 1, 1));
        var router = CreateRouter(policy, enforceRateLimits: false);
        var frame = BuildIpv4Frame(
            LocalMac,
            ClientMac,
            ClientIp,
            IPAddress.Parse("8.8.8.8"),
            2_000);

        Assert.Equal(FrameAction.Forward, router.Route(frame).Action);

        policy.SetRule(ClientMac.ToString(), new TrafficRule(true, 0, 0));

        Assert.Equal(FrameAction.Drop, router.Route(frame).Action);
    }

    [Fact]
    public void Route_BlockedDomainDropsOnlyTheMatchingClientsOutboundRequest()
    {
        var policy = new TrafficPolicy();
        policy.SetBlockedDomains(ClientMac.ToString(), ["youtube.com"]);
        var router = CreateRouter(policy);
        var frame = BuildDnsQueryFrame("www.youtube.com");

        var result = router.Route(frame);

        Assert.Equal(FrameAction.Drop, result.Action);
        Assert.Equal(TrafficDirection.Upload, result.Direction);
        Assert.Equal(ClientMac, result.ClientMac);
        Assert.Null(result.Frame);
    }

    [Fact]
    public void Route_DeviceWithDomainRulesDropsQuicSoAppsFallBackToInspectableTls()
    {
        var policy = new TrafficPolicy();
        policy.SetBlockedDomains(ClientMac.ToString(), ["youtube.com"]);
        var router = CreateRouter(policy);
        var frame = BuildUdpIpv4Frame(443, [0xc0, 0x00, 0x00, 0x00]);

        var result = router.Route(frame);

        Assert.Equal(FrameAction.Drop, result.Action);
        Assert.Equal(TrafficDirection.Upload, result.Direction);
        Assert.Null(result.Frame);
    }

    [Fact]
    public void Route_NewRuleStopsPreviouslyObservedTlsFlowInBothDirections()
    {
        var policy = new TrafficPolicy();
        var router = CreateRouter(policy);
        var remoteIp = IPAddress.Parse("157.240.0.35");
        const ushort clientPort = 51000;
        var clientHello = BuildTcpPayloadFrame(
            LocalMac,
            ClientMac,
            ClientIp,
            remoteIp,
            clientPort,
            443,
            BuildTlsClientHello("graph.facebook.com"));

        Assert.Equal(FrameAction.Forward, router.Route(clientHello).Action);

        policy.SetBlockedDomains(ClientMac.ToString(), ["facebook.com"]);
        var upload = BuildTcpPayloadFrame(
            LocalMac,
            ClientMac,
            ClientIp,
            remoteIp,
            clientPort,
            443,
            [0x17, 0x03, 0x03, 0x00, 0x20]);
        var download = BuildTcpPayloadFrame(
            LocalMac,
            GatewayMac,
            remoteIp,
            ClientIp,
            443,
            clientPort,
            [0x17, 0x03, 0x03, 0x00, 0x20]);

        Assert.Equal(FrameAction.Drop, router.Route(upload).Action);
        Assert.Equal(FrameAction.Drop, router.Route(download).Action);
    }

    [Fact]
    public void Route_ReusesTlsHostnameAndCanonicalKeyForReverseDownloadFlow()
    {
        var policy = new TrafficPolicy();
        policy.SetServiceRule(
            ClientMac.ToString(),
            "youtube",
            new ServiceTrafficRule(1_000, 0));
        var router = CreateRouter(policy);
        var remoteIp = IPAddress.Parse("142.250.186.110");
        const ushort clientPort = 51000;
        var clientHello = BuildTcpPayloadFrame(
            LocalMac,
            ClientMac,
            ClientIp,
            remoteIp,
            clientPort,
            443,
            BuildTlsClientHello("www.youtube.com"));
        var download = BuildTcpPayloadFrame(
            LocalMac,
            GatewayMac,
            remoteIp,
            ClientIp,
            443,
            clientPort,
            [0x17, 0x03, 0x03, 0x00, 0x20]);

        var uploadResult = router.Route(clientHello);
        var downloadResult = router.Route(download);

        Assert.Equal("www.youtube.com", uploadResult.AttributedDomain);
        Assert.Equal("www.youtube.com", downloadResult.AttributedDomain);
        Assert.Equal("youtube", uploadResult.ServiceId);
        Assert.Equal("youtube", downloadResult.ServiceId);
        Assert.NotNull(uploadResult.Flow);
        Assert.Equal(uploadResult.Flow, downloadResult.Flow);
        Assert.Equal(clientPort, downloadResult.Flow!.Value.ClientPort);
        Assert.Equal(remoteIp, downloadResult.Flow.Value.RemoteAddress);
    }

    [Fact]
    public void Route_LeavesServiceClassificationToInspectorWhenNoServiceLimitExists()
    {
        var router = CreateRouter();
        var clientHello = BuildTcpPayloadFrame(
            LocalMac,
            ClientMac,
            ClientIp,
            IPAddress.Parse("142.250.186.110"),
            51_000,
            443,
            BuildTlsClientHello("www.youtube.com"));

        var result = router.Route(clientHello);

        Assert.Equal("www.youtube.com", result.AttributedDomain);
        Assert.Null(result.ServiceId);
    }

    [Fact]
    public void Route_DnsObservationDoesNotBindResolverFlowToQueriedService()
    {
        var router = CreateRouter();

        var result = router.Route(BuildDnsQueryFrame("youtube.com"));

        Assert.Equal("youtube.com", result.Observation?.Domain);
        Assert.Null(result.AttributedDomain);
        Assert.Null(result.Flow);
    }

    [Theory]
    [InlineData("rr1.googlevideo.com", "youtube")]
    [InlineData("video.xx.fbcdn.net", "facebook")]
    [InlineData("i.cdninstagram.com", "instagram")]
    public void Route_DnsAnswerAttributesLaterQuicFlowToResolvedServiceDomain(
        string domain,
        string expectedService)
    {
        var router = CreateRouter();
        var serviceAddress = IPAddress.Parse("142.250.186.110");

        var dnsResult = router.Route(BuildDnsResponseFrame(domain, serviceAddress));
        var result = router.Route(BuildUdpPayloadFrame(
            LocalMac,
            ClientMac,
            ClientIp,
            serviceAddress,
            53000,
            443,
            [0xc0, 0x00, 0x00, 0x00]));

        Assert.Null(dnsResult.Flow);
        Assert.Equal(FrameAction.Forward, result.Action);
        Assert.Equal(domain, result.AttributedDomain);
        Assert.Equal(
            expectedService,
            Lantern.Core.Services.ServiceDefinitionCatalog.MatchDomain(result.AttributedDomain).Id);
        Assert.NotNull(result.Flow);
    }

    [Fact]
    public void Route_ExplicitTlsHostnameTakesPriorityOverResolvedAddressCache()
    {
        var router = CreateRouter();
        var sharedAddress = IPAddress.Parse("157.240.0.35");
        const ushort clientPort = 53000;

        _ = router.Route(BuildDnsResponseFrame("video.xx.fbcdn.net", sharedAddress));
        _ = router.Route(BuildTcpPayloadFrame(
            LocalMac,
            ClientMac,
            ClientIp,
            sharedAddress,
            clientPort,
            443,
            BuildTlsClientHello("www.youtube.com")));
        var result = router.Route(BuildTcpPayloadFrame(
            LocalMac,
            ClientMac,
            ClientIp,
            sharedAddress,
            clientPort,
            443,
            [0x17, 0x03, 0x03, 0x00, 0x20]));

        Assert.Equal("www.youtube.com", result.AttributedDomain);
    }

    [Fact]
    public void Route_LocalComputerTrafficIsIgnored()
    {
        var router = CreateRouter();
        var frame = BuildIpv4Frame(
            GatewayMac,
            LocalMac,
            LocalIp,
            IPAddress.Parse("8.8.8.8"),
            40);

        var result = router.Route(frame);

        Assert.Equal(FrameAction.Ignore, result.Action);
    }

    [Fact]
    public void UpdateClient_RemovesStaleAddressForTheSameMac()
    {
        var router = CreateRouter();
        var latestAddress = IPAddress.Parse("192.168.31.213");
        router.UpdateClient(latestAddress, ClientMac);
        var oldAddressFrame = BuildIpv4Frame(
            LocalMac,
            GatewayMac,
            IPAddress.Parse("1.1.1.1"),
            ClientIp,
            100);
        var latestAddressFrame = BuildIpv4Frame(
            LocalMac,
            GatewayMac,
            IPAddress.Parse("1.1.1.1"),
            latestAddress,
            100);

        Assert.Equal(FrameAction.Ignore, router.Route(oldAddressFrame).Action);
        Assert.Equal(FrameAction.Forward, router.Route(latestAddressFrame).Action);
    }

    private static FrameRouter CreateRouter(
        TrafficPolicy? policy = null,
        bool enforceRateLimits = true) =>
        new(
            LocalMac,
            LocalIp,
            GatewayMac,
            new Dictionary<IPAddress, PhysicalAddress> { [ClientIp] = ClientMac },
            policy ?? new TrafficPolicy(),
            enforceRateLimits);

    private static byte[] BuildIpv4Frame(
        PhysicalAddress destinationMac,
        PhysicalAddress sourceMac,
        IPAddress sourceIp,
        IPAddress destinationIp,
        int payloadBytes)
    {
        var frame = new byte[14 + 20 + payloadBytes];
        destinationMac.GetAddressBytes().CopyTo(frame, 0);
        sourceMac.GetAddressBytes().CopyTo(frame, 6);
        frame[12] = 0x08;
        frame[13] = 0x00;
        frame[14] = 0x45;
        var totalLength = 20 + payloadBytes;
        frame[16] = (byte)(totalLength >> 8);
        frame[17] = (byte)totalLength;
        frame[22] = 64;
        frame[23] = 6;
        sourceIp.GetAddressBytes().CopyTo(frame, 26);
        destinationIp.GetAddressBytes().CopyTo(frame, 30);
        return frame;
    }

    private static byte[] BuildTcpIpv4Frame(
        PhysicalAddress destinationMac,
        PhysicalAddress sourceMac,
        IPAddress sourceIp,
        IPAddress destinationIp,
        int applicationPayloadBytes)
    {
        const int ipv4HeaderBytes = 20;
        const int tcpHeaderBytes = 20;
        var frame = BuildIpv4Frame(
            destinationMac,
            sourceMac,
            sourceIp,
            destinationIp,
            tcpHeaderBytes + applicationPayloadBytes);
        frame[14 + ipv4HeaderBytes + 12] = 0x50;
        return frame;
    }

    private static byte[] BuildDnsQueryFrame(string domain)
    {
        var query = new List<byte>
        {
            0x12, 0x34, 0x01, 0x00, 0x00, 0x01,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        };
        foreach (var label in domain.Split('.'))
        {
            query.Add((byte)label.Length);
            query.AddRange(Encoding.ASCII.GetBytes(label));
        }

        query.Add(0);
        query.AddRange([0, 1, 0, 1]);
        const int udpHeaderBytes = 8;
        var frame = BuildIpv4Frame(
            LocalMac,
            ClientMac,
            ClientIp,
            IPAddress.Parse("1.1.1.1"),
            udpHeaderBytes + query.Count);
        frame[23] = 17;
        var udpOffset = 14 + 20;
        frame[udpOffset] = 0xcf;
        frame[udpOffset + 1] = 0x08;
        frame[udpOffset + 2] = 0;
        frame[udpOffset + 3] = 53;
        var udpLength = udpHeaderBytes + query.Count;
        frame[udpOffset + 4] = (byte)(udpLength >> 8);
        frame[udpOffset + 5] = (byte)udpLength;
        query.CopyTo(frame, udpOffset + udpHeaderBytes);
        return frame;
    }

    private static byte[] BuildUdpIpv4Frame(ushort destinationPort, byte[] payload)
    {
        return BuildUdpPayloadFrame(
            LocalMac,
            ClientMac,
            ClientIp,
            IPAddress.Parse("1.1.1.1"),
            0xcf08,
            destinationPort,
            payload);
    }

    private static byte[] BuildDnsResponseFrame(string domain, IPAddress resolvedAddress)
    {
        var response = new List<byte>
        {
            0x12, 0x34, 0x81, 0x80, 0x00, 0x01,
            0x00, 0x01, 0x00, 0x00, 0x00, 0x00,
        };
        foreach (var label in domain.Split('.'))
        {
            response.Add((byte)label.Length);
            response.AddRange(Encoding.ASCII.GetBytes(label));
        }

        response.Add(0);
        response.AddRange([0, 1, 0, 1]);
        response.AddRange([
            0xc0, 0x0c,
            0x00, 0x01,
            0x00, 0x01,
            0x00, 0x00, 0x00, 0x3c,
            0x00, 0x04,
        ]);
        response.AddRange(resolvedAddress.GetAddressBytes());
        return BuildUdpPayloadFrame(
            LocalMac,
            GatewayMac,
            IPAddress.Parse("1.1.1.1"),
            ClientIp,
            53,
            0xcf08,
            [.. response]);
    }

    private static byte[] BuildUdpPayloadFrame(
        PhysicalAddress destinationMac,
        PhysicalAddress sourceMac,
        IPAddress sourceIp,
        IPAddress destinationIp,
        ushort sourcePort,
        ushort destinationPort,
        byte[] payload)
    {
        const int udpHeaderBytes = 8;
        var frame = BuildIpv4Frame(
            destinationMac,
            sourceMac,
            sourceIp,
            destinationIp,
            udpHeaderBytes + payload.Length);
        frame[23] = 17;
        var udpOffset = 14 + 20;
        frame[udpOffset] = (byte)(sourcePort >> 8);
        frame[udpOffset + 1] = (byte)sourcePort;
        frame[udpOffset + 2] = (byte)(destinationPort >> 8);
        frame[udpOffset + 3] = (byte)destinationPort;
        var udpLength = udpHeaderBytes + payload.Length;
        frame[udpOffset + 4] = (byte)(udpLength >> 8);
        frame[udpOffset + 5] = (byte)udpLength;
        payload.CopyTo(frame, udpOffset + udpHeaderBytes);
        return frame;
    }

    private static byte[] BuildTcpPayloadFrame(
        PhysicalAddress destinationMac,
        PhysicalAddress sourceMac,
        IPAddress sourceIp,
        IPAddress destinationIp,
        ushort sourcePort,
        ushort destinationPort,
        byte[] payload)
    {
        const int tcpHeaderBytes = 20;
        var frame = BuildIpv4Frame(
            destinationMac,
            sourceMac,
            sourceIp,
            destinationIp,
            tcpHeaderBytes + payload.Length);
        var tcpOffset = 14 + 20;
        frame[tcpOffset] = (byte)(sourcePort >> 8);
        frame[tcpOffset + 1] = (byte)sourcePort;
        frame[tcpOffset + 2] = (byte)(destinationPort >> 8);
        frame[tcpOffset + 3] = (byte)destinationPort;
        frame[tcpOffset + 12] = 0x50;
        frame[tcpOffset + 13] = 0x18;
        payload.CopyTo(frame, tcpOffset + tcpHeaderBytes);
        return frame;
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
}
